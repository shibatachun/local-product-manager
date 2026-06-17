using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/social-fetch-logs")]
public sealed class SocialFetchLogsController(SocialFetchLogService logs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SocialFetchLogEntry>>> Get(
        CancellationToken cancellationToken)
    {
        if (!IsAdminHost()) return AdminOnly();
        return Ok(await logs.GetRecentAsync(100, cancellationToken));
    }

    [HttpPost("archive")]
    public async Task<ActionResult<ArchiveLogResult>> Archive(CancellationToken cancellationToken)
    {
        if (!IsAdminHost()) return AdminOnly();
        var path = await logs.ArchiveAsync(cancellationToken);
        return Ok(new ArchiveLogResult(path));
    }

    private bool IsAdminHost()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        return remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp);
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "This action is only available on the host computer through localhost.");
}

public sealed record ArchiveLogResult(string Path);
