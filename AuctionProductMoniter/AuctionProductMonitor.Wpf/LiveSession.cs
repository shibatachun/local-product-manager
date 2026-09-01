using System.Text.Json.Serialization;

namespace AuctionProductMonitor.Wpf;

public sealed class LiveSession
{
    public string Id { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string ScreenshotFolder { get; set; } = "";
    public List<SaleEvent> Sales { get; set; } = [];

    [JsonIgnore] public string LiveDate => StartedAt.ToLocalTime().ToString("yyyy-MM-dd");
    [JsonIgnore] public string StartTime => StartedAt.ToLocalTime().ToString("HH:mm:ss");
    [JsonIgnore] public string EndTime => EndedAt?.ToLocalTime().ToString("HH:mm:ss") ?? LanguageManager.Get("LiveRunning");
    [JsonIgnore] public int SaleCount => Sales.Count;
}
