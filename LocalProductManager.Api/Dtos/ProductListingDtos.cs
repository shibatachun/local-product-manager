namespace LocalProductManager.Api.Dtos;

public sealed record UpsertProductListingRequest(
    string Platform,
    string? ListingUrl);

public sealed record ProductListingDto(
    int Id,
    int ProductId,
    string Platform,
    string? ListingUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
