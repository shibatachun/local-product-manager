namespace LocalProductManager.Api.Models;

public sealed class AppUserSession
{
    public string Id { get; set; } = "";
    public int? AppUserId { get; set; }
    public string UserName { get; set; } = "guest";
    public string Role { get; set; } = "guest";
    public string? RemoteIp { get; set; }
    public bool IsHost { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser? AppUser { get; set; }
}
