namespace LocalProductManager.Api.Models;

public sealed class SocialVideoMetricSnapshot
{
    public int Id { get; set; }
    public int SocialAccountId { get; set; }
    public string PlatformVideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int Clicks { get; set; }
    public int Views { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SocialAccount? SocialAccount { get; set; }
}
