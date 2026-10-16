using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AuctionProductMonitor.Wpf;

public sealed record UnnumberedShowOrder(int RowNumber, string Buyer, string Title);
public sealed record WhatnotShowReportImport(
    string Sha256, string ShowDate, IReadOnlyList<SaleEvent> Sales, int IgnoredRows,
    IReadOnlyList<UnnumberedShowOrder> UnnumberedOrders);

public static partial class WhatnotShowReportImporter
{
    private static readonly string[] RequiredColumns =
    [
        "order_id", "buyer_username", "product_name", "product_quantity",
        "placed_at", "cancelled_or_failed", "shipment_id"
    ];

    [GeneratedRegex(@"#\s*(\d{1,6})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductNumberPattern();

    [GeneratedRegex(@"^\s*free\s+gift\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FreeGiftPattern();

    public static WhatnotShowReportImport Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        using var csv = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        csv.SetDelimiters(",");

        var headers = csv.ReadFields() ?? throw new InvalidDataException("The show report is empty.");
        if (headers.Length > 0) headers[0] = headers[0].TrimStart('\uFEFF');
        var columns = headers.Select((name, index) => (name: name.Trim(), index))
            .ToDictionary(entry => entry.name, entry => entry.index, StringComparer.OrdinalIgnoreCase);
        var missing = RequiredColumns.Where(name => !columns.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("Missing Whatnot show report columns: " + string.Join(", ", missing));

        var sales = new List<SaleEvent>();
        var orderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var unnumberedOrders = new List<UnnumberedShowOrder>();
        var ignored = 0;
        var rowNumber = 1;
        var showDate = "";
        while (!csv.EndOfData)
        {
            rowNumber++;
            string[] fields;
            try { fields = csv.ReadFields() ?? []; }
            catch (MalformedLineException ex) { throw new InvalidDataException($"CSV row {rowNumber} is malformed: {ex.Message}", ex); }
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace)) continue;
            string Value(string column) => columns[column] < fields.Length ? fields[columns[column]].Trim() : "";
            if (!string.IsNullOrWhiteSpace(Value("cancelled_or_failed"))) { ignored++; continue; }

            var orderId = Value("order_id");
            var buyer = Value("buyer_username");
            var title = Value("product_name");
            var placedAt = Value("placed_at");
            var numberMatch = ProductNumberPattern().Match(title);
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(buyer) ||
                !int.TryParse(Value("product_quantity"), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) ||
                quantity is < 1 or > 20)
            {
                errors.Add($"row {rowNumber}: order ID, buyer, or quantity (1–20) is invalid");
                continue;
            }
            if (!numberMatch.Success)
            {
                unnumberedOrders.Add(new UnnumberedShowOrder(rowNumber, buyer, title));
                continue;
            }
            if (!orderIds.Add(orderId)) { errors.Add($"row {rowNumber}: duplicate order_id {orderId}"); continue; }
            var number = numberMatch.Groups[1].Value;
            var isFreeGift = FreeGiftPattern().IsMatch(title);
            var productNumber = isFreeGift ? $"Free Gift #{number}" : number;
            if (showDate.Length == 0 && placedAt.Length >= 10 &&
                DateOnly.TryParseExact(placedAt[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                showDate = placedAt[..10];
            sales.Add(new SaleEvent
            {
                OrderId = orderId,
                ShipmentId = Value("shipment_id"),
                Quantity = quantity,
                ReportRowNumber = rowNumber,
                ReportPlacedAt = placedAt,
                BuyerName = buyer,
                ProductNumber = productNumber,
                ProductName = title,
                SourceText = title,
                DetectedAt = DateTimeOffset.Now
            });
        }
        if (errors.Count > 0)
            throw new InvalidDataException("Import stopped; no rows were saved. " + string.Join("; ", errors.Take(12)));
        if (sales.Count == 0) throw new InvalidDataException(
            $"No paid orders with a final #number were found. {unnumberedOrders.Count} paid order(s) need manual handling.");
        if (showDate.Length == 0) throw new InvalidDataException("No valid placed_at date was found.");
        return new WhatnotShowReportImport(hash, showDate, sales, ignored, unnumberedOrders);
    }
}
