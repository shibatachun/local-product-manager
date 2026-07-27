namespace LocalProductManager.Api.Models;

public sealed class Warehouse
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<WarehouseShelf> Shelves { get; set; } = [];
}
