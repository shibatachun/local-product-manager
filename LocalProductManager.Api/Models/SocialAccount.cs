namespace LocalProductManager.Api.Models;

public sealed class SocialAccount
{
    public int Id { get; set; }
    public required string Platform { get; set; }
    public required string AccountName { get; set; }
    public string? AccountHandle { get; set; }
    public string? AccountUrl { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<SocialMetricSnapshot> Metrics { get; set; } = [];
}
