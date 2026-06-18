using System.Security.Cryptography;
using System.Text;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class AuthService(AppDbContext db)
{
    public const string SessionCookieName = "lpm_session";

    public async Task SeedAdminAsync(CancellationToken cancellationToken = default)
    {
        var admin = await db.AppUsers.FirstOrDefaultAsync(
            user => user.UserName == "admin123",
            cancellationToken);
        if (admin is null)
        {
            db.AppUsers.Add(new AppUser
            {
                UserName = "admin123",
                PasswordHash = HashPassword("123"),
                Role = "admin",
                IsEnabled = true,
                CanManageStorage = true,
                CanEditAttribution = true,
                CanManageSocial = true,
                CanImportData = true
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<CurrentUserDto> GetOrCreateCurrentUserAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var sessionId = context.Request.Cookies[SessionCookieName];
        AppUserSession? session = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            session = await db.AppUserSessions
                .Include(item => item.AppUser)
                .FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        }

        if (session is null)
        {
            session = new AppUserSession
            {
                Id = NewSessionId(),
                UserName = "guest",
                Role = "guest",
                RemoteIp = RemoteIp(context),
                IsHost = IsHost(context)
            };
            db.AppUserSessions.Add(session);
            SetSessionCookie(context, session.Id);
        }

        session.LastSeenAt = DateTimeOffset.UtcNow;
        session.RemoteIp = RemoteIp(context);
        session.IsHost = IsHost(context);
        await db.SaveChangesAsync(cancellationToken);
        return ToCurrentUser(session);
    }

    public async Task<CurrentUserDto?> LoginAsync(HttpContext context, string userName, string password, CancellationToken cancellationToken)
    {
        if (!IsHost(context))
        {
            return null;
        }

        var normalizedUserName = userName.Trim();
        var passwordHash = HashPassword(password);
        var user = await db.AppUsers.FirstOrDefaultAsync(
            item => item.UserName == normalizedUserName && item.PasswordHash == passwordHash && item.IsEnabled,
            cancellationToken);
        if (user is null)
        {
            return null;
        }

        var session = await GetSessionEntityAsync(context, cancellationToken);
        session.AppUserId = user.Id;
        session.AppUser = user;
        session.UserName = user.UserName;
        session.Role = user.Role;
        session.RemoteIp = RemoteIp(context);
        session.IsHost = true;
        session.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        SetSessionCookie(context, session.Id);
        return ToCurrentUser(session);
    }

    public async Task LogoutAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var session = await GetSessionEntityAsync(context, cancellationToken);
        session.AppUserId = null;
        session.AppUser = null;
        session.UserName = "guest";
        session.Role = "guest";
        session.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsAdminAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        (await GetOrCreateCurrentUserAsync(context, cancellationToken)).IsAdmin;

    public async Task<bool> CanManageStorageAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        (await GetOrCreateCurrentUserAsync(context, cancellationToken)).CanManageStorage;

    public async Task<bool> CanEditAttributionAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        (await GetOrCreateCurrentUserAsync(context, cancellationToken)).CanEditAttribution;

    public async Task<bool> CanManageSocialAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        (await GetOrCreateCurrentUserAsync(context, cancellationToken)).CanManageSocial;

    public async Task<bool> CanImportDataAsync(HttpContext context, CancellationToken cancellationToken = default) =>
        (await GetOrCreateCurrentUserAsync(context, cancellationToken)).CanImportData;

    public static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<AppUserSession> GetSessionEntityAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var sessionId = context.Request.Cookies[SessionCookieName];
        var session = !string.IsNullOrWhiteSpace(sessionId)
            ? await db.AppUserSessions.Include(item => item.AppUser).FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken)
            : null;
        if (session is not null) return session;

        session = new AppUserSession
        {
            Id = NewSessionId(),
            UserName = "guest",
            Role = "guest",
            RemoteIp = RemoteIp(context),
            IsHost = IsHost(context)
        };
        db.AppUserSessions.Add(session);
        SetSessionCookie(context, session.Id);
        return session;
    }

    private static CurrentUserDto ToCurrentUser(AppUserSession session)
    {
        var user = session.AppUser;
        var isEnabledUser = user?.IsEnabled == true;
        var effectiveRole = isEnabledUser ? user!.Role : session.Role;
        var isAdmin = isEnabledUser && effectiveRole == "admin";
        return new CurrentUserDto(
            session.Id,
            session.UserName,
            effectiveRole,
            session.IsHost,
            isAdmin,
            isAdmin || (isEnabledUser && user?.CanManageStorage == true),
            isAdmin || (isEnabledUser && user?.CanEditAttribution == true),
            isAdmin || (isEnabledUser && user?.CanManageSocial == true),
            isAdmin || (isEnabledUser && user?.CanImportData == true));
    }

    private static void SetSessionCookie(HttpContext context, string sessionId) =>
        context.Response.Cookies.Append(SessionCookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Expires = DateTimeOffset.UtcNow.AddDays(14)
        });

    public static bool IsHost(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        return remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp);
    }

    private static string? RemoteIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString();

    private static string NewSessionId() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
