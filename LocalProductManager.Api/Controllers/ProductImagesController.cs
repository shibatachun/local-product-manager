using System.Diagnostics;
using System.IO.Compression;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/products/{productId:int}/image")]
public sealed class ProductImagesController(
    AppDbContext db,
    ImageFileService imageFiles,
    ProductImageFolderService productImageFolders,
    AuthService auth,
    ImageThumbnailService thumbnails,
    RemoteImageProxyService remoteImages,
    AmazonImageLookupService amazonImages) : ControllerBase
{
    private static readonly HashSet<string> AllowedUploadExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };
    private static readonly HashSet<string> AllowedVideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".webm" };
    private static readonly Dictionary<string, string> UploadExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
            ["image/bmp"] = ".bmp"
        };
    private static readonly Dictionary<string, string> VideoExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["video/mp4"] = ".mp4",
            ["video/quicktime"] = ".mov",
            ["video/webm"] = ".webm"
        };
    private const long MaximumImageBytes = 15L * 1024 * 1024;
    private const long MaximumVideoBytes = 200L * 1024 * 1024;
    private const long MaximumMediaRequestBytes = 250L * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Get(
        int productId,
        [FromQuery] bool download,
        [FromQuery] int? thumbnail,
        CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new { item.ImagePath, item.ImageSource })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(product.ImagePath))
        {
            return NotFound("This product does not have an image path.");
        }

        try
        {
            if (!download && thumbnail is not null)
            {
                var size = Math.Clamp(thumbnail.Value, 48, 512);
                ImageThumbnailResult result;
                if (product.ImageSource == "remote")
                {
                    result = await thumbnails.GetOrCreateAsync(
                        $"remote:{product.ImagePath}",
                        size,
                        async token =>
                        {
                            var remoteImage = await remoteImages.GetImageAsync(product.ImagePath, token);
                            return remoteImage.Stream;
                        },
                        cancellationToken);
                }
                else
                {
                    var localImage = imageFiles.GetImage(product.ImagePath);
                    var file = new FileInfo(localImage.FullPath);
                    result = await thumbnails.GetOrCreateAsync(
                        $"local:{localImage.FullPath}|{file.Length}|{file.LastWriteTimeUtc.Ticks}",
                        size,
                        _ => Task.FromResult<Stream>(new FileStream(
                            localImage.FullPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete,
                            bufferSize: 64 * 1024,
                            useAsync: true)),
                        cancellationToken);
                }

                Response.Headers.CacheControl = "private, max-age=3600";
                Response.Headers.LastModified = result.LastModified.ToString("R");
                return PhysicalFile(
                    result.FullPath,
                    result.ContentType,
                    enableRangeProcessing: false);
            }

            if (product.ImageSource == "remote")
            {
                var remoteImage = await remoteImages.GetImageAsync(product.ImagePath, cancellationToken);
                Response.Headers.CacheControl = "private, max-age=300";
                return download
                    ? File(remoteImage.Stream, remoteImage.ContentType, remoteImage.FileName)
                    : File(remoteImage.Stream, remoteImage.ContentType);
            }

            var image = imageFiles.GetImage(product.ImagePath);
            var stream = new FileStream(
                image.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);

            Response.Headers.CacheControl = "private, max-age=300";
            return download
                ? File(stream, image.ContentType, image.FileName, enableRangeProcessing: true)
                : File(stream, image.ContentType, enableRangeProcessing: true);
        }
        catch (FileNotFoundException)
        {
            return NotFound("The configured image file could not be found by the server.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (IOException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "The image share is temporarily unavailable.");
        }
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaximumImageBytes)]
    public async Task<IActionResult> Upload(
        int productId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();
        if (file.Length is <= 0 or > MaximumImageBytes)
            return BadRequest("Choose an image no larger than 15 MB.");

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedUploadExtensions.Contains(extension) &&
            UploadExtensionsByContentType.TryGetValue(file.ContentType, out var contentTypeExtension))
        {
            extension = contentTypeExtension;
        }
        if (!AllowedUploadExtensions.Contains(extension) ||
            !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Choose a JPG, PNG, WebP, GIF, or BMP image.");

        var uploadRoot = productImageFolders.GetProductFolder(product.Id, product.LocalInventoryId);
        var fullPath = Path.Combine(uploadRoot, $"{productId}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        product.ImagePath = fullPath;
        product.ImageSource = "local";
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { imagePath = fullPath });
    }

    [HttpPost("upload-many")]
    [RequestSizeLimit(MaximumMediaRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumMediaRequestBytes)]
    public async Task<IActionResult> UploadMany(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();
        // Read all multipart file sections instead of relying on a parameter named
        // "files". Mobile Safari/installed PWAs may submit camera captures under a
        // browser-generated field name, which otherwise binds as an empty list.
        var form = await Request.ReadFormAsync(cancellationToken);
        var files = form.Files;
        if (files.Count == 0) return BadRequest("Choose at least one image or video.");
        if (files.Count > 20) return BadRequest("Upload no more than 20 media files at once.");

        var productFolder = productImageFolders.GetProductFolder(product.Id, product.LocalInventoryId);
        var savedPaths = new List<string>();
        foreach (var file in files)
        {
            var media = ResolveUploadMedia(file);
            if (media is null)
                return BadRequest($"{file.FileName}: choose a JPG, PNG, WebP, GIF, BMP, MP4, MOV, or WebM file.");
            if (file.Length <= 0 || file.Length > (media.Value.IsVideo ? MaximumVideoBytes : MaximumImageBytes))
                return BadRequest(media.Value.IsVideo
                    ? $"{file.FileName}: choose a video no larger than 200 MB."
                    : $"{file.FileName}: choose an image no larger than 15 MB.");

            var fullPath = Path.Combine(
                productFolder,
                $"{productId}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{media.Value.Extension}");
            await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(stream, cancellationToken);
            savedPaths.Add(fullPath);
        }

        var savedImagePaths = savedPaths
            .Where(path => AllowedUploadExtensions.Contains(Path.GetExtension(path)))
            .ToArray();
        if (string.IsNullOrWhiteSpace(product.ImagePath) && savedImagePaths.Length > 0)
        {
            product.ImagePath = savedImagePaths[0];
            product.ImageSource = "local";
        }
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new
        {
            imagePath = product.ImagePath,
            imagePaths = savedImagePaths,
            mediaPaths = savedPaths,
            imageCount = savedImagePaths.Length,
            videoCount = savedPaths.Count - savedImagePaths.Length,
            count = savedPaths.Count
        });
    }

    [HttpPost("upload-capture")]
    [RequestSizeLimit(MaximumMediaRequestBytes)]
    public async Task<IActionResult> UploadCapture(int productId, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();

        var suppliedName = Request.Headers["X-Upload-File-Name"].FirstOrDefault() ?? "mobile-capture.mp4";
        var safeName = Path.GetFileName(suppliedName);
        var contentType = Request.ContentType ?? "video/mp4";
        var extension = Path.GetExtension(safeName);
        if (!AllowedVideoExtensions.Contains(extension))
        {
            extension = VideoExtensionsByContentType.TryGetValue(contentType, out var mappedExtension)
                ? mappedExtension
                : ".mp4";
        }
        if (!contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return BadRequest("The captured file is not a supported video.");
        if (Request.ContentLength == 0)
            return BadRequest("The camera returned an empty video.");

        var productFolder = productImageFolders.GetProductFolder(product.Id, product.LocalInventoryId);
        var fullPath = Path.Combine(
            productFolder,
            $"{productId}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        try
        {
            long uploadedLength;
            await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await Request.Body.CopyToAsync(stream, cancellationToken);
                uploadedLength = stream.Length;
            }
            if (uploadedLength == 0 || uploadedLength > MaximumVideoBytes)
            {
                System.IO.File.Delete(fullPath);
                return uploadedLength == 0
                    ? BadRequest("The camera returned an empty video.")
                    : BadRequest("Choose a video no larger than 200 MB.");
            }

            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new
            {
                imagePath = product.ImagePath,
                imagePaths = Array.Empty<string>(),
                mediaPaths = new[] { fullPath },
                imageCount = 0,
                videoCount = 1,
                count = 1
            });
        }
        catch
        {
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
            throw;
        }
    }

    [HttpGet("files")]
    public async Task<IActionResult> Files(int productId, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new { item.Id, item.LocalInventoryId, item.ImagePath })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null) return NotFound();

        var folder = productImageFolders.GetProductFolder(product.Id, product.LocalInventoryId);
        var primaryPath = string.IsNullOrWhiteSpace(product.ImagePath) ||
                          Uri.TryCreate(product.ImagePath, UriKind.Absolute, out var primaryUri) &&
                          (primaryUri.Scheme == Uri.UriSchemeHttp || primaryUri.Scheme == Uri.UriSchemeHttps)
            ? null
            : Path.GetFullPath(product.ImagePath);
        var files = Directory.EnumerateFiles(folder)
            .Where(path => IsAllowedMediaExtension(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new
            {
                fileName = file.Name,
                mediaType = AllowedVideoExtensions.Contains(file.Extension) ? "video" : "image",
                isVideo = AllowedVideoExtensions.Contains(file.Extension),
                isPrimary = primaryPath is not null && string.Equals(file.FullName, primaryPath, StringComparison.OrdinalIgnoreCase),
                updatedAt = file.LastWriteTimeUtc
            });
        return Ok(files);
    }

    [HttpGet("files/{fileName}")]
    public async Task<IActionResult> FilePreview(int productId, string fileName, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new { item.Id, item.LocalInventoryId })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null) return NotFound();

        var path = ResolveProductImagePath(product.Id, product.LocalInventoryId, fileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, ContentTypeFor(path), enableRangeProcessing: AllowedVideoExtensions.Contains(Path.GetExtension(path)));
    }

    [HttpDelete("files/{fileName}")]
    public async Task<IActionResult> DeleteFile(int productId, string fileName, CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken)) return Forbid();

        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();

        var path = ResolveProductImagePath(product.Id, product.LocalInventoryId, fileName);
        if (!System.IO.File.Exists(path) || !IsAllowedMediaExtension(Path.GetExtension(path)))
            return NotFound("The selected product media file does not exist.");

        var primaryCleared = !string.IsNullOrWhiteSpace(product.ImagePath)
            && string.Equals(Path.GetFullPath(product.ImagePath), path, StringComparison.OrdinalIgnoreCase);
        System.IO.File.Delete(path);
        if (primaryCleared)
        {
            product.ImagePath = null;
            product.ImageSource = "local";
        }
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { deleted = fileName, primaryCleared });
    }

    [HttpGet("files/download-all")]
    public async Task<IActionResult> DownloadAll(int productId, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new { item.Id, item.LocalInventoryId, item.Sku })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null) return NotFound();

        var folder = productImageFolders.GetProductFolder(product.Id, product.LocalInventoryId);
        var imagePaths = Directory.EnumerateFiles(folder)
            .Where(path => AllowedUploadExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0) return NotFound("This product does not have any downloadable images.");

        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var imagePath in imagePaths)
            {
                var entry = archive.CreateEntry(Path.GetFileName(imagePath), CompressionLevel.Fastest);
                await using var source = System.IO.File.OpenRead(imagePath);
                await using var destination = entry.Open();
                await source.CopyToAsync(destination, cancellationToken);
            }
        }

        var archiveName = SafeDownloadName(product.LocalInventoryId ?? product.Sku, product.Id);
        return File(output.ToArray(), "application/zip", $"{archiveName}-images.zip");
    }

    [HttpGet("files/download-all-media")]
    public async Task<IActionResult> DownloadAllMedia(int productId, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new { item.Id, item.LocalInventoryId, item.Sku })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null) return NotFound();

        var folder = productImageFolders.GetProductFolder(product.Id, product.LocalInventoryId);
        var mediaPaths = Directory.EnumerateFiles(folder)
            .Where(path => IsAllowedMediaExtension(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mediaPaths.Length == 0) return NotFound("This product does not have any downloadable media.");

        var temporaryArchivePath = Path.Combine(Path.GetTempPath(), $"lpm-media-{Guid.NewGuid():N}.zip");
        var output = new FileStream(
            temporaryArchivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var mediaPath in mediaPaths)
                {
                    var entry = archive.CreateEntry(Path.GetFileName(mediaPath), CompressionLevel.Fastest);
                    await using var source = System.IO.File.OpenRead(mediaPath);
                    await using var destination = entry.Open();
                    await source.CopyToAsync(destination, cancellationToken);
                }
            }
            output.Position = 0;
            var archiveName = SafeDownloadName(product.LocalInventoryId ?? product.Sku, product.Id);
            return File(output, "application/zip", $"{archiveName}-media.zip");
        }
        catch
        {
            await output.DisposeAsync();
            throw;
        }
    }

    [HttpPost("primary")]
    public async Task<IActionResult> SetPrimary(
        int productId,
        SetPrimaryProductImageRequest request,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();

        var path = ResolveProductImagePath(product.Id, product.LocalInventoryId, request.FileName);
        if (!System.IO.File.Exists(path) || !AllowedUploadExtensions.Contains(Path.GetExtension(path)))
            return NotFound("The selected product image does not exist.");

        product.ImagePath = path;
        product.ImageSource = "local";
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { imagePath = path });
    }

    [HttpPost("open-folder")]
    public async Task<IActionResult> OpenFolder(int productId, CancellationToken cancellationToken)
    {
        var currentUser = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        if (!currentUser.IsAdmin)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                "Opening a local product folder requires an administrator session.");
        }

        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();
        if (!OperatingSystem.IsWindows())
            return StatusCode(StatusCodes.Status501NotImplemented, "Opening folders is only supported on the Windows host.");

        var folder = productImageFolders.GetProductFolder(product.Id, product.LocalInventoryId);
        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
        return Ok(new { folder });
    }

    [HttpPost("from-amazon")]
    public async Task<ActionResult<AmazonImageLookupResult>> UseAmazonImage(
        int productId,
        AmazonImageLookupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await UseListingImageCore(
            productId,
            request.ListingUrl,
            "amazon",
            "Add an Amazon listing URL before fetching the image.",
            cancellationToken);

        return result.Result is not null
            ? result.Result
            : Ok(new AmazonImageLookupResult(result.Value!.ImageUrl));
    }

    [HttpPost("from-listing")]
    public async Task<ActionResult<ListingImageLookupResult>> UseListingImage(
        int productId,
        ListingImageLookupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await UseListingImageCore(
            productId,
            request.ListingUrl,
            request.Platform,
            "Add a listing URL before fetching the image.",
            cancellationToken);

        return result.Result is not null
            ? result.Result
            : Ok(result.Value);
    }

    private async Task<ActionResult<ListingImageLookupResult>> UseListingImageCore(
        int productId,
        string? requestedListingUrl,
        string? requestedPlatform,
        string missingUrlMessage,
        CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Include(item => item.Listings)
            .FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var platform = requestedPlatform?.Trim().ToLowerInvariant();
        var listingUrl = requestedListingUrl?.Trim();
        if (string.IsNullOrWhiteSpace(listingUrl))
        {
            listingUrl = !string.IsNullOrWhiteSpace(platform)
                ? product.Listings.FirstOrDefault(listing => listing.Platform == platform)?.ListingUrl
                : product.Listings.FirstOrDefault(listing => !string.IsNullOrWhiteSpace(listing.ListingUrl))?.ListingUrl;
        }

        if (string.IsNullOrWhiteSpace(listingUrl))
        {
            return BadRequest(missingUrlMessage);
        }

        try
        {
            var imageUrl = await amazonImages.FindImageUrlAsync(listingUrl, platform, cancellationToken);
            product.ImagePath = imageUrl;
            product.ImageSource = "remote";
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return new ListingImageLookupResult(imageUrl);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private string ResolveProductImagePath(int productId, string? localInventoryId, string fileName)
    {
        var folder = Path.GetFullPath(productImageFolders.GetProductFolder(productId, localInventoryId));
        var path = Path.GetFullPath(Path.Combine(folder, Path.GetFileName(fileName)));
        if (!string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid product image file name.");
        return path;
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        _ => "image/jpeg"
    };

    private static bool IsAllowedMediaExtension(string extension) =>
        AllowedUploadExtensions.Contains(extension) || AllowedVideoExtensions.Contains(extension);

    private static (string Extension, bool IsVideo)? ResolveUploadMedia(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (AllowedUploadExtensions.Contains(extension) &&
            file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return (extension.ToLowerInvariant(), false);
        if (AllowedVideoExtensions.Contains(extension) &&
            file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return (extension.ToLowerInvariant(), true);
        if (UploadExtensionsByContentType.TryGetValue(file.ContentType, out var imageExtension))
            return (imageExtension, false);
        if (VideoExtensionsByContentType.TryGetValue(file.ContentType, out var videoExtension))
            return (videoExtension, true);
        return null;
    }

    private static string SafeDownloadName(string value, int productId)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? $"product-{productId}" : safe;
    }
}

public sealed record SetPrimaryProductImageRequest(string FileName);
