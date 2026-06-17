using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/store-links")]
public sealed class StoreLinksController(StoreLinkService storeLinks) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<StoreLinkDto>> GetAll() => Ok(storeLinks.GetAll());

    [HttpPut("{platform}")]
    public ActionResult<StoreLinkDto> Save(string platform, SaveStoreLinkRequest request)
    {
        try
        {
            return Ok(storeLinks.Save(platform, request.Url));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public sealed record SaveStoreLinkRequest(string? Url);
