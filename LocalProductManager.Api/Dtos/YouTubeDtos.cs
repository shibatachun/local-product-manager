namespace LocalProductManager.Api.Dtos;

public sealed record YouTubeAuthStatusDto(
    bool IsConfigured,
    bool IsConnected,
    string? ChannelId,
    string? ChannelTitle,
    string? ChannelThumbnailUrl,
    string? Message,
    string RedirectUri,
    bool IsAdminHost,
    bool IsTokenExpired,
    bool CanRefresh,
    bool NeedsReauthorization);

public sealed record YouTubeAuthStartResult(string AuthorizationUrl, string RedirectUri);

public sealed record YouTubeDebugSnapshotDto(
    YouTubeDebugChannelDto Channel,
    IReadOnlyList<YouTubeDebugVideoDto> Videos);

public sealed record YouTubeDebugChannelDto(
    string Id,
    string Title,
    string? ThumbnailUrl,
    long Followers,
    long Views,
    long VideoCount,
    long? Clicks,
    string ClicksNote);

public sealed record YouTubeDebugVideoDto(
    string Id,
    string Title,
    string? ThumbnailUrl,
    DateTimeOffset? PublishedAt,
    long Views,
    long Likes,
    long Comments,
    IReadOnlyList<YouTubeVideoMetricPointDto> Metrics);

public sealed record YouTubeVideoMetricPointDto(
    DateOnly SnapshotDate,
    long Views,
    long Likes,
    long Comments,
    long Clicks);
