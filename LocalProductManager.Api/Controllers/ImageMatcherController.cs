using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/images")]
public sealed class ImageMatcherController(
    ImageMatcherService imageMatcher,
    ImageFileService imageFiles) : ControllerBase
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
}

public sealed record AddImageStorageRootRequest(string Path);
