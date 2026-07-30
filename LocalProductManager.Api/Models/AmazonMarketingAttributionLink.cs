namespace LocalProductManager.Api.Models;

public sealed class AmazonMarketingAttributionLink
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? AmazonAsinId { get; set; }
    public required string Platform { get; set; }
    public required string AttributionUrl { get; set; }
    public string? ImportedFileName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public AmazonMarketingProduct? Product { get; set; }
    public AmazonMarketingAsin? AmazonAsin { get; set; }
}
