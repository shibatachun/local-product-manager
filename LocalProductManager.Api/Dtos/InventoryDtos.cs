namespace LocalProductManager.Api.Dtos;

public sealed record CreateWarehouseRequest(string Name);
public sealed record CreateShelfRequest(string ZoneCode, int RowNumber, int LevelCount, decimal X, decimal Y);
public sealed record SetLevelInventoryRequest(int ProductId, int Qty, int BinNumber = 1, string? InventoryChannel = null);
public sealed record AdjustLevelInventoryRequest(int ProductId, int Delta, int BinNumber = 1, string? InventoryChannel = null);
public sealed record MoveProductInventoryRequest(
    int? SourceLevelId, int? SourceBinNumber, int? TargetLevelId, int? TargetBinNumber, int Quantity,
    string? TargetInventoryChannel = null);
public sealed record UpdateShelfLayoutRequest(decimal X, decimal Y, int Rotation);
public sealed record WarehouseDto(int Id, string Code, string Name, IReadOnlyList<WarehouseShelfDto> Shelves);
public sealed record WarehouseShelfDto(
    int Id, string Code, string ZoneCode, int RowNumber, int RackNumber, int LevelCount,
    decimal X, decimal Y, decimal Width, decimal Height, int Rotation, int TotalQty);
public sealed record ShelfDetailDto(int Id, string Code, int LevelCount, IReadOnlyList<ShelfLevelDto> Levels);
public sealed record ShelfLevelDto(int Id, string Code, int LevelNumber, int TotalQty, IReadOnlyList<ShelfInventoryItemDto> Items);
public sealed record ShelfInventoryItemDto(int ProductId, string Sku, string ProductName, int Qty, int BinNumber, string InventoryChannel);
public sealed record ScannerLocationDto(
    int LevelId, int BinNumber, string Key, string Code, string Description, int Qty, string InventoryChannel);

public sealed record InventoryHistoryDto(
    long Id, int ProductId, string? LocalInventoryId, string Sku, string ProductName,
    int ShelfLevelId, int BinNumber, string LocationCode, string LocationDescription,
    string Operation, int BeforeQty, int AfterQty, int Delta,
    string UserName, string UserRole, string? RemoteIp, string? UserAgent,
    string Source, DateTimeOffset CreatedAt);
