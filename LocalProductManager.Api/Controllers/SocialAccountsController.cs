using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/social-accounts")]
public sealed class SocialAccountsController(AppDbContext db) : ControllerBase
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
        if (!IsAdminHost()) return AdminOnly();
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
        if (!IsAdminHost()) return AdminOnly();
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
        if (!IsAdminHost()) return AdminOnly();
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
        if (!IsAdminHost()) return AdminOnly();
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
        if (!IsAdminHost()) return AdminOnly();
        var accounts = await db.SocialAccounts.CountAsync(
            account => account.IsActive,
            cancellationToken);
        return Ok(new SocialSyncResult(
            accounts,
            0,
            "API connectors are reserved for a later integration."));
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

    private bool IsAdminHost()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        return remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp);
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "This action is only available on the host computer through localhost.");
}
