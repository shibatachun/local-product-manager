namespace LocalProductManager.Api.Models;

public sealed class ProductListing
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string Platform { get; set; }
    public string? ListingUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Product? Product { get; set; }
}
