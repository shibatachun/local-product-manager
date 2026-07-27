namespace LocalProductManager.Api.Dtos;

public sealed record CreateWarehouseRequest(string Name);
public sealed record CreateShelfRequest(string ZoneCode, int AisleNumber, int LevelCount, decimal X, decimal Y);
public sealed record SetLevelInventoryRequest(int ProductId, int Qty);
public sealed record UpdateShelfLayoutRequest(decimal X, decimal Y, int Rotation);
public sealed record WarehouseDto(int Id, string Code, string Name, IReadOnlyList<WarehouseShelfDto> Shelves);
public sealed record WarehouseShelfDto(
    int Id, string Code, string ZoneCode, int AisleNumber, int RackNumber, int LevelCount,
    decimal X, decimal Y, decimal Width, decimal Height, int Rotation, int TotalQty);
public sealed record ShelfDetailDto(int Id, string Code, int LevelCount, IReadOnlyList<ShelfLevelDto> Levels);
public sealed record ShelfLevelDto(int Id, string Code, int LevelNumber, int TotalQty, IReadOnlyList<ShelfInventoryItemDto> Items);
public sealed record ShelfInventoryItemDto(int ProductId, string Sku, string ProductName, int Qty);
