namespace AuctionProductMonitor.Wpf;

public sealed class SaleEvent
{
    public string OrderId { get; set; } = "";
    public string ShipmentId { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int ReportRowNumber { get; set; }
    public string ReportPlacedAt { get; set; } = "";
    public DateTimeOffset? PrintedAt { get; set; }
    public string SessionId { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string ProductNumber { get; set; } = "";
    public string ProductName { get; set; } = "";
    public DateTimeOffset DetectedAt { get; set; }
    public string SourceText { get; set; } = "";
    public string PageUrl { get; set; } = "";

    public string LocalTime => !string.IsNullOrWhiteSpace(ReportPlacedAt)
        ? ReportPlacedAt
        : DetectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string PrintProductName => !string.IsNullOrWhiteSpace(ProductName)
        ? ProductName.Trim()
        : ReportRowNumber > 0 && !string.IsNullOrWhiteSpace(SourceText)
            ? SourceText.Trim()
            : ProductNumber.Trim();
    public string PrintState => PrintedAt.HasValue ? LanguageManager.Get("Printed") : LanguageManager.Get("NotPrinted");
}
