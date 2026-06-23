namespace LocalProductManager.Api.Dtos;

public sealed record MetaAuthStatusDto(
    bool IsConfigured,
    bool IsConnected,
    string? UserId,
    string? UserName,
    int FacebookPages,
    int InstagramAccounts,
    string? Message,
    string RedirectUri,
    bool IsAdminHost);

public sealed record MetaAuthStartResult(string AuthorizationUrl, string RedirectUri);
