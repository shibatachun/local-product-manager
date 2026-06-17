using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/pinterest")]
public sealed class PinterestAuthController(
    AppDbContext db,
    PinterestOAuthService pinterest,
    SocialFetchLogService fetchLogs) : ControllerBase
{
    [HttpGet("auth/status")]
    public ActionResult<PinterestAuthStatusDto> Status() => Ok(pinterest.GetStatus(IsAdminHost()));

    [HttpGet("auth/start")]
    public ActionResult<PinterestAuthStartResult> Start()
    {
        if (!IsAdminHost()) return AdminOnly();
        try
        {
            return Ok(new PinterestAuthStartResult(
                pinterest.BuildAuthorizationUrl(pinterest.RedirectUri()),
                pinterest.RedirectUri()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("auth/disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        if (!IsAdminHost()) return AdminOnly();
        await pinterest.DisconnectAsync(cancellationToken);
        await db.SocialAccounts
            .Where(account => account.Platform == "pinterest")
            .ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("oauth/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!IsAdminHost()) return AdminOnly();
        if (!string.IsNullOrWhiteSpace(error))
        {
            return Content(
                $"<html><body><h2>Pinterest authorization failed</h2><p>{error}</p></body></html>",
                "text/html");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest("Pinterest did not return an authorization code.");
        }

        try
        {
            await pinterest.CompleteAuthorizationAsync(code, state, pinterest.RedirectUri(), cancellationToken);
            return Content(
                """
                <html><body>
                <h2>Pinterest connected</h2>
                <p>You can close this window and return to Local Product Manager.</p>
                <script>setTimeout(() => window.close(), 1200);</script>
                </body></html>
                """,
                "text/html");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return Content(
                $"<html><body><h2>Pinterest authorization failed</h2><p>{ex.Message}</p></body></html>",
                "text/html");
        }
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SocialSyncResult>> Sync(CancellationToken cancellationToken)
    {
        if (!IsAdminHost()) return AdminOnly();
        try
        {
            var imported = await pinterest.SyncAccountMetricsAsync(db, cancellationToken);
            await fetchLogs.RecordAsync(
                "pinterest-manual-sync",
                "success",
                imported,
                "Pinterest account metrics synced.",
                cancellationToken);
            return Ok(new SocialSyncResult(1, imported, "Pinterest account metrics synced."));
        }
        catch (InvalidOperationException ex)
        {
            await fetchLogs.RecordAsync("pinterest-manual-sync", "failed", 0, ex.Message, cancellationToken);
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            await fetchLogs.RecordAsync("pinterest-manual-sync", "failed", 0, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    private bool IsAdminHost()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        return remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp);
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "This action is only available on the host computer through localhost.");
}
