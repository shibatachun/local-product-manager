using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/products/{productId:int}/amazon-asins")]
public sealed class AmazonAsinsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AmazonAsinDto>>> GetAll(
        int productId,
        CancellationToken cancellationToken)
    {
        if (!await ProductExists(productId, cancellationToken))
        {
            return NotFound();
        }

        var asins = await db.AmazonAsins
            .AsNoTracking()
            .Where(asin => asin.ProductId == productId)
            .OrderBy(asin => asin.AsinType)
            .ThenBy(asin => asin.Asin)
            .ToListAsync(cancellationToken);

        return Ok(asins.Select(ProductMapper.ToDto).ToArray());
    }

    [HttpPost]
    public async Task<ActionResult<AmazonAsinDto>> Create(
        int productId,
        CreateAmazonAsinRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request.Asin, request.AsinType);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        if (!await ProductExists(productId, cancellationToken))
        {
            return NotFound();
        }

        var asin = new AmazonAsin
        {
            ProductId = productId,
            Asin = request.Asin.Trim().ToUpperInvariant(),
            AsinType = AllowedValues.Normalize(request.AsinType),
            ParentAsin = NormalizeAsin(request.ParentAsin),
            AmazonAttributeDone = request.AmazonAttributeDone
        };

        db.AmazonAsins.Add(asin);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("This ASIN already exists for the product.");
        }

        return CreatedAtAction(nameof(GetAll), new { productId }, ProductMapper.ToDto(asin));
    }

    [HttpPut("{asinId:int}")]
    public async Task<ActionResult<AmazonAsinDto>> Update(
        int productId,
        int asinId,
        UpdateAmazonAsinRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request.Asin, request.AsinType);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var asin = await db.AmazonAsins.FirstOrDefaultAsync(
            existing => existing.Id == asinId && existing.ProductId == productId,
            cancellationToken);

        if (asin is null)
        {
            return NotFound();
        }

        asin.Asin = request.Asin.Trim().ToUpperInvariant();
        asin.AsinType = AllowedValues.Normalize(request.AsinType);
        asin.ParentAsin = NormalizeAsin(request.ParentAsin);
        asin.AmazonAttributeDone = request.AmazonAttributeDone;
        asin.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("This ASIN already exists for the product.");
        }
        return Ok(ProductMapper.ToDto(asin));
    }

    [HttpDelete("{asinId:int}")]
    public async Task<IActionResult> Delete(int productId, int asinId, CancellationToken cancellationToken)
    {
        var rows = await db.AmazonAsins
            .Where(asin => asin.Id == asinId && asin.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

        return rows > 0 ? NoContent() : NotFound();
    }

    private Task<bool> ProductExists(int productId, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(product => product.Id == productId, cancellationToken);

    private static string? Validate(string asin, string asinType)
    {
        if (string.IsNullOrWhiteSpace(asin))
        {
            return "ASIN is required.";
        }

        if (!AllowedValues.AmazonAsinTypes.Contains(AllowedValues.Normalize(asinType)))
        {
            return $"Unsupported ASIN type: {asinType}";
        }

        return null;
    }

    private static string? NormalizeAsin(string? asin)
    {
        var trimmed = asin?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToUpperInvariant();
    }
}
