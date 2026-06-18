using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/products/{productId:int}/amazon-attributions")]
public sealed class AmazonAttributionsController(
    AppDbContext db,
    AmazonAttributionImportService attributionService,
    AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AmazonAttributionLinkDto>>> GetAll(
        int productId,
        [FromQuery] int? amazonAsinId,
        CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(product => product.Id == productId, cancellationToken))
        {
            return NotFound();
        }

        var query = db.AmazonAttributionLinks
            .AsNoTracking()
            .Where(link => link.ProductId == productId);

        if (amazonAsinId.HasValue)
        {
            query = query.Where(link => link.AmazonAsinId == amazonAsinId.Value);
        }

        var links = await query
            .OrderBy(link => link.Platform)
            .ToListAsync(cancellationToken);

        return Ok(links.Select(ProductMapper.ToDto).ToArray());
    }

    [HttpPut("{platform}")]
    public async Task<ActionResult<AmazonAttributionLinkDto>> Upsert(
        int productId,
        string platform,
        UpsertAmazonAttributionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanEditAttributionAsync(HttpContext, cancellationToken)) return AdminOnly();
        if (string.IsNullOrWhiteSpace(request.AttributionUrl))
        {
            return BadRequest("Attribution URL is required.");
        }

        try
        {
            var link = await attributionService.UpsertAsync(productId, platform, request, cancellationToken);
            return link is null ? NotFound() : Ok(link);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("import")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ImportAmazonAttributionResult>> Import(
        int productId,
        IFormFile file,
        [FromForm] int? amazonAsinId,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken)) return AdminOnly();
        if (file.Length == 0)
        {
            return BadRequest("Import file is empty.");
        }

        try
        {
            var result = await attributionService.ImportCsvAsync(productId, file, amazonAsinId, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private ObjectResult AdminOnly() =>
        StatusCode(StatusCodes.Status403Forbidden, "Only admin can edit or import attribution links.");
}
