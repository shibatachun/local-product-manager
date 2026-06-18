namespace LocalProductManager.Api.Models;

public sealed class AppUser
{
    public int Id { get; set; }
    public required string UserName { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool CanManageStorage { get; set; }
    public bool CanEditAttribution { get; set; }
    public bool CanManageSocial { get; set; }
    public bool CanImportData { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
