namespace LocalProductManager.Api.Models;

public sealed class AmazonAttributionLink
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? AmazonAsinId { get; set; }
    public required string Platform { get; set; }
    public required string AttributionUrl { get; set; }
    public string? ImportedFileName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Product? Product { get; set; }
    public AmazonAsin? AmazonAsin { get; set; }
}
