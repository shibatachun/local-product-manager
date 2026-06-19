namespace LocalProductManager.Api.Models;

public sealed class VideoTrendItem
{
    public int Id { get; set; }
    public string Platform { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? PlatformVideoId { get; set; }
    public string? EmbedUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Tags { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
