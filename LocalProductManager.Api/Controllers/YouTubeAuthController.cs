using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/youtube")]
public sealed class YouTubeAuthController(
    AppDbContext db,
    YouTubeOAuthService youtube,
    SocialFetchLogService fetchLogs,
    AuthService auth) : ControllerBase
{
    [HttpGet("auth/status")]
    public async Task<ActionResult<YouTubeAuthStatusDto>> Status(CancellationToken cancellationToken) =>
        Ok(youtube.GetStatus(await auth.CanManageSocialAsync(HttpContext, cancellationToken)));

    [HttpGet("auth/start")]
    public async Task<ActionResult<YouTubeAuthStartResult>> Start(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        try
        {
            return Ok(new YouTubeAuthStartResult(
                youtube.BuildAuthorizationUrl(youtube.RedirectUri()),
                youtube.RedirectUri()));
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
        await youtube.DisconnectAsync(cancellationToken);
        await db.SocialAccounts
            .Where(account => account.Platform == "youtube")
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
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        if (!string.IsNullOrWhiteSpace(error))
        {
            return Content(
                $"<html><body><h2>YouTube authorization failed</h2><p>{error}</p></body></html>",
                "text/html");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest("Google did not return an authorization code.");
        }

        try
        {
            await youtube.CompleteAuthorizationAsync(code, state, youtube.RedirectUri(), cancellationToken);
            return Content(
                """
                <html><body>
                <h2>YouTube connected</h2>
                <p>You can close this window and return to Local Product Manager.</p>
                <script>setTimeout(() => window.close(), 1200);</script>
                </body></html>
                """,
                "text/html");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            return Content(
                $"<html><body><h2>YouTube authorization failed</h2><p>{ex.Message}</p></body></html>",
                "text/html");
        }
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SocialSyncResult>> Sync(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        try
        {
            var imported = await youtube.SyncChannelMetricsAsync(db, cancellationToken);
            await fetchLogs.RecordAsync(
                "youtube-manual-sync",
                "success",
                imported,
                "YouTube channel metrics synced.",
                cancellationToken);
            return Ok(new SocialSyncResult(1, imported, "YouTube channel metrics synced."));
        }
        catch (InvalidOperationException ex)
        {
            await fetchLogs.RecordAsync("youtube-manual-sync", "failed", 0, ex.Message, cancellationToken);
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            await fetchLogs.RecordAsync("youtube-manual-sync", "failed", 0, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    [HttpGet("videos/debug")]
    public async Task<ActionResult<YouTubeDebugSnapshotDto>> VideosDebug(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        try
        {
            var snapshot = await youtube.GetDebugSnapshotAsync(db, cancellationToken);
            await fetchLogs.RecordAsync(
                "youtube-fetch-videos",
                "success",
                snapshot.Videos.Count + 1,
                $"Fetched {snapshot.Videos.Count} videos for {snapshot.Channel.Title}.",
                cancellationToken);
            return Ok(snapshot);
        }
        catch (InvalidOperationException ex)
        {
            await fetchLogs.RecordAsync("youtube-fetch-videos", "failed", 0, ex.Message, cancellationToken);
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            await fetchLogs.RecordAsync("youtube-fetch-videos", "failed", 0, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    [HttpGet("videos")]
    public async Task<ActionResult<YouTubeDebugSnapshotDto>> StoredVideos(CancellationToken cancellationToken)
    {
        var snapshot = await youtube.GetStoredSnapshotAsync(db, cancellationToken);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "Only admin can manage YouTube authorization and fetches.");
}
