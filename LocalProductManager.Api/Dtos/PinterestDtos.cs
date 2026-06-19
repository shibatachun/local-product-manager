namespace LocalProductManager.Api.Dtos;

public sealed record PinterestAuthStatusDto(
    bool IsConfigured,
    bool IsConnected,
    string? AccountId,
    string? AccountName,
    string? AccountThumbnailUrl,
    string? Message,
    string RedirectUri,
    bool IsAdminHost);

public sealed record PinterestAuthStartResult(string AuthorizationUrl, string RedirectUri);

public sealed record PinterestReportImportResult(
    int AccountId,
    int DailyRowsImported,
    int BoardsImported,
    int PinsImported,
    string AccountName,
    DateOnly SnapshotDate);

public sealed record PinterestContentSnapshotDto(
    PinterestContentChannelDto Channel,
    IReadOnlyList<PinterestContentItemDto> Pins);

public sealed record PinterestContentChannelDto(
    string Id,
    string Title,
    string? ThumbnailUrl,
    long Followers,
    long Views,
    long PinCount,
    long Clicks,
    string ClicksNote);

public sealed record PinterestContentItemDto(
    string Id,
    string Title,
    string Url,
    string Kind,
    DateTimeOffset? PublishedAt,
    long Views,
    long Likes,
    long Clicks,
    long Comments,
    IReadOnlyList<PinterestMetricPointDto> Metrics);

public sealed record PinterestMetricPointDto(
    DateOnly SnapshotDate,
    long Views,
    long Likes,
    long Comments,
    long Clicks);

public sealed record InstagramReportImportResult(
    int AccountId,
    int PostsImported,
    int ViewsImported,
    int LikesImported,
    int CommentsImported,
    int SharesImported,
    int FollowsImported,
    string AccountName,
    DateOnly SnapshotDate);

public sealed record InstagramContentSnapshotDto(
    InstagramContentChannelDto Channel,
    IReadOnlyList<InstagramContentItemDto> Posts);

public sealed record InstagramContentChannelDto(
    string Id,
    string Title,
    string? ThumbnailUrl,
    long Followers,
    long Views,
    long PostCount,
    long Shares,
    string ClicksNote);

public sealed record InstagramContentItemDto(
    string Id,
    string Title,
    string Url,
    string Kind,
    DateTimeOffset? PublishedAt,
    long Views,
    long Likes,
    long Saves,
    long Comments,
    IReadOnlyList<PinterestMetricPointDto> Metrics);
