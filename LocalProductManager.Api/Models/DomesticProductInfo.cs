namespace LocalProductManager.Api.Models;

public sealed class DomesticProductInfo
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public string? Material { get; set; }
    public string? Specification { get; set; }
    public decimal? CostRmb { get; set; }
    public decimal? MinimumSalePriceUsd { get; set; }
    public int? WarehouseInventory { get; set; }
    public string? SupplierName { get; set; }
    public string? ChineseDeclarationName { get; set; }
    public string? EnglishDeclarationName { get; set; }
    public decimal? WeightGrams { get; set; }
    public string? ImagePath { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public string? SourceFileName { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
