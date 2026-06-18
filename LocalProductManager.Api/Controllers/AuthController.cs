using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    AuthService auth) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken) =>
        Ok(await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken));

    [HttpPost("login")]
    public async Task<ActionResult<CurrentUserDto>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = await auth.LoginAsync(HttpContext, request.UserName, request.Password, cancellationToken);
        return currentUser is null
            ? StatusCode(StatusCodes.Status403Forbidden, "Admin login is only available on the host computer with valid credentials.")
            : Ok(currentUser);
    }

    [HttpPost("logout")]
    public async Task<ActionResult<CurrentUserDto>> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(HttpContext, cancellationToken);
        return Ok(await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken));
    }

    [HttpGet("users")]
    public async Task<ActionResult<object>> Users(CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken)) return AdminOnly();

        var users = await db.AppUsers
            .AsNoTracking()
            .OrderBy(user => user.UserName)
            .Select(user => new AppUserAdminDto(
                user.Id,
                user.UserName,
                user.Role,
                user.IsEnabled,
                user.CanManageStorage,
                user.CanEditAttribution,
                user.CanManageSocial,
                user.CanImportData,
                user.UpdatedAt))
            .ToListAsync(cancellationToken);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var sessions = await db.AppUserSessions
            .AsNoTracking()
            .Where(session => session.LastSeenAt >= cutoff)
            .OrderByDescending(session => session.LastSeenAt)
            .Select(session => new UserSessionAdminDto(
                session.Id,
                session.AppUserId,
                session.UserName,
                session.Role,
                session.RemoteIp,
                session.IsHost,
                session.CreatedAt,
                session.LastSeenAt))
            .ToListAsync(cancellationToken);

        return Ok(new { users, sessions });
    }

    [HttpPut("users/{userId:int}")]
    public async Task<ActionResult<AppUserAdminDto>> UpdateUser(
        int userId,
        UpdateUserPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken)) return AdminOnly();

        var user = await db.AppUsers.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null) return NotFound();

        user.Role = NormalizeRole(request.Role);
        user.IsEnabled = request.IsEnabled;
        user.CanManageStorage = request.CanManageStorage;
        user.CanEditAttribution = request.CanEditAttribution;
        user.CanManageSocial = request.CanManageSocial;
        user.CanImportData = request.CanImportData;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new AppUserAdminDto(
            user.Id,
            user.UserName,
            user.Role,
            user.IsEnabled,
            user.CanManageStorage,
            user.CanEditAttribution,
            user.CanManageSocial,
            user.CanImportData,
            user.UpdatedAt));
    }

    private static string NormalizeRole(string role) =>
        string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "user";

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "This action requires an admin session on the host computer.");
}
