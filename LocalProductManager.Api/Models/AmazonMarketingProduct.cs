namespace LocalProductManager.Api.Models;

public sealed class AmazonMarketingProduct
{
    public int Id { get; set; }
    public required string Sku { get; set; }
    public required string ProductName { get; set; }
    public string? ImageUrl { get; set; }
    public string ProductType { get; set; } = "standalone";
    public string? SkuBase { get; set; }
    public string? AmazonParentAsin { get; set; }
    public int? ParentProductId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AmazonMarketingAsin> Asins { get; set; } = [];
    public List<AmazonMarketingAttributionLink> AttributionLinks { get; set; } = [];
    public AmazonMarketingProduct? ParentProduct { get; set; }
    public List<AmazonMarketingProduct> ChildProducts { get; set; } = [];
}
