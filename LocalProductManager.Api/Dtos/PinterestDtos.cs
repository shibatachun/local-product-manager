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
