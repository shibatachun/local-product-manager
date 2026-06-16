namespace LocalProductManager.Api.Dtos;

public sealed record CreateAmazonAsinRequest(
    string Asin,
    string AsinType,
    string? ParentAsin,
    bool AmazonAttributeDone);

public sealed record UpdateAmazonAsinRequest(
    string Asin,
    string AsinType,
    string? ParentAsin,
    bool AmazonAttributeDone);

public sealed record AmazonAsinDto(
    int Id,
    int ProductId,
    string Asin,
    string AsinType,
    string? ParentAsin,
    bool AmazonAttributeDone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
