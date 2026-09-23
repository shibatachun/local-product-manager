using System.Diagnostics;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/live-stream-bid-products/{productId:int}/image")]
public sealed class LiveStreamBidProductImagesController(
    AppDbContext db,
    ImageFileService imageFiles,
    ImageThumbnailService thumbnails,
    AuthService auth,
    IWebHostEnvironment environment) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };
    private static readonly HashSet<string> AllowedVideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".webm" };
    private static readonly Dictionary<string, string> VideoExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["video/mp4"] = ".mp4",
            ["video/quicktime"] = ".mov",
            ["video/webm"] = ".webm"
        };
    private static readonly Dictionary<string, string> ExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
            ["image/bmp"] = ".bmp"
        };
    private const long MaximumImageBytes = 15L * 1024 * 1024;
    private const long MaximumVideoBytes = 200L * 1024 * 1024;
    private const long MaximumVideoRequestBytes = 250L * 1024 * 1024;
    private const long MaximumRequestBytes = 150L * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Get(
        int productId,
        [FromQuery] bool download,
        [FromQuery] int? thumbnail,
        CancellationToken cancellationToken)
    {
        var product = await db.LiveStreamBidProducts.AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new { item.Uuid, item.ImagePath })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null) return NotFound();
        if (string.IsNullOrWhiteSpace(product.ImagePath)) return NotFound("This BID product does not have a picture.");

        try
        {
            var image = imageFiles.GetImage(product.ImagePath);
            if (!download && thumbnail is not null)
            {
                var file = new FileInfo(image.FullPath);
                var result = await thumbnails.GetOrCreateAsync(
                    $"bid:{image.FullPath}|{file.Length}|{file.LastWriteTimeUtc.Ticks}",
                    thumbnail.Value,
                    _ => Task.FromResult<Stream>(new FileStream(
                        image.FullPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true)),
                    cancellationToken);
                Response.Headers.CacheControl = "private, max-age=3600";
                return PhysicalFile(result.FullPath, result.ContentType);
            }

            Response.Headers.CacheControl = "private, max-age=300";
            if (!download)
                return PhysicalFile(image.FullPath, image.ContentType, enableRangeProcessing: true);

            var safeUuid = string.Concat(product.Uuid.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim(' ', '.');
            var downloadName = $"BID-{(string.IsNullOrWhiteSpace(safeUuid) ? productId : safeUuid)}{Path.GetExtension(image.FullPath)}";
            return PhysicalFile(image.FullPath, image.ContentType, downloadName, enableRangeProcessing: true);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException exception) { return StatusCode(StatusCodes.Status403Forbidden, exception.Message); }
        catch (InvalidOperationException exception) { return BadRequest(exception.Message); }
        catch (IOException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, "The image storage is temporarily unavailable."); }
    }

    [HttpPost("upload-many")]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes)]
    public async Task<ActionResult<LiveStreamBidProductImageUploadResult>> UploadMany(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();
        // Camera captures from mobile Safari and installed PWAs can use a
        // browser-generated multipart field name, so read every file section.
        var form = await Request.ReadFormAsync(cancellationToken);
        var files = form.Files;
        if (files.Count == 0) return BadRequest("Choose at least one image.");
        if (files.Count > 20) return BadRequest("Upload no more than 20 images at once.");

        var uploads = new List<(IFormFile File, string Extension)>();
        foreach (var file in files)
        {
            if (file.Length is <= 0 or > MaximumImageBytes)
                return BadRequest("Each image must be no larger than 15 MB.");
            var extension = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(extension) &&
                ExtensionsByContentType.TryGetValue(file.ContentType, out var contentTypeExtension))
                extension = contentTypeExtension;
            if (!AllowedExtensions.Contains(extension) ||
                !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Choose JPG, PNG, WebP, GIF, or BMP images.");
            uploads.Add((file, extension.ToLowerInvariant()));
        }

        var folder = GetProductFolder(product.Uuid);
        var savedPaths = new List<string>();
        try
        {
            foreach (var upload in uploads)
            {
                var fullPath = Path.Combine(folder, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{upload.Extension}");
                await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
                await upload.File.CopyToAsync(output, cancellationToken);
                savedPaths.Add(fullPath);
            }
        }
        catch
        {
            foreach (var path in savedPaths)
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            throw;
        }

        product.ImagePath = savedPaths[0];
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new LiveStreamBidProductImageUploadResult(product.ImagePath, product.UpdatedAt, savedPaths.Count));
    }

    [HttpGet("videos")]
    public async Task<IActionResult> Videos(int productId, CancellationToken cancellationToken)
    {
        var uuid = await db.LiveStreamBidProducts.AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => item.Uuid)
            .FirstOrDefaultAsync(cancellationToken);
        if (uuid is null) return NotFound();

        var videos = Directory.EnumerateFiles(GetProductFolder(uuid))
            .Where(path => AllowedVideoExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new { fileName = file.Name, updatedAt = file.LastWriteTimeUtc });
        return Ok(videos);
    }

    [HttpGet("videos/{fileName}")]
    public async Task<IActionResult> Video(int productId, string fileName, CancellationToken cancellationToken)
    {
        var uuid = await db.LiveStreamBidProducts.AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => item.Uuid)
            .FirstOrDefaultAsync(cancellationToken);
        if (uuid is null) return NotFound();
        if (Path.GetFileName(fileName) != fileName || !AllowedVideoExtensions.Contains(Path.GetExtension(fileName)))
            return BadRequest("Invalid video file name.");

        var path = Path.Combine(GetProductFolder(uuid), fileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => "video/mp4"
        };
        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    [HttpPost("videos/upload-many")]
    [RequestSizeLimit(MaximumVideoRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumVideoRequestBytes)]
    public async Task<IActionResult> UploadVideos(int productId, CancellationToken cancellationToken)
    {
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();

        var files = (await Request.ReadFormAsync(cancellationToken)).Files;
        if (files.Count == 0) return BadRequest("Choose at least one video.");
        if (files.Count > 20) return BadRequest("Upload no more than 20 videos at once.");

        var uploads = new List<(IFormFile File, string Extension)>();
        foreach (var file in files)
        {
            if (file.Length is <= 0 or > MaximumVideoBytes)
                return BadRequest("Each video must be no larger than 200 MB.");
            var extension = Path.GetExtension(file.FileName);
            if (!AllowedVideoExtensions.Contains(extension) &&
                VideoExtensionsByContentType.TryGetValue(file.ContentType, out var contentTypeExtension))
                extension = contentTypeExtension;
            if (!AllowedVideoExtensions.Contains(extension) ||
                !file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Choose MP4, MOV, or WebM videos.");
            uploads.Add((file, extension.ToLowerInvariant()));
        }

        var folder = GetProductFolder(product.Uuid);
        var savedPaths = new List<string>();
        try
        {
            foreach (var upload in uploads)
            {
                var path = Path.Combine(folder, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{upload.Extension}");
                savedPaths.Add(path);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
                await upload.File.CopyToAsync(output, cancellationToken);
            }
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new { videoCount = savedPaths.Count });
        }
        catch
        {
            foreach (var path in savedPaths)
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            throw;
        }
    }

    [HttpPost("videos/upload-capture")]
    [RequestSizeLimit(MaximumVideoRequestBytes)]
    public async Task<IActionResult> UploadCapturedVideo(int productId, CancellationToken cancellationToken)
    {
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();

        var contentType = Request.ContentType ?? "";
        if (!contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return BadRequest("The camera did not return a supported video.");
        var suppliedName = Path.GetFileName(Request.Headers["X-Upload-File-Name"].FirstOrDefault() ?? "mobile-capture.mp4");
        var extension = Path.GetExtension(suppliedName);
        if (!AllowedVideoExtensions.Contains(extension))
            extension = VideoExtensionsByContentType.TryGetValue(contentType, out var mappedExtension) ? mappedExtension : "";
        if (!AllowedVideoExtensions.Contains(extension)) return BadRequest("Choose MP4, MOV, or WebM videos.");
        if (Request.ContentLength == 0) return BadRequest("The camera returned an empty video.");

        var path = Path.Combine(GetProductFolder(product.Uuid),
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                await Request.Body.CopyToAsync(output, cancellationToken);
            var length = new FileInfo(path).Length;
            if (length is <= 0 or > MaximumVideoBytes)
            {
                System.IO.File.Delete(path);
                return BadRequest(length == 0 ? "The camera returned an empty video." : "Choose a video no larger than 200 MB.");
            }
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new { videoCount = 1 });
        }
        catch
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            throw;
        }
    }

    [HttpPost("open-folder")]
    public async Task<IActionResult> OpenFolder(int productId, CancellationToken cancellationToken)
    {
        var currentUser = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        if (!currentUser.IsAdmin || !AuthService.IsHost(HttpContext))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Opening a BID product folder requires an administrator on the host computer.");

        var uuid = await db.LiveStreamBidProducts.AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => item.Uuid)
            .FirstOrDefaultAsync(cancellationToken);
        if (uuid is null) return NotFound();
        if (!OperatingSystem.IsWindows())
            return StatusCode(StatusCodes.Status501NotImplemented, "Opening folders is only supported on the Windows host.");

        var folder = GetProductFolder(uuid);
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        return Ok(new { folder });
    }

    private string GetProductFolder(string uuid)
    {
        var normalizedUuid = uuid.Trim();
        var safeFolderName = string.Concat(normalizedUuid.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safeFolderName)) safeFolderName = "unknown-uuid";

        var root = Path.Combine(environment.ContentRootPath, "Data", "live-stream-bid-product-images");
        Directory.CreateDirectory(root);
        imageFiles.AddRoot(root);
        var folder = Path.Combine(root, safeFolderName);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
