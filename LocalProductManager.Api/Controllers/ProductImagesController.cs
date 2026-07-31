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
    ImageThumbnailService thumbnails,
    RemoteImageProxyService remoteImages,
    AmazonImageLookupService amazonImages,
    IWebHostEnvironment environment) : ControllerBase
{
    private static readonly HashSet<string> AllowedUploadExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };
    private static readonly Dictionary<string, string> UploadExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
            ["image/bmp"] = ".bmp"
        };

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
    [RequestSizeLimit(15 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        int productId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();
        if (file.Length is <= 0 or > 15 * 1024 * 1024)
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

        var uploadRoot = Path.Combine(environment.ContentRootPath, "Data", "product-images");
        Directory.CreateDirectory(uploadRoot);
        imageFiles.AddRoot(uploadRoot);
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
}
