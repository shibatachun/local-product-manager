namespace LocalProductManager.Api.Dtos;

public sealed record DomesticProductDto(
    int Id, string Sku, string? ProductCode, string? ProductName, string? Material,
    string? Specification, decimal? CostRmb, decimal? MinimumSalePriceUsd,
    int? WarehouseInventory, string? SupplierName, string? ChineseDeclarationName,
    string? EnglishDeclarationName, decimal? WeightGrams, string? ImagePath,
    IReadOnlyDictionary<string, string> Details, string? SourceFileName,
    DateTimeOffset ImportedAt, DateTimeOffset UpdatedAt);

public sealed record DomesticProductSearchResult(
    IReadOnlyList<DomesticProductDto> Items, int Total, int Page, int PageSize,
    string SkuSortDirection);

public sealed record DomesticProductImportResult(
    int Processed, int Created, int Updated, int Skipped, string SourceFileName);
