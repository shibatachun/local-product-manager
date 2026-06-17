namespace LocalProductManager.Api.Models;

public sealed class Product
{
    public int Id { get; set; }
    public required string Sku { get; set; }
    public required string ProductName { get; set; }
    public decimal? Cost { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public string? ImagePath { get; set; }
    public string ImageSource { get; set; } = "local";
    public int? ParentProductId { get; set; }
    public string ProductType { get; set; } = "standalone";
    public string? SkuBase { get; set; }
    public string? AmazonParentAsin { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ProductListing> Listings { get; set; } = [];
    public List<AmazonAsin> AmazonAsins { get; set; } = [];
    public List<AmazonAttributionLink> AmazonAttributionLinks { get; set; } = [];
    public Product? ParentProduct { get; set; }
    public List<Product> ChildProducts { get; set; } = [];
}
