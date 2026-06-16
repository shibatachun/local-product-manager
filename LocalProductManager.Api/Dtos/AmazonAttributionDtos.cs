namespace LocalProductManager.Api.Dtos;

public sealed record UpsertAmazonAttributionRequest(
    string AttributionUrl,
    int? AmazonAsinId);

public sealed record AmazonAttributionLinkDto(
    int Id,
    int ProductId,
    int? AmazonAsinId,
    string Platform,
    string AttributionUrl,
    string? ImportedFileName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ImportAmazonAttributionResult(
    int ImportedCount,
    IReadOnlyList<AmazonAttributionLinkDto> Links);
