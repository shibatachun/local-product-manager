namespace LocalProductManager.Api.Models;

public sealed class WarehouseShelf
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public required string Code { get; set; }
    public required string ZoneCode { get; set; }
    public int AisleNumber { get; set; }
    public int RackNumber { get; set; }
    public int LevelCount { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Width { get; set; } = 18;
    public decimal Height { get; set; } = 12;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Warehouse? Warehouse { get; set; }
    public List<ShelfLevel> Levels { get; set; } = [];
}
