namespace LocalProductManager.Api.Dtos;

public sealed record UpsertProductListingRequest(
    string Platform,
    string? ListingUrl,
    decimal? RetailPrice);

public sealed record ProductListingDto(
    int Id,
    int ProductId,
    string Platform,
    string? ListingUrl,
    decimal? RetailPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
