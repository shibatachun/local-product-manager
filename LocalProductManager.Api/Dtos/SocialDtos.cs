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
