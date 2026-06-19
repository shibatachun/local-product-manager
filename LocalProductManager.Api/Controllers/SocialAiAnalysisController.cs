using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/social-ai-analysis")]
public sealed class SocialAiAnalysisController(
    SocialAiAnalysisService analysis,
    SocialFetchLogService fetchLogs,
    AuthService auth) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<SocialAiAnalysisStatusDto> Status() => Ok(analysis.GetStatus());

    [HttpGet("reports")]
    public async Task<ActionResult<IReadOnlyList<SocialAiAnalysisReportDto>>> Reports(
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        return Ok(await analysis.GetRecentReportsAsync(limit <= 0 ? 20 : limit, cancellationToken));
    }

    [HttpPost("run")]
    public async Task<ActionResult<SocialAiAnalysisResultDto>> Run(
        SocialAiAnalysisRunRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        try
        {
            var result = await analysis.RunAsync(request.Period, request.Date, cancellationToken);
            await fetchLogs.RecordAsync(
                $"ai-analysis-{result.Period}",
                "success",
                1,
                $"Generated {result.Period} AI social analysis: {result.Path}",
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            await fetchLogs.RecordAsync("ai-analysis", "failed", 0, ex.Message, cancellationToken);
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            await fetchLogs.RecordAsync("ai-analysis", "failed", 0, ex.Message, cancellationToken);
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "Only admin can run social AI analysis.");
}
