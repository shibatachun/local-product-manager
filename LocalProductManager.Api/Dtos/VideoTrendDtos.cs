namespace LocalProductManager.Api.Dtos;

public sealed record VideoTrendItemDto(
    int Id,
    string Platform,
    string Url,
    string Title,
    string? Description,
    string? Author,
    string? PlatformVideoId,
    string? EmbedUrl,
    string? ThumbnailUrl,
    string? Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateVideoTrendItemRequest(
    string? Platform,
    string Url,
    string? Title,
    string? Description,
    string? Author,
    string? Tags);
