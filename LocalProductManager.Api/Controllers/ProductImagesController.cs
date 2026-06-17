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
    RemoteImageProxyService remoteImages,
    AmazonImageLookupService amazonImages) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        int productId,
        [FromQuery] bool download,
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
