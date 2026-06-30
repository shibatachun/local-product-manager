using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/tiktok")]
public sealed class TikTokReportsController(
    TikTokReportImportService tiktokReports,
    SocialFetchLogService fetchLogs,
    AuthService auth) : ControllerBase
{
    [HttpPost("report/import")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<TikTokReportImportResult>> ImportReport(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Only admin can import TikTok reports.");
        }
        if (file.Length == 0) return BadRequest("TikTok report file is empty.");

        try
        {
            var result = await tiktokReports.ImportAsync(file, cancellationToken);
            await fetchLogs.RecordAsync(
                "tiktok-report-import",
                "success",
                result.RowsImported,
                $"Imported TikTok overview for {result.AccountName}.",
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            await fetchLogs.RecordAsync("tiktok-report-import", "failed", 0, ex.Message, cancellationToken);
            return BadRequest(ex.Message);
        }
        catch (IOException ex)
        {
            await fetchLogs.RecordAsync("tiktok-report-import", "failed", 0, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (DbUpdateException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            await fetchLogs.RecordAsync("tiktok-report-import", "failed", 0, message, cancellationToken);
            return BadRequest($"TikTok report import failed while saving data: {message}");
        }
    }
}
