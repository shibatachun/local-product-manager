namespace LocalProductManager.Api.Models;

public sealed class ImportedBidBatch
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}
