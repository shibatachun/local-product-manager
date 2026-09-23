namespace LocalProductManager.Api.Models;

public sealed class LiveStreamBidShowSession
{
    public int Id { get; set; }
    public DateOnly ShowDate { get; set; }
    public int ShowNumber { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public string StartedBy { get; set; } = "guest";
    public string? EndedBy { get; set; }
    public ICollection<LiveStreamBidSale> Sales { get; set; } = [];
}
