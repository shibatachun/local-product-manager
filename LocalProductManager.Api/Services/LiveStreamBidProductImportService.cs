using System.Globalization;
using System.Text;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace LocalProductManager.Api.Services;

public sealed class LiveStreamBidProductImportService(AppDbContext db, ExchangeRateService exchangeRates)
{
    public async Task<LiveStreamBidImportResult> ImportAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var parser = new TextFieldParser(stream, Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        if (parser.EndOfData) throw new InvalidOperationException("The CSV file is empty.");
        var headers = (parser.ReadFields() ?? []).Select(NormalizeHeader).ToArray();
        var columns = RequiredHeaders.ToDictionary(
            item => item.Key,
            item => Array.FindIndex(headers, header => item.Value.Contains(header)),
            StringComparer.OrdinalIgnoreCase);
        var missing = columns.Where(item => item.Value < 0).Select(item => item.Key).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Missing required CSV column(s): {string.Join(", ", missing)}.");

        var rows = new List<ImportRow>();
        var skipped = 0;
        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            cancellationToken.ThrowIfCancellationRequested();
            string[] fields;
            try { fields = parser.ReadFields() ?? []; }
            catch (MalformedLineException ex) { throw new InvalidOperationException($"Invalid CSV row near line {ex.LineNumber}."); }

            var uuid = NormalizeUuid(Value(fields, columns["UUID"]));
            var actualSkuColumn = Array.FindIndex(headers, header => header is "actualsku" or "sku");
            var actualSku = NullIfEmpty(Value(fields, actualSkuColumn));
            var title = Value(fields, columns["Title"]).Trim();
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(title)) { skipped++; continue; }
            if (!int.TryParse(Value(fields, columns["Quantity"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity < 0 ||
                !TryDecimal(Value(fields, columns["Unit Price"]), out var unitPrice))
            { skipped++; continue; }

            var actualInventoryColumn = Array.FindIndex(headers, header => header == "inventory");
            var expectedInventoryColumn = Array.FindIndex(headers, header => header == "expectedinventory");
            var actualInventory = ParseInventory(fields, actualInventoryColumn, 0);
            var expectedInventory = ParseInventory(fields, expectedInventoryColumn, 1);
            var weightGrams = NullableDecimal(Value(fields, columns["Weight (g)"]));
            var totalCost = NullableDecimal(Value(fields, columns["Total Cost"]))
                ?? decimal.Round((unitPrice * quantity) + ((weightGrams ?? 0) * 0.05m), 2);
            rows.Add(new ImportRow(
                rowNumber, uuid, actualSku, title, quantity, expectedInventory, actualInventory, unitPrice, weightGrams,
                NullableDecimal(Value(fields, columns["Weight (oz)"])),
                NullIfEmpty(Value(fields, columns["Material"])), totalCost));
        }

        var existingItems = await db.LiveStreamBidProducts.AsNoTracking()
            .Select(item => new { item.Uuid, item.ActualSku })
            .ToListAsync(cancellationToken);
        var existingUuids = existingItems.Select(item => NormalizeUuid(item.Uuid)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingSkuSet = existingItems.Where(item => item.ActualSku is not null)
            .Select(item => item.ActualSku!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateSkus = rows.Where(row => row.ActualSku is not null)
            .GroupBy(row => row.ActualSku!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<LiveStreamBidImportConflictDto>();
        var importableRows = new List<ImportRow>();
        var regeneratedRows = new List<(ImportRow Row, string OriginalUuid)>();
        var usedUuids = new HashSet<string>(existingUuids, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var reasons = new List<string>();
            if (row.ActualSku is not null && duplicateSkus.Contains(row.ActualSku)) reasons.Add("Actual SKU is duplicated in this CSV file.");
            if (row.ActualSku is not null && existingSkuSet.Contains(row.ActualSku)) reasons.Add("Actual SKU already exists in the database.");
            if (reasons.Count > 0)
                conflicts.Add(new(row.RowNumber, row.Uuid, row.ActualSku, row.Title, reasons));
            else
            {
                if (usedUuids.Add(row.Uuid))
                    importableRows.Add(row);
                else
                {
                    var replacement = NewUniqueUuid(usedUuids);
                    var changed = row with { Uuid = replacement };
                    importableRows.Add(changed);
                    regeneratedRows.Add((changed, row.Uuid));
                }
            }
        }

        var rate = await GetSafePricingExchangeRateAsync(cancellationToken);
        var created = 0;
        var updated = 0;
        var createdItemsByRow = new Dictionary<int, LiveStreamBidProduct>();
        var regeneratedRowNumbers = regeneratedRows.Select(item => item.Row.RowNumber).ToHashSet();
        foreach (var row in importableRows)
        {
            var item = new LiveStreamBidProduct { Uuid = row.Uuid, RequiresLocalLabel = regeneratedRowNumbers.Contains(row.RowNumber), ActualInventory = 0 };
            db.LiveStreamBidProducts.Add(item);
            createdItemsByRow[row.RowNumber] = item;
            created++;

            item.Uuid = row.Uuid;
            item.ActualSku = row.ActualSku;
            item.Title = row.Title;
            item.Quantity = row.Quantity;
            item.Inventory = row.ExpectedInventory;
            item.ActualInventory = row.ActualInventory;
            item.IsSold = row.ExpectedInventory == 0;
            if (!item.IsSold) item.SoldAt = null;
            item.UnitPrice = row.UnitPrice;
            item.WeightGrams = row.WeightGrams;
            item.WeightOunces = row.WeightOunces;
            item.Material = row.Material;
            item.TotalCost = row.TotalCost;
            item.WhatnotAuctionSafePrice = WhatnotSafePriceCalculator.Calculate(row.TotalCost, rate);
            item.ItemCode = WhatnotSafePriceCalculator.ToItemCode(item.WhatnotAuctionSafePrice);
            item.ImportedFileName = Path.GetFileName(file.FileName);
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        var regenerated = regeneratedRows.Select(change => new LiveStreamBidRegeneratedUuidDto(
            change.Row.RowNumber, createdItemsByRow[change.Row.RowNumber].Id,
            change.OriginalUuid, change.Row.Uuid, change.Row.Title)).ToArray();
        return new LiveStreamBidImportResult(created, updated, skipped, rows.Count + skipped, conflicts, regenerated);
    }

    private async Task<decimal> GetSafePricingExchangeRateAsync(CancellationToken cancellationToken)
    {
        try { return (await exchangeRates.GetRmbToUsdAsync(cancellationToken)).Rate; }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { return 0.15m; }
    }

    private static readonly Dictionary<string, string[]> RequiredHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UUID"] = ["uuid"], ["Title"] = ["title"], ["Quantity"] = ["quantity"],
        ["Unit Price"] = ["unitprice"], ["Weight (g)"] = ["weightg"],
        ["Weight (oz)"] = ["weightoz"], ["Material"] = ["material"], ["Total Cost"] = ["totalcost"]
    };
    private static string NormalizeHeader(string value) => value.Trim().TrimStart('\uFEFF').ToLowerInvariant()
        .Replace(" ", "").Replace("_", "").Replace("-", "").Replace("(", "").Replace(")", "");
    private static string Value(string[] fields, int index) => index >= 0 && index < fields.Length ? fields[index] : "";
    private static int ParseInventory(string[] fields, int index, int fallback) =>
        index >= 0 && int.TryParse(Value(fields, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value)
            : fallback;
    private static string NormalizeUuid(string value)
    {
        var normalized = value.Trim().TrimStart('\uFEFF');
        if (normalized.StartsWith("BID", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..].TrimStart(' ', '|', ':', '-');
        return normalized.Trim();
    }
    private static bool TryDecimal(string value, out decimal result) => decimal.TryParse(
        value.Trim().Replace("$", "").Replace("¥", "").Replace("￥", ""),
        NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    private static decimal? NullableDecimal(string value) => TryDecimal(value, out var result) ? result : null;
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NewUniqueUuid(ISet<string> usedUuids)
    {
        string value;
        do value = Guid.NewGuid().ToString().ToUpperInvariant();
        while (!usedUuids.Add(value));
        return value;
    }
    private sealed record ImportRow(int RowNumber, string Uuid, string? ActualSku, string Title, int Quantity, int ExpectedInventory, int ActualInventory, decimal UnitPrice, decimal? WeightGrams, decimal? WeightOunces, string? Material, decimal? TotalCost);
}
