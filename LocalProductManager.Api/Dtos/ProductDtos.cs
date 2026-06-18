namespace LocalProductManager.Api.Dtos;

public sealed record CreateProductRequest(
    string Sku,
    string ProductName,
    string? Category,
    decimal? Cost,
    decimal? Weight,
    decimal? Length,
    decimal? Width,
    decimal? Height,
    string? ImagePath,
    string? ImageSource);

public sealed record ProductDuplicateCheckDto(
    string Sku,
    string? SkuBase,
    bool ExactSkuExists,
    int? ExactProductId,
    string? ExactProductName,
    IReadOnlyList<ProductDuplicateCandidateDto> SameBaseProducts);

public sealed record ProductDuplicateCandidateDto(
    int Id,
    string Sku,
    string ProductName,
    string? SkuBase);

public sealed record UpdateProductRequest(
    string Sku,
    string ProductName,
    string? Category,
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
    string? Category,
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
    string? Category,
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

public sealed record ListingImageLookupRequest(string? ListingUrl, string? Platform);

public sealed record AmazonImageLookupResult(string ImageUrl);

public sealed record ListingImageLookupResult(string ImageUrl);
