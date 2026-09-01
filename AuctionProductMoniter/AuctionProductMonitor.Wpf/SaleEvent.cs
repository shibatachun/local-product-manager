namespace AuctionProductMonitor.Wpf;

public sealed class SaleEvent
{
    public string SessionId { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string ProductNumber { get; set; } = "";
    public DateTimeOffset DetectedAt { get; set; }
    public string SourceText { get; set; } = "";
    public string PageUrl { get; set; } = "";

    public string LocalTime => DetectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
