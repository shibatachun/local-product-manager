namespace LocalProductManager.Api.Models;

public sealed class SocialDailyMetricDelta
{
    public int Id { get; set; }
    public int SocialAccountId { get; set; }
    public string Scope { get; set; } = "account";
    public string MetricKey { get; set; } = "__account__";
    public string Title { get; set; } = "";
    public DateOnly SnapshotDate { get; set; }
    public int Clicks { get; set; }
    public int Likes { get; set; }
    public int Views { get; set; }
    public int Comments { get; set; }
    public int Shares { get; set; }
    public int Followers { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SocialAccount? SocialAccount { get; set; }
}
