namespace LocalProductManager.Api.Models;

public sealed class InventoryHistoryEntry
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public string? LocalInventoryId { get; set; }
    public required string Sku { get; set; }
    public required string ProductName { get; set; }
    public int ShelfLevelId { get; set; }
    public int BinNumber { get; set; }
    public required string LocationCode { get; set; }
    public required string LocationDescription { get; set; }
    public required string Operation { get; set; }
    public int BeforeQty { get; set; }
    public int AfterQty { get; set; }
    public int Delta { get; set; }
    public required string UserName { get; set; }
    public required string UserRole { get; set; }
    public string? RemoteIp { get; set; }
    public string? UserAgent { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
