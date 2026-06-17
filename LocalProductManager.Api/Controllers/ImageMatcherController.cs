using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/images")]
public sealed class ImageMatcherController(
    ImageMatcherService imageMatcher,
    ImageFileService imageFiles,
    AmazonImageLookupService listingImages) : ControllerBase
{
    [HttpGet("roots")]
    public IActionResult GetRoots() => Ok(imageFiles.GetRoots());

    [HttpPost("roots")]
    public ActionResult<ImageStorageRoot> AddRoot(AddImageStorageRootRequest request)
    {
        try
        {
            return Ok(imageFiles.AddRoot(request.Path));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("roots")]
    public IActionResult RemoveRoot([FromQuery] string path)
    {
        try
        {
            return imageFiles.RemoveRoot(path) ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("match")]
    public ActionResult<MatchImagesResponse> Match(MatchImagesRequest request)
    {
        try
        {
            if (!imageFiles.TryGetAllowedDirectory(request.FolderPath, out var fullFolderPath))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        message = "The image folder is outside the configured image storage roots.",
                        allowedRoots = imageFiles.GetRoots().Select(root => root.Path)
                    });
            }

            return Ok(imageMatcher.Match(request with { FolderPath = fullFolderPath }));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("from-listing")]
    public async Task<ActionResult<ListingImageLookupResult>> FromListing(
        ListingImageLookupRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ListingUrl))
        {
            return BadRequest("Enter a listing URL before fetching the image.");
        }

        try
        {
            var imageUrl = await listingImages.FindImageUrlAsync(request.ListingUrl, cancellationToken);
            return Ok(new ListingImageLookupResult(imageUrl));
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

    [HttpGet("preview")]
    public IActionResult Preview([FromQuery] string path)
    {
        try
        {
            var image = imageFiles.GetImage(path);
            var stream = new FileStream(
                image.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);

            Response.Headers.CacheControl = "private, max-age=300";
            return File(stream, image.ContentType, enableRangeProcessing: true);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (FileNotFoundException)
        {
            return NotFound("The image file was not found by the server.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (IOException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "The image share is temporarily unavailable.");
        }
    }
}

public sealed record AddImageStorageRootRequest(string Path);
