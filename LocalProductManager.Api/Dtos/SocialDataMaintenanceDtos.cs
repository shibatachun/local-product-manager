namespace LocalProductManager.Api.Dtos;

public sealed record ResetSocialDataRequest(string AdminPassword);

public sealed record SocialDataMaintenanceResult(
    string Status,
    int Accounts,
    int AccountSnapshots,
    int ContentSnapshots,
    int DailyDeltas,
    DateTimeOffset CompletedAt);
