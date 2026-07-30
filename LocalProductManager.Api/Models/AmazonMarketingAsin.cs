namespace LocalProductManager.Api.Models;

public sealed class AmazonMarketingAsin
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string Asin { get; set; }
    public required string AsinType { get; set; }
    public string? ParentAsin { get; set; }
    public bool AmazonAttributeDone { get; set; }
    public AmazonMarketingProduct? Product { get; set; }
}
