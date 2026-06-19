using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/video-trends")]
public sealed class VideoTrendController(
    AppDbContext db,
    AuthService auth,
    VideoTrendLinkParser parser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VideoTrendItemDto>>> GetAll(
        [FromQuery] string? platform,
        CancellationToken cancellationToken)
    {
        var query = db.VideoTrendItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(platform))
        {
            var normalized = platform.Trim().ToLowerInvariant();
            query = query.Where(item => item.Platform == normalized);
        }

        var items = await query
            .OrderBy(item => item.Platform)
            .ThenByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
        return Ok(items.Select(ToDto).ToArray());
    }

    [HttpPost]
    public async Task<ActionResult<VideoTrendItemDto>> Create(
        CreateVideoTrendItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken)) return PermissionDenied();
        if (string.IsNullOrWhiteSpace(request.Url)) return BadRequest("Video URL is required.");
        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out _)) return BadRequest("Video URL must be a valid absolute URL.");

        var parsed = parser.Parse(request.Platform, request.Url);
        var item = new VideoTrendItem
        {
            Platform = parsed.Platform,
            Url = request.Url.Trim(),
            Title = NormalizeTitle(request.Title, parsed.PlatformVideoId, parsed.Platform),
            Description = NormalizeOptional(request.Description),
            Author = NormalizeOptional(request.Author) ?? parsed.Author,
            PlatformVideoId = parsed.PlatformVideoId,
            EmbedUrl = parsed.EmbedUrl,
            ThumbnailUrl = parsed.ThumbnailUrl,
            Tags = NormalizeOptional(request.Tags)
        };
        db.VideoTrendItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { platform = item.Platform }, ToDto(item));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken)) return PermissionDenied();
        var deleted = await db.VideoTrendItems
            .Where(item => item.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0 ? NoContent() : NotFound();
    }

    private static VideoTrendItemDto ToDto(VideoTrendItem item) => new(
        item.Id,
        item.Platform,
        item.Url,
        item.Title,
        item.Description,
        item.Author,
        item.PlatformVideoId,
        item.EmbedUrl,
        item.ThumbnailUrl,
        item.Tags,
        item.CreatedAt,
        item.UpdatedAt);

    private static string NormalizeTitle(string? title, string? id, string platform)
    {
        var trimmed = title?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed.Length <= 300 ? trimmed : trimmed[..300];
        return string.IsNullOrWhiteSpace(id) ? $"{platform} video" : $"{platform} video {id}";
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private ObjectResult PermissionDenied() =>
        StatusCode(StatusCodes.Status403Forbidden, "Only users with import permission can manage video trends.");
}
