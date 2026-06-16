namespace LocalProductManager.Api.Models;

public sealed class AmazonAsin
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string Asin { get; set; }
    public required string AsinType { get; set; }
    public string? ParentAsin { get; set; }
    public bool AmazonAttributeDone { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Product? Product { get; set; }
    public List<AmazonAttributionLink> AttributionLinks { get; set; } = [];
}
