using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/social-fetch-logs")]
public sealed class SocialFetchLogsController(SocialFetchLogService logs, AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SocialFetchLogEntry>>> Get(
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        return Ok(await logs.GetRecentAsync(100, cancellationToken));
    }

    [HttpPost("archive")]
    public async Task<ActionResult<ArchiveLogResult>> Archive(CancellationToken cancellationToken)
    {
        if (!await auth.CanManageSocialAsync(HttpContext, cancellationToken)) return AdminOnly();
        var path = await logs.ArchiveAsync(cancellationToken);
        return Ok(new ArchiveLogResult(path));
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "Only admin can view or archive social fetch logs.");
}

public sealed record ArchiveLogResult(string Path);
