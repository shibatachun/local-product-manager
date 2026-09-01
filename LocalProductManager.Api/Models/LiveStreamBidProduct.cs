namespace LocalProductManager.Api.Models;

public sealed class LiveStreamBidProduct
{
    public int Id { get; set; }
    public int? SourceProductId { get; set; }
    public string? BatchNumber { get; set; }
    public int? BatchProductNumber { get; set; }
    public string? ActualSku { get; set; }
    public bool RequiresLocalLabel { get; set; }
    public decimal? WhatnotAuctionSafePrice { get; set; }
    public string? ItemCode { get; set; }
    public string Uuid { get; set; } = "";
    public string Title { get; set; } = "";
    public int Quantity { get; set; }
    public int Inventory { get; set; } = 1;
    public int ActualInventory { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? WeightGrams { get; set; }
    public decimal? WeightOunces { get; set; }
    public string? Material { get; set; }
    public decimal? TotalCost { get; set; }
    public string? ImportedFileName { get; set; }
    public bool IsSold { get; set; }
    public DateTimeOffset? SoldAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Product? SourceProduct { get; set; }
}
