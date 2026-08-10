namespace LocalProductManager.Api.Dtos;

public sealed record CurrentUserDto(
    string SessionId,
    string UserName,
    string Role,
    bool IsHost,
    bool IsAdmin,
    bool CanManageStorage,
    bool CanEditAttribution,
    bool CanManageSocial,
    bool CanImportData,
    bool CanEditProductDetails);

public sealed record LoginRequest(string UserName, string Password);

public sealed record AppUserAdminDto(
    int Id,
    string UserName,
    string Role,
    bool IsEnabled,
    bool CanManageStorage,
    bool CanEditAttribution,
    bool CanManageSocial,
    bool CanImportData,
    bool CanEditProductDetails,
    DateTimeOffset UpdatedAt);

public sealed record UpdateUserPermissionsRequest(
    string Role,
    bool IsEnabled,
    bool CanManageStorage,
    bool CanEditAttribution,
    bool CanManageSocial,
    bool CanImportData,
    bool CanEditProductDetails);

public sealed record UserSessionAdminDto(
    string Id,
    int? AppUserId,
    string UserName,
    string Role,
    string? RemoteIp,
    bool IsHost,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);
