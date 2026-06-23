using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/social-data")]
public sealed class SocialDataMaintenanceController(
    SocialDataMaintenanceService maintenance,
    AuthService auth) : ControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken)) return AdminOnly();
        var content = await maintenance.ExportAsync(cancellationToken);
        return File(
            content,
            "application/json",
            $"social-data-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    }

    [HttpPost("import")]
    [RequestSizeLimit(100_000_000)]
    public async Task<ActionResult<SocialDataMaintenanceResult>> Import(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken)) return AdminOnly();
        try
        {
            return Ok(await maintenance.ImportAsync(file, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("reset")]
    public async Task<ActionResult<SocialDataMaintenanceResult>> Reset(
        ResetSocialDataRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken)) return AdminOnly();
        try
        {
            return Ok(await maintenance.ResetAsync(request.AdminPassword, cancellationToken));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "This action requires an admin session on the host computer.");
}
