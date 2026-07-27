namespace LocalProductManager.Api.Models;

public sealed class ShelfLevel
{
    public int Id { get; set; }
    public int WarehouseShelfId { get; set; }
    public int LevelNumber { get; set; }
    public string Code { get; set; } = "";
    public WarehouseShelf? WarehouseShelf { get; set; }
    public List<ShelfLevelInventory> Inventory { get; set; } = [];
}
