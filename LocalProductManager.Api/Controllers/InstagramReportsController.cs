using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/instagram")]
public sealed class InstagramReportsController(
    InstagramReportImportService instagramReports,
    SocialFetchLogService fetchLogs,
    AuthService auth) : ControllerBase
{
    [HttpPost("report/import")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<InstagramReportImportResult>> ImportReport(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Only admin can import Instagram reports.");
        }
        if (file.Length == 0) return BadRequest("Instagram report file is empty.");

        try
        {
            var result = await instagramReports.ImportAsync(file, cancellationToken);
            await fetchLogs.RecordAsync(
                "instagram-report-import",
                "success",
                result.PostsImported,
                $"Imported Instagram CSV for {result.AccountName}.",
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            await fetchLogs.RecordAsync("instagram-report-import", "failed", 0, ex.Message, cancellationToken);
            return BadRequest(ex.Message);
        }
        catch (IOException ex)
        {
            await fetchLogs.RecordAsync("instagram-report-import", "failed", 0, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (DbUpdateException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            await fetchLogs.RecordAsync("instagram-report-import", "failed", 0, message, cancellationToken);
            return BadRequest($"Instagram report import failed while saving data: {message}");
        }
    }

    [HttpGet("posts")]
    public async Task<ActionResult<InstagramContentSnapshotDto>> Posts(
        [FromQuery] int? accountId,
        CancellationToken cancellationToken)
    {
        var snapshot = await instagramReports.GetStoredSnapshotAsync(accountId, cancellationToken);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }
}
