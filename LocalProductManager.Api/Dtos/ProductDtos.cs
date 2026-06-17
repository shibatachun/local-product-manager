namespace LocalProductManager.Api.Dtos;

public sealed record CreateProductRequest(
    string Sku,
    string ProductName,
    decimal? Cost,
    decimal? Weight,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? ImagePath,
    string? ImageSource);

public sealed record UpdateProductRequest(
    string Sku,
    string ProductName,
    decimal? Cost,
    decimal? Weight,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? ImagePath,
    string? ImageSource);

public sealed record ProductListItemDto(
    int Id,
    string Sku,
    string ProductName,
    decimal? Cost,
    decimal? Weight,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? ImagePath,
    string ImageSource,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<string> AmazonAsinValues,
    int AsinCount,
    string ProductType,
    string? SkuBase,
    string? AmazonParentAsin,
    int? ParentProductId,
    IReadOnlyList<ProductListItemDto> Children,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProductDetailDto(
    int Id,
    string Sku,
    string ProductName,
    decimal? Cost,
    decimal? Weight,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? ImagePath,
    string ImageSource,
    IReadOnlyList<ProductListingDto> Listings,
    IReadOnlyList<AmazonAsinDto> AmazonAsins,
    IReadOnlyList<AmazonAttributionLinkDto> AmazonAttributionLinks,
    string ProductType,
    string? SkuBase,
    string? AmazonParentAsin,
    int? ParentProductId,
    IReadOnlyList<ProductListItemDto> Children,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AmazonImageLookupRequest(string? ListingUrl);

public sealed record AmazonImageLookupResult(string ImageUrl);
