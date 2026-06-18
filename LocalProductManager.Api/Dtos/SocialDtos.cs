namespace LocalProductManager.Api.Dtos;

public sealed record SocialAccountDto(
    int Id,
    string Platform,
    string AccountName,
    string? AccountHandle,
    string? AccountUrl,
    string? Color,
    bool IsActive,
    IReadOnlyList<SocialMetricSnapshotDto> Metrics,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SocialMetricSnapshotDto(
    int Id,
    int SocialAccountId,
    DateOnly SnapshotDate,
    int Clicks,
    int Likes,
    int Views,
    int Comments,
    int Shares,
    int Followers,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpsertSocialAccountRequest(
    string Platform,
    string AccountName,
    string? AccountHandle,
    string? AccountUrl,
    string? Color,
    bool IsActive);

public sealed record UpsertSocialMetricRequest(
    DateOnly SnapshotDate,
    int Clicks,
    int Likes,
    int Views,
    int Comments,
    int Shares,
    int Followers);

public sealed record SocialSyncResult(
    int AccountsChecked,
    int MetricsImported,
    string Status);

public sealed record SocialDebugSnapshotDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SocialDebugAccountDto> Accounts,
    IReadOnlyList<SocialDebugMetricDto> MetricSnapshots,
    IReadOnlyList<SocialDebugVideoMetricDto> VideoMetricSnapshots,
    IReadOnlyList<SocialDebugPlatformTotalDto> PlatformTotals,
    IReadOnlyList<SocialDebugVideoTotalDto> LatestVideoTotals);

public sealed record SocialDebugAccountDto(
    int Id,
    string Platform,
    string AccountName,
    string? AccountHandle,
    bool IsActive);

public sealed record SocialDebugMetricDto(
    int Id,
    int SocialAccountId,
    string Platform,
    string AccountName,
    DateOnly SnapshotDate,
    int Clicks,
    int Likes,
    int Views,
    int Comments,
    int Shares,
    int Followers,
    DateTimeOffset UpdatedAt);

public sealed record SocialDebugVideoMetricDto(
    int Id,
    int SocialAccountId,
    string Platform,
    string AccountName,
    string PlatformVideoId,
    string Title,
    DateOnly SnapshotDate,
    int Views,
    int Likes,
    int Comments,
    int Clicks,
    DateTimeOffset UpdatedAt);

public sealed record SocialDebugPlatformTotalDto(
    string Platform,
    int Accounts,
    int MetricRows,
    int LatestViews,
    int LatestLikes,
    int LatestComments,
    int LatestShares,
    int LatestFollowers,
    int SumViews,
    int SumLikes,
    int SumComments,
    int SumClicks);

public sealed record SocialDebugVideoTotalDto(
    string Platform,
    int SocialAccountId,
    string AccountName,
    int Videos,
    int LatestViews,
    int LatestLikes,
    int LatestComments,
    DateOnly? LatestSnapshotDate);
