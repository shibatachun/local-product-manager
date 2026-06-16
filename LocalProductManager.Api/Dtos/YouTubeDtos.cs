namespace LocalProductManager.Api.Dtos;

public sealed record YouTubeAuthStatusDto(
    bool IsConfigured,
    bool IsConnected,
    string? ChannelTitle,
    string? Message,
    string RedirectUri);

public sealed record YouTubeAuthStartResult(string AuthorizationUrl, string RedirectUri);
