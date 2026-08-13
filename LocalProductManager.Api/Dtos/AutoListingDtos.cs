namespace LocalProductManager.Api.Dtos;

public sealed record AutoListingPlatformDto(
    string Platform,
    string Label,
    string Status,
    bool Enabled,
    string Message);

public sealed record EbayListingDraftDto(
    int ProductId,
    string Sku,
    string Title,
    string Description,
    decimal? Price,
    int Quantity,
    string Condition,
    string MarketplaceId,
    string? ImagePath,
    IReadOnlyList<string> MissingFields);
