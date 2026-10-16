using LocalProductManager.Api.Models;
using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LocalProductManager.Api.Services;

public sealed record ParsedWhatnotShow(
    string Sha256, DateOnly ShowDate, IReadOnlyList<LiveStreamBidShowOrder> Orders, int IgnoredRows);

public sealed class WhatnotShowCostImportService
{
    private static readonly string[] RequiredColumns =
    [
        "order_id", "buyer_username", "product_name", "product_quantity", "placed_at", "cancelled_or_failed"
    ];

    public ParsedWhatnotShow Read(Stream input)
    {
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        var data = bytes.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        using var stream = new MemoryStream(data);
        using var parser = new TextFieldParser(stream, Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("The Whatnot show report is empty.");
        if (headers.Length > 0) headers[0] = headers[0].TrimStart('\uFEFF');
        var columns = headers.Select((name, index) => (Name: name.Trim(), Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var missing = RequiredColumns.Where(name => !columns.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("Missing Whatnot show report columns: " + string.Join(", ", missing));

        var orders = new List<LiveStreamBidShowOrder>();
        var orderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateOnly? showDate = null;
        var ignored = 0;
        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            string[] fields;
            try { fields = parser.ReadFields() ?? []; }
            catch (MalformedLineException ex) { throw new InvalidDataException($"CSV row {rowNumber} is malformed: {ex.Message}", ex); }
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace)) continue;
            string Value(string name) => columns[name] < fields.Length ? fields[columns[name]].Trim() : "";
            if (!string.IsNullOrWhiteSpace(Value("cancelled_or_failed"))) { ignored++; continue; }

            var orderId = Value("order_id");
            var customer = Value("buyer_username");
            var orderName = Value("product_name");
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(customer) || string.IsNullOrWhiteSpace(orderName))
                throw new InvalidDataException($"CSV row {rowNumber} must include order_id, buyer_username, and product_name.");
            if (!int.TryParse(Value("product_quantity"), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
                throw new InvalidDataException($"CSV row {rowNumber} has an invalid product_quantity.");
            if (!orderIds.Add(orderId))
                throw new InvalidDataException($"CSV row {rowNumber} repeats order_id {orderId}; no data was imported.");
            var placedAt = Value("placed_at");
            if (placedAt.Length < 10 || !DateOnly.TryParseExact(placedAt[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rowDate))
                throw new InvalidDataException($"CSV row {rowNumber} has an invalid placed_at date.");
            if (showDate.HasValue && showDate.Value != rowDate)
                throw new InvalidDataException("The CSV contains orders from more than one calendar date. Import one show at a time.");
            showDate = rowDate;
            orders.Add(new LiveStreamBidShowOrder
            {
                ExternalOrderId = orderId,
                CustomerName = customer,
                OrderName = orderName,
                Quantity = quantity,
                PlacedAt = placedAt
            });
        }
        if (orders.Count == 0) throw new InvalidDataException("No paid orders were found in the Whatnot show report.");
        return new ParsedWhatnotShow(sha256, showDate!.Value, orders, ignored);
    }
}
