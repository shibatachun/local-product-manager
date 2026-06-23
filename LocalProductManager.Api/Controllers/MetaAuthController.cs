using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/meta")]
public sealed class MetaAuthController(
    AppDbContext db,
    MetaOAuthService meta,
    SocialFetchLogService fetchLogs,
    AuthService auth) : ControllerBase
{
    [HttpGet("auth/status")]
    public async Task<ActionResult<MetaAuthStatusDto>> Status(CancellationToken cancellationToken) =>
        Ok(meta.GetStatus(await auth.CanManageSocialAsync(HttpContext, cancellationToken)));

    [HttpGet("auth/start")]
    public async Task<ActionResult<MetaAuthStartResult>> Start(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        try
        {
            return Ok(new MetaAuthStartResult(
                meta.BuildAuthorizationUrl(meta.RedirectUri()),
                meta.RedirectUri()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("auth/disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        await meta.DisconnectAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("oauth/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        if (!string.IsNullOrWhiteSpace(error))
        {
            return Content(
                $"<html><body><h2>Meta authorization failed</h2><p>{error}</p></body></html>",
                "text/html");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest("Meta did not return an authorization code.");
        }

        try
        {
            await meta.CompleteAuthorizationAsync(code, state, meta.RedirectUri(), cancellationToken);
            return Content(
                """
                <html><body>
                <h2>Meta connected</h2>
                <p>You can close this window and return to Local Product Manager.</p>
                <script>setTimeout(() => window.close(), 1200);</script>
                </body></html>
                """,
                "text/html");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return Content(
                $"<html><body><h2>Meta authorization failed</h2><p>{ex.Message}</p></body></html>",
                "text/html");
        }
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SocialSyncResult>> Sync(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        try
        {
            var imported = await meta.SyncAsync(db, cancellationToken);
            await fetchLogs.RecordAsync(
                "meta-manual-sync",
                "success",
                imported,
                "Meta Facebook/Instagram account metrics synced.",
                cancellationToken);
            return Ok(new SocialSyncResult(imported, imported, "Meta Facebook/Instagram account metrics synced."));
        }
        catch (InvalidOperationException ex)
        {
            await fetchLogs.RecordAsync("meta-manual-sync", "failed", 0, ex.Message, cancellationToken);
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            await fetchLogs.RecordAsync("meta-manual-sync", "failed", 0, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
        catch (DbUpdateException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            await fetchLogs.RecordAsync("meta-manual-sync", "failed", 0, message, cancellationToken);
            return BadRequest($"Meta sync failed while saving data: {message}");
        }
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "Only admin can manage Meta authorization and imports.");
}
