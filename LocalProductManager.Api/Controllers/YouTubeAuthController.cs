using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/youtube")]
public sealed class YouTubeAuthController(
    AppDbContext db,
    YouTubeOAuthService youtube) : ControllerBase
{
    [HttpGet("auth/status")]
    public ActionResult<YouTubeAuthStatusDto> Status() => Ok(youtube.GetStatus());

    [HttpGet("auth/start")]
    public ActionResult<YouTubeAuthStartResult> Start()
    {
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

    [HttpGet("oauth/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
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
        try
        {
            var imported = await youtube.SyncChannelMetricsAsync(db, cancellationToken);
            return Ok(new SocialSyncResult(1, imported, "YouTube channel metrics synced."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }
}
