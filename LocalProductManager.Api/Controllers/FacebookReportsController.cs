using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/facebook")]
public sealed class FacebookReportsController(
    FacebookReportImportService facebookReports,
    SocialFetchLogService fetchLogs,
    AuthService auth) : ControllerBase
{
    [HttpPost("report/import")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<FacebookReportImportResult>> ImportReport(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Only admin can import Facebook reports.");
        }
        if (file.Length == 0) return BadRequest("Facebook report file is empty.");

        try
        {
            var result = await facebookReports.ImportAsync(file, cancellationToken);
            await fetchLogs.RecordAsync(
                "facebook-report-import",
                "success",
                result.PostsImported,
                $"Imported Facebook CSV for {result.AccountName}.",
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            await fetchLogs.RecordAsync("facebook-report-import", "failed", 0, ex.Message, cancellationToken);
            return BadRequest(ex.Message);
        }
        catch (IOException ex)
        {
            await fetchLogs.RecordAsync("facebook-report-import", "failed", 0, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (DbUpdateException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            await fetchLogs.RecordAsync("facebook-report-import", "failed", 0, message, cancellationToken);
            return BadRequest($"Facebook report import failed while saving data: {message}");
        }
    }

    [HttpGet("posts")]
    public async Task<ActionResult<FacebookContentSnapshotDto>> Posts(
        [FromQuery] int? accountId,
        CancellationToken cancellationToken)
    {
        var snapshot = await facebookReports.GetStoredSnapshotAsync(accountId, cancellationToken);
        return snapshot is null ? NotFound() : Ok(snapshot);
    }
}
