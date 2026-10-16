using System.Diagnostics;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/media-library")]
public sealed class MediaLibraryController(IWebHostEnvironment environment, AuthService auth) : ControllerBase
{
    private const long MaximumImageBytes = 15L * 1024 * 1024;
    private const long MaximumVideoBytes = 200L * 1024 * 1024;
    private const long MaximumRequestBytes = 250L * 1024 * 1024;
    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp",
        ["image/gif"] = ".gif", ["image/bmp"] = ".bmp"
    };
    private static readonly Dictionary<string, string> VideoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = ".mp4", ["video/quicktime"] = ".mov", ["video/webm"] = ".webm"
    };

    private string Root => Path.Combine(environment.ContentRootPath, "Data", "media-library");

    [HttpGet("folders")]
    public IActionResult Folders()
    {
        Directory.CreateDirectory(Root);
        return Ok(Directory.EnumerateDirectories(Root)
            .Select(path => new DirectoryInfo(path))
            .Where(folder => !folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .Select(folder => new
            {
                name = folder.Name,
                photoCount = Directory.EnumerateFiles(folder.FullName).Count(path => IsImage(path)),
                videoCount = Directory.EnumerateFiles(folder.FullName).Count(path => IsVideo(path))
            }));
    }

    [HttpPost("folders")]
    public IActionResult CreateFolder([FromBody] CreateMediaFolderRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (!ValidSegment(name)) return BadRequest("Choose a folder name of 1–80 characters without path separators or reserved characters.");
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name);
        if (Directory.Exists(path) || System.IO.File.Exists(path)) return Conflict("A folder with this name already exists.");
        Directory.CreateDirectory(path);
        return CreatedAtAction(nameof(Files), new { folder = name }, new { name, photoCount = 0, videoCount = 0 });
    }

    [HttpGet("folders/{folder}/files")]
    public IActionResult Files(string folder)
    {
        var path = FolderPath(folder);
        if (path is null) return NotFound();
        return Ok(Directory.EnumerateFiles(path)
            .Where(file => IsImage(file) || IsVideo(file))
            .Select(file => new FileInfo(file))
            .Where(file => !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new { fileName = file.Name, isVideo = IsVideo(file.Name), size = file.Length, updatedAt = file.LastWriteTimeUtc }));
    }

    [HttpPost("folders/{folder}/open")]
    public async Task<IActionResult> OpenFolder(string folder, CancellationToken cancellationToken)
    {
        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        if (!user.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, "Opening a folder on the host requires an administrator.");
        var path = FolderPath(folder);
        if (path is null) return NotFound();
        if (!OperatingSystem.IsWindows())
            return StatusCode(StatusCodes.Status501NotImplemented, "Opening folders is only supported on the Windows host.");
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        return Ok(new { folder = path });
    }

    [HttpGet("folders/{folder}/files/{fileName}")]
    public IActionResult MediaFile(string folder, string fileName)
    {
        var path = FolderPath(folder);
        if (path is null || !ValidSegment(fileName) || (!IsImage(fileName) && !IsVideo(fileName))) return NotFound();
        var file = Path.Combine(path, fileName);
        if (!System.IO.File.Exists(file) || System.IO.File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) return NotFound();
        var type = IsVideo(fileName)
            ? VideoTypes.First(entry => entry.Value.Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase)).Key
            : ImageTypes.First(entry => entry.Value.Equals(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase)).Key;
        return PhysicalFile(file, type, enableRangeProcessing: IsVideo(fileName));
    }

    [HttpPost("folders/{folder}/upload")]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes)]
    public async Task<IActionResult> Upload(string folder, CancellationToken cancellationToken)
    {
        var path = FolderPath(folder);
        if (path is null) return NotFound();
        var files = (await Request.ReadFormAsync(cancellationToken)).Files;
        if (files.Count is < 1 or > 20) return BadRequest("Choose 1–20 photos or videos.");
        var media = files.Select(ResolveMedia).ToArray();
        if (media.Any(item => item is null)) return BadRequest("Choose JPG, PNG, WebP, GIF, BMP, MP4, MOV, or WebM media.");
        for (var i = 0; i < files.Count; i++)
            if (files[i].Length <= 0 || files[i].Length > (media[i]!.Value.IsVideo ? MaximumVideoBytes : MaximumImageBytes))
                return BadRequest($"{files[i].FileName}: file is empty or exceeds its size limit.");

        var saved = new List<string>();
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                var destination = NewMediaPath(path, media[i]!.Value.Extension);
                await using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                saved.Add(destination);
                await files[i].CopyToAsync(stream, cancellationToken);
            }
        }
        catch
        {
            foreach (var file in saved) System.IO.File.Delete(file);
            throw;
        }
        return Ok(new { imageCount = media.Count(item => item is { IsVideo: false }), videoCount = media.Count(item => item is { IsVideo: true }) });
    }

    [HttpPost("folders/{folder}/upload-capture")]
    [RequestSizeLimit(MaximumRequestBytes)]
    public async Task<IActionResult> UploadCapture(string folder, CancellationToken cancellationToken)
    {
        var path = FolderPath(folder);
        if (path is null) return NotFound();
        var contentType = Request.ContentType?.Split(';', 2)[0].Trim() ?? "";
        if (!VideoTypes.TryGetValue(contentType, out var extension)) return BadRequest("Choose an MP4, MOV, or WebM video.");
        if (Request.ContentLength is > MaximumVideoBytes or 0) return BadRequest("Choose a video between 1 byte and 200 MB.");
        var destination = NewMediaPath(path, extension);
        try
        {
            long length;
            await using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await Request.Body.CopyToAsync(stream, cancellationToken);
                length = stream.Length;
            }
            if (length is <= 0 or > MaximumVideoBytes)
            {
                System.IO.File.Delete(destination);
                return BadRequest("Choose a video between 1 byte and 200 MB.");
            }
            return Ok(new { imageCount = 0, videoCount = 1 });
        }
        catch
        {
            if (System.IO.File.Exists(destination)) System.IO.File.Delete(destination);
            throw;
        }
    }

    [HttpPost("folders/{folder}/upload-file")]
    [RequestSizeLimit(MaximumVideoBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadFile(string folder, [FromQuery] string? fileName, CancellationToken cancellationToken)
    {
        var path = FolderPath(folder);
        if (path is null) return NotFound();
        var media = ResolveMedia(Request.ContentType, fileName);
        if (media is null) return BadRequest("Choose JPG, PNG, WebP, GIF, BMP, MP4, MOV, or WebM media.");
        var maximum = media.Value.IsVideo ? MaximumVideoBytes : MaximumImageBytes;
        var expectedHeader = Request.Headers["X-Upload-Expected-Bytes"].FirstOrDefault();
        var hasExpectedLength = long.TryParse(expectedHeader, out var expectedLength);
        if (Request.ContentLength is 0 || Request.ContentLength > maximum)
            return BadRequest($"The server received {Request.ContentLength ?? 0} B. Choose a nonempty file under {(media.Value.IsVideo ? 200 : 15)} MB.");

        var destination = NewMediaPath(path, media.Value.Extension);
        try
        {
            long length;
            await using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await Request.Body.CopyToAsync(stream, cancellationToken);
                length = stream.Length;
            }
            if (length is <= 0 || length > maximum)
            {
                System.IO.File.Delete(destination);
                return BadRequest($"The server received {length} B. Choose a nonempty file under {(media.Value.IsVideo ? 200 : 15)} MB.");
            }
            if (hasExpectedLength && length != expectedLength)
            {
                System.IO.File.Delete(destination);
                return BadRequest($"Upload was incomplete: browser sent {expectedLength} B but the server received {length} B. Try again.");
            }
            return Ok(new { imageCount = media.Value.IsVideo ? 0 : 1, videoCount = media.Value.IsVideo ? 1 : 0 });
        }
        catch
        {
            if (System.IO.File.Exists(destination)) System.IO.File.Delete(destination);
            throw;
        }
    }

    private string? FolderPath(string name)
    {
        if (!ValidSegment(name)) return null;
        var path = Path.Combine(Root, name);
        if (!Directory.Exists(path) || new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
        return path;
    }

    private static bool ValidSegment(string value)
    {
        if (value.Length is < 1 or > 80 || value is "." or ".." || value != value.Trim(' ', '.')) return false;
        if (value.Any(character => char.IsControl(character) || character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')) return false;
        var stem = value.Split('.')[0];
        return !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
            .Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    private static (string Extension, bool IsVideo)? ResolveMedia(IFormFile file)
    {
        return ResolveMedia(file.ContentType, file.FileName);
    }

    private static (string Extension, bool IsVideo)? ResolveMedia(string? contentType, string? fileName)
    {
        var type = contentType?.Split(';', 2)[0].Trim() ?? "";
        if (ImageTypes.TryGetValue(type, out var imageExtension)) return (imageExtension, false);
        if (VideoTypes.TryGetValue(type, out var videoExtension)) return (videoExtension, true);
        // Some file pickers omit the MIME type (especially for MOV files).
        if (type is "" or "application/octet-stream")
        {
            var extension = Path.GetExtension(fileName ?? "");
            if (extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) return (".jpg", false);
            if (IsImage(fileName ?? "")) return (extension.ToLowerInvariant(), false);
            if (IsVideo(fileName ?? "")) return (extension.ToLowerInvariant(), true);
        }
        return null;
    }

    private static string NewMediaPath(string folder, string extension) =>
        Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}");
    private static bool IsImage(string file) => ImageTypes.Values.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
    private static bool IsVideo(string file) => VideoTypes.Values.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
}

public sealed record CreateMediaFolderRequest(string? Name);
