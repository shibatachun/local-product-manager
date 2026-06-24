using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/social-accounts")]
public sealed class SocialAccountsController(
    AppDbContext db,
    AuthService auth,
    MetaOAuthService meta,
    SocialFetchLogService fetchLogs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SocialAccountDto>>> GetAll(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var accounts = await db.SocialAccounts
            .AsNoTracking()
            .Include(account => account.Metrics)
            .OrderBy(account => account.Platform)
            .ThenBy(account => account.AccountName)
            .ToListAsync(cancellationToken);

        return Ok(accounts.Select(account => ToDto(account, from, to)).ToArray());
    }

    [HttpPost]
    public async Task<ActionResult<SocialAccountDto>> Create(
        UpsertSocialAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        if (string.IsNullOrWhiteSpace(request.Platform) ||
            string.IsNullOrWhiteSpace(request.AccountName))
        {
            return BadRequest("Platform and account name are required.");
        }

        var account = new SocialAccount
        {
            Platform = request.Platform.Trim().ToLowerInvariant(),
            AccountName = request.AccountName.Trim(),
            AccountHandle = NormalizeOptional(request.AccountHandle),
            AccountUrl = NormalizeOptional(request.AccountUrl),
            Color = NormalizeOptional(request.Color),
            IsActive = request.IsActive
        };
        db.SocialAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { account.Id }, ToDto(account, null, null));
    }

    [HttpPut("{accountId:int}")]
    public async Task<ActionResult<SocialAccountDto>> Update(
        int accountId,
        UpsertSocialAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        var account = await db.SocialAccounts
            .Include(item => item.Metrics)
            .FirstOrDefaultAsync(item => item.Id == accountId, cancellationToken);
        if (account is null) return NotFound();

        account.Platform = request.Platform.Trim().ToLowerInvariant();
        account.AccountName = request.AccountName.Trim();
        account.AccountHandle = NormalizeOptional(request.AccountHandle);
        account.AccountUrl = NormalizeOptional(request.AccountUrl);
        account.Color = NormalizeOptional(request.Color);
        account.IsActive = request.IsActive;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(account, null, null));
    }

    [HttpDelete("{accountId:int}")]
    public async Task<IActionResult> Delete(int accountId, CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        var deleted = await db.SocialAccounts
            .Where(account => account.Id == accountId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0 ? NoContent() : NotFound();
    }

    [HttpPost("{accountId:int}/metrics")]
    public async Task<ActionResult<SocialMetricSnapshotDto>> UpsertMetric(
        int accountId,
        UpsertSocialMetricRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        if (!await db.SocialAccounts.AnyAsync(account => account.Id == accountId, cancellationToken))
        {
            return NotFound();
        }

        var metric = await db.SocialMetricSnapshots.FirstOrDefaultAsync(
            item => item.SocialAccountId == accountId && item.SnapshotDate == request.SnapshotDate,
            cancellationToken);
        if (metric is null)
        {
            metric = new SocialMetricSnapshot
            {
                SocialAccountId = accountId,
                SnapshotDate = request.SnapshotDate
            };
            db.SocialMetricSnapshots.Add(metric);
        }

        metric.Clicks = Math.Max(0, request.Clicks);
        metric.Likes = Math.Max(0, request.Likes);
        metric.Views = Math.Max(0, request.Views);
        metric.Comments = Math.Max(0, request.Comments);
        metric.Shares = Math.Max(0, request.Shares);
        metric.Followers = Math.Max(0, request.Followers);
        metric.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(metric));
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SocialSyncResult>> Sync(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        var accounts = await db.SocialAccounts.CountAsync(
            account => account.IsActive,
            cancellationToken);
        var imported = 0;
        var messages = new List<string>();
        try
        {
            imported += await meta.SyncAsync(db, cancellationToken);
            messages.Add("Meta synced.");
            await fetchLogs.RecordAsync(
                "social-accounts-sync",
                "success",
                imported,
                "Meta sync completed from the Social Accounts Sync API button.",
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            messages.Add($"Meta skipped: {ex.Message}");
            await fetchLogs.RecordAsync("social-accounts-sync", "skipped", 0, ex.Message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            messages.Add($"Meta failed: {ex.Message}");
            await fetchLogs.RecordAsync("social-accounts-sync", "failed", 0, ex.Message, cancellationToken);
        }

        return Ok(new SocialSyncResult(
            accounts,
            imported,
            messages.Count > 0 ? string.Join(" ", messages) : "No API connectors are configured."));
    }

    [HttpGet("debug")]
    public async Task<ActionResult<SocialDebugSnapshotDto>> Debug(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();

        var accounts = await db.SocialAccounts
            .AsNoTracking()
            .OrderBy(account => account.Platform)
            .ThenBy(account => account.AccountName)
            .ToListAsync(cancellationToken);
        var metrics = await db.SocialMetricSnapshots
            .AsNoTracking()
            .Include(metric => metric.SocialAccount)
            .OrderByDescending(metric => metric.SnapshotDate)
            .ThenBy(metric => metric.SocialAccount!.Platform)
            .ThenBy(metric => metric.SocialAccount!.AccountName)
            .Take(200)
            .ToListAsync(cancellationToken);
        var videoMetrics = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Include(metric => metric.SocialAccount)
            .OrderByDescending(metric => metric.SnapshotDate)
            .ThenBy(metric => metric.SocialAccount!.Platform)
            .ThenByDescending(metric => metric.Views)
            .Take(300)
            .ToListAsync(cancellationToken);
        var allMetrics = await db.SocialMetricSnapshots
            .AsNoTracking()
            .Include(metric => metric.SocialAccount)
            .ToListAsync(cancellationToken);
        var allVideoMetrics = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Include(metric => metric.SocialAccount)
            .ToListAsync(cancellationToken);
        var allDailyDeltas = await db.SocialDailyMetricDeltas
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var latestByAccount = allMetrics
            .GroupBy(metric => metric.SocialAccountId)
            .Select(group => group.OrderByDescending(metric => metric.SnapshotDate).First())
            .ToArray();
        var latestDeltaByAccount = allDailyDeltas
            .Where(delta => delta.Scope == "account" && delta.MetricKey == "__account__")
            .GroupBy(delta => delta.SocialAccountId)
            .Select(group => group.OrderByDescending(delta => delta.SnapshotDate).First())
            .ToArray();
        var platformTotals = accounts
            .GroupBy(account => account.Platform)
            .Select(group =>
            {
                var platform = group.Key;
                var accountIds = group.Select(account => account.Id).ToHashSet();
                var platformMetrics = allMetrics
                    .Where(metric => accountIds.Contains(metric.SocialAccountId))
                    .ToArray();
                var latestMetrics = latestByAccount
                    .Where(metric => accountIds.Contains(metric.SocialAccountId))
                    .ToArray();
                var latestDeltas = latestDeltaByAccount
                    .Where(delta => accountIds.Contains(delta.SocialAccountId))
                    .ToArray();
                return new SocialDebugPlatformTotalDto(
                    platform,
                    group.Count(),
                    platformMetrics.Length,
                    latestDeltas.Sum(metric => metric.Views),
                    latestDeltas.Sum(metric => metric.Likes),
                    latestDeltas.Sum(metric => metric.Comments),
                    latestDeltas.Sum(metric => metric.Shares),
                    latestDeltas.Sum(metric => metric.Followers),
                    latestMetrics.Sum(metric => metric.Views),
                    latestMetrics.Sum(metric => metric.Likes),
                    latestMetrics.Sum(metric => metric.Comments),
                    latestMetrics.Sum(metric => metric.Clicks));
            })
            .OrderBy(total => total.Platform)
            .ToArray();

        var latestVideoTotals = allVideoMetrics
            .GroupBy(metric => new
            {
                metric.SocialAccountId,
                metric.SocialAccount!.Platform,
                metric.SocialAccount.AccountName
            })
            .Select(group =>
            {
                var rowsForTotal = RowsForContentTotal(group);
                var latestRows = rowsForTotal
                    .GroupBy(metric => metric.PlatformVideoId)
                    .Select(videoGroup => videoGroup.OrderByDescending(metric => metric.SnapshotDate).First())
                    .ToArray();
                return new SocialDebugVideoTotalDto(
                    group.Key.Platform,
                    group.Key.SocialAccountId,
                    group.Key.AccountName,
                    latestRows.Length,
                    latestRows.Sum(metric => metric.Views),
                    latestRows.Sum(metric => metric.Likes),
                    latestRows.Sum(metric => metric.Comments),
                    latestRows.OrderByDescending(metric => metric.SnapshotDate).FirstOrDefault()?.SnapshotDate);
            })
            .OrderBy(total => total.Platform)
            .ThenBy(total => total.AccountName)
            .ToArray();

        return Ok(new SocialDebugSnapshotDto(
            DateTimeOffset.UtcNow,
            accounts.Select(account => new SocialDebugAccountDto(
                account.Id,
                account.Platform,
                account.AccountName,
                account.AccountHandle,
                account.IsActive)).ToArray(),
            metrics.Select(metric => new SocialDebugMetricDto(
                metric.Id,
                metric.SocialAccountId,
                metric.SocialAccount?.Platform ?? "",
                metric.SocialAccount?.AccountName ?? "",
                metric.SnapshotDate,
                metric.Clicks,
                metric.Likes,
                metric.Views,
                metric.Comments,
                metric.Shares,
                metric.Followers,
                metric.UpdatedAt)).ToArray(),
            videoMetrics.Select(metric => new SocialDebugVideoMetricDto(
                metric.Id,
                metric.SocialAccountId,
                metric.SocialAccount?.Platform ?? "",
                metric.SocialAccount?.AccountName ?? "",
                metric.PlatformVideoId,
                metric.Title,
                metric.SnapshotDate,
                metric.Views,
                metric.Likes,
                metric.Comments,
                metric.Clicks,
                metric.UpdatedAt)).ToArray(),
            platformTotals,
            latestVideoTotals));
    }

    private static IEnumerable<SocialVideoMetricSnapshot> RowsForContentTotal(
        IEnumerable<SocialVideoMetricSnapshot> rows)
    {
        var materialized = rows.ToArray();
        if (!materialized.Any(row => string.Equals(row.SocialAccount?.Platform, "pinterest", StringComparison.OrdinalIgnoreCase)))
        {
            return materialized;
        }

        var pins = materialized
            .Where(row => !row.Title.StartsWith("Board:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return pins.Length > 0 ? pins : materialized;
    }

    private static SocialAccountDto ToDto(SocialAccount account, DateOnly? from, DateOnly? to)
    {
        var metrics = account.Metrics.AsEnumerable();
        if (from.HasValue) metrics = metrics.Where(metric => metric.SnapshotDate >= from.Value);
        if (to.HasValue) metrics = metrics.Where(metric => metric.SnapshotDate <= to.Value);

        return new SocialAccountDto(
            account.Id,
            account.Platform,
            account.AccountName,
            account.AccountHandle,
            account.AccountUrl,
            account.Color,
            account.IsActive,
            metrics.OrderBy(metric => metric.SnapshotDate).Select(ToDto).ToArray(),
            account.CreatedAt,
            account.UpdatedAt);
    }

    private static SocialMetricSnapshotDto ToDto(SocialMetricSnapshot metric) => new(
        metric.Id,
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

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "Only admin can manage social account data.");
}
