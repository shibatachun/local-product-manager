using System.Text.Json;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class SocialDataMaintenanceService(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken)
    {
        var accounts = await db.SocialAccounts
            .AsNoTracking()
            .OrderBy(account => account.Platform)
            .ThenBy(account => account.AccountName)
            .ToListAsync(cancellationToken);
        var metrics = await db.SocialMetricSnapshots
            .AsNoTracking()
            .OrderBy(metric => metric.SocialAccountId)
            .ThenBy(metric => metric.SnapshotDate)
            .ToListAsync(cancellationToken);
        var content = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .OrderBy(metric => metric.SocialAccountId)
            .ThenBy(metric => metric.PlatformVideoId)
            .ThenBy(metric => metric.SnapshotDate)
            .ToListAsync(cancellationToken);
        var deltas = await db.SocialDailyMetricDeltas
            .AsNoTracking()
            .OrderBy(delta => delta.SocialAccountId)
            .ThenBy(delta => delta.Scope)
            .ThenBy(delta => delta.MetricKey)
            .ThenBy(delta => delta.SnapshotDate)
            .ToListAsync(cancellationToken);

        var export = new SocialDataExport(
            1,
            DateTimeOffset.Now,
            accounts.Select(ToExport).ToArray(),
            metrics.Select(ToExport).ToArray(),
            content.Select(ToExport).ToArray(),
            deltas.Select(ToExport).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);
    }

    public async Task<SocialDataMaintenanceResult> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new InvalidOperationException("Social data export file is empty.");
        }

        await using var stream = file.OpenReadStream();
        var export = await JsonSerializer.DeserializeAsync<SocialDataExport>(
            stream,
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("Social data export file was invalid.");

        await ClearSocialSnapshotsAsync(cancellationToken);
        var accountMap = new Dictionary<int, int>();
        foreach (var item in export.Accounts)
        {
            var account = await UpsertAccountAsync(item, cancellationToken);
            accountMap[item.Id] = account.Id;
        }

        foreach (var item in export.AccountSnapshots)
        {
            if (!accountMap.TryGetValue(item.SocialAccountId, out var accountId)) continue;
            db.SocialMetricSnapshots.Add(new SocialMetricSnapshot
            {
                SocialAccountId = accountId,
                SnapshotDate = item.SnapshotDate,
                Clicks = item.Clicks,
                Likes = item.Likes,
                Views = item.Views,
                Comments = item.Comments,
                Shares = item.Shares,
                Followers = item.Followers,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            });
        }

        foreach (var item in export.ContentSnapshots)
        {
            if (!accountMap.TryGetValue(item.SocialAccountId, out var accountId)) continue;
            db.SocialVideoMetricSnapshots.Add(new SocialVideoMetricSnapshot
            {
                SocialAccountId = accountId,
                PlatformVideoId = item.PlatformVideoId,
                Title = item.Title,
                ThumbnailUrl = item.ThumbnailUrl,
                PublishedAt = item.PublishedAt,
                SnapshotDate = item.SnapshotDate,
                Clicks = item.Clicks,
                Likes = item.Likes,
                Views = item.Views,
                Comments = item.Comments,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            });
        }

        foreach (var item in export.DailyDeltas)
        {
            if (!accountMap.TryGetValue(item.SocialAccountId, out var accountId)) continue;
            db.SocialDailyMetricDeltas.Add(new SocialDailyMetricDelta
            {
                SocialAccountId = accountId,
                Scope = item.Scope,
                MetricKey = item.MetricKey,
                Title = item.Title,
                SnapshotDate = item.SnapshotDate,
                Clicks = item.Clicks,
                Likes = item.Likes,
                Views = item.Views,
                Comments = item.Comments,
                Shares = item.Shares,
                Followers = item.Followers,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return await SnapshotResultAsync("Social data import completed.", cancellationToken);
    }

    public async Task<SocialDataMaintenanceResult> ResetAsync(
        string adminPassword,
        CancellationToken cancellationToken)
    {
        await VerifyAdminPasswordAsync(adminPassword, cancellationToken);
        await ClearSocialSnapshotsAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await SnapshotResultAsync("Social snapshots, content snapshots, and daily deltas were reset.", cancellationToken);
    }

    private async Task<SocialAccount> UpsertAccountAsync(
        SocialAccountExport item,
        CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts.FirstOrDefaultAsync(
            existing => existing.Platform == item.Platform &&
                ((item.AccountHandle != null && existing.AccountHandle == item.AccountHandle) ||
                 existing.AccountName == item.AccountName),
            cancellationToken);
        if (account is null)
        {
            account = new SocialAccount
            {
                Platform = item.Platform,
                AccountName = item.AccountName,
                AccountHandle = item.AccountHandle,
                AccountUrl = item.AccountUrl,
                Color = item.Color,
                IsActive = item.IsActive,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            };
            db.SocialAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            account.AccountName = item.AccountName;
            account.AccountHandle = item.AccountHandle ?? account.AccountHandle;
            account.AccountUrl = item.AccountUrl ?? account.AccountUrl;
            account.Color = item.Color ?? account.Color;
            account.IsActive = item.IsActive;
            account.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return account;
    }

    private async Task VerifyAdminPasswordAsync(
        string adminPassword,
        CancellationToken cancellationToken)
    {
        var hash = AuthService.HashPassword(adminPassword);
        var valid = await db.AppUsers.AnyAsync(
            user => user.UserName == "admin123" &&
                user.PasswordHash == hash &&
                user.Role == "admin" &&
                user.IsEnabled,
            cancellationToken);
        if (!valid)
        {
            throw new UnauthorizedAccessException("Admin password confirmation failed.");
        }
    }

    private async Task ClearSocialSnapshotsAsync(CancellationToken cancellationToken)
    {
        await db.SocialDailyMetricDeltas.ExecuteDeleteAsync(cancellationToken);
        await db.SocialVideoMetricSnapshots.ExecuteDeleteAsync(cancellationToken);
        await db.SocialMetricSnapshots.ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<SocialDataMaintenanceResult> SnapshotResultAsync(
        string status,
        CancellationToken cancellationToken) =>
        new(
            status,
            await db.SocialAccounts.CountAsync(cancellationToken),
            await db.SocialMetricSnapshots.CountAsync(cancellationToken),
            await db.SocialVideoMetricSnapshots.CountAsync(cancellationToken),
            await db.SocialDailyMetricDeltas.CountAsync(cancellationToken),
            DateTimeOffset.Now);

    private static SocialAccountExport ToExport(SocialAccount account) => new(
        account.Id,
        account.Platform,
        account.AccountName,
        account.AccountHandle,
        account.AccountUrl,
        account.Color,
        account.IsActive,
        account.CreatedAt,
        account.UpdatedAt);

    private static SocialMetricSnapshotExport ToExport(SocialMetricSnapshot metric) => new(
        metric.SocialAccountId,
        metric.SnapshotDate,
        metric.Clicks,
        metric.Likes,
        metric.Views,
        metric.Comments,
        metric.Shares,
        metric.Followers,
        metric.CreatedAt,
        metric.UpdatedAt);

    private static SocialVideoMetricSnapshotExport ToExport(SocialVideoMetricSnapshot metric) => new(
        metric.SocialAccountId,
        metric.PlatformVideoId,
        metric.Title,
        metric.ThumbnailUrl,
        metric.PublishedAt,
        metric.SnapshotDate,
        metric.Clicks,
        metric.Likes,
        metric.Views,
        metric.Comments,
        metric.CreatedAt,
        metric.UpdatedAt);

    private static SocialDailyMetricDeltaExport ToExport(SocialDailyMetricDelta delta) => new(
        delta.SocialAccountId,
        delta.Scope,
        delta.MetricKey,
        delta.Title,
        delta.SnapshotDate,
        delta.Clicks,
        delta.Likes,
        delta.Views,
        delta.Comments,
        delta.Shares,
        delta.Followers,
        delta.CreatedAt,
        delta.UpdatedAt);

    private sealed record SocialDataExport(
        int Version,
        DateTimeOffset ExportedAt,
        IReadOnlyList<SocialAccountExport> Accounts,
        IReadOnlyList<SocialMetricSnapshotExport> AccountSnapshots,
        IReadOnlyList<SocialVideoMetricSnapshotExport> ContentSnapshots,
        IReadOnlyList<SocialDailyMetricDeltaExport> DailyDeltas);

    private sealed record SocialAccountExport(
        int Id,
        string Platform,
        string AccountName,
        string? AccountHandle,
        string? AccountUrl,
        string? Color,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record SocialMetricSnapshotExport(
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

    private sealed record SocialVideoMetricSnapshotExport(
        int SocialAccountId,
        string PlatformVideoId,
        string Title,
        string? ThumbnailUrl,
        DateTimeOffset? PublishedAt,
        DateOnly SnapshotDate,
        int Clicks,
        int Likes,
        int Views,
        int Comments,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record SocialDailyMetricDeltaExport(
        int SocialAccountId,
        string Scope,
        string MetricKey,
        string Title,
        DateOnly SnapshotDate,
        int Clicks,
        int Likes,
        int Views,
        int Comments,
        int Shares,
        int Followers,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
