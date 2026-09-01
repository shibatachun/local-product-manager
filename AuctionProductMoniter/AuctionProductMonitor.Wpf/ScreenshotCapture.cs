namespace AuctionProductMonitor.Wpf;

public sealed class ScreenshotCapture
{
    public string BuyerName { get; set; } = "";
    public string ProductNumber { get; set; } = "";
    public string ImageDataUrl { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public string PageUrl { get; set; } = "";
}
