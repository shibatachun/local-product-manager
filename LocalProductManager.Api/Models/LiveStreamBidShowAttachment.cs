namespace LocalProductManager.Api.Models;

public sealed class LiveStreamBidShowAttachment
{
    public int Id { get; set; }
    public int ShowSessionId { get; set; }
    public LiveStreamBidShowSession ShowSession { get; set; } = null!;
    public int? ShowOrderId { get; set; }
    public LiveStreamBidShowOrder? ShowOrder { get; set; }
    public string? CustomerName { get; set; }
    public string Kind { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UploadedBy { get; set; } = "guest";
}
