using System.Globalization;
using System.Text;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace LocalProductManager.Api.Services;

public sealed class LiveStreamBidProductImportService(AppDbContext db)
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

        var rows = new Dictionary<string, ImportRow>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] fields;
            try { fields = parser.ReadFields() ?? []; }
            catch (MalformedLineException ex) { throw new InvalidOperationException($"Invalid CSV row near line {ex.LineNumber}."); }

            var uuid = NormalizeUuid(Value(fields, columns["UUID"]));
            var title = Value(fields, columns["Title"]).Trim();
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(title)) { skipped++; continue; }
            if (!int.TryParse(Value(fields, columns["Quantity"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity < 0 ||
                !TryDecimal(Value(fields, columns["Unit Price"]), out var unitPrice))
            { skipped++; continue; }

            var inventoryColumn = Array.FindIndex(headers, header => header == "inventory");
            var inventory = inventoryColumn >= 0 &&
                int.TryParse(Value(fields, inventoryColumn), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInventory)
                ? Math.Max(0, parsedInventory)
                : 1;
            var weightGrams = NullableDecimal(Value(fields, columns["Weight (g)"]));
            var totalCost = NullableDecimal(Value(fields, columns["Total Cost"]))
                ?? decimal.Round((unitPrice * quantity) + ((weightGrams ?? 0) * 0.05m), 2);
            rows[uuid] = new ImportRow(
                uuid, title, quantity, inventory, unitPrice, weightGrams,
                NullableDecimal(Value(fields, columns["Weight (oz)"])),
                NullIfEmpty(Value(fields, columns["Material"])), totalCost);
        }

        var lookupValues = rows.Keys
            .SelectMany(uuid => new[] { uuid, $"BID|{uuid}", $"BID | {uuid}" })
            .ToArray();
        var existingItems = await db.LiveStreamBidProducts
            .Where(item => lookupValues.Contains(item.Uuid))
            .ToListAsync(cancellationToken);
        var existing = existingItems
            .GroupBy(item => NormalizeUuid(item.Uuid), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;
        foreach (var row in rows.Values)
        {
            if (!existing.TryGetValue(row.Uuid, out var item))
            {
                item = new LiveStreamBidProduct { Uuid = row.Uuid, RequiresLocalLabel = false, ActualInventory = 0 };
                db.LiveStreamBidProducts.Add(item);
                created++;
            }
            else updated++;

            item.Uuid = row.Uuid;
            item.Title = row.Title;
            item.Quantity = row.Quantity;
            item.Inventory = row.Inventory;
            item.IsSold = row.Inventory == 0;
            if (!item.IsSold) item.SoldAt = null;
            item.UnitPrice = row.UnitPrice;
            item.WeightGrams = row.WeightGrams;
            item.WeightOunces = row.WeightOunces;
            item.Material = row.Material;
            item.TotalCost = row.TotalCost;
            item.ImportedFileName = Path.GetFileName(file.FileName);
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new LiveStreamBidImportResult(created, updated, skipped, created + updated + skipped);
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
    private sealed record ImportRow(string Uuid, string Title, int Quantity, int Inventory, decimal UnitPrice, decimal? WeightGrams, decimal? WeightOunces, string? Material, decimal? TotalCost);
}
