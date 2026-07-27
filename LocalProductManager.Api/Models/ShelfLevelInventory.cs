namespace LocalProductManager.Api.Models;

public sealed class ShelfLevelInventory
{
    public int Id { get; set; }
    public int ShelfLevelId { get; set; }
    public int ProductId { get; set; }
    public int Qty { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ShelfLevel? ShelfLevel { get; set; }
    public Product? Product { get; set; }
}
