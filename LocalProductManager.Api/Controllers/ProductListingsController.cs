using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/products/{productId:int}/listings")]
public sealed class ProductListingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductListingDto>>> GetAll(
        int productId,
        CancellationToken cancellationToken)
    {
        if (!await ProductExists(productId, cancellationToken))
        {
            return NotFound();
        }

        var listings = await db.ProductListings
            .AsNoTracking()
            .Where(listing => listing.ProductId == productId)
            .OrderBy(listing => listing.Platform)
            .ToListAsync(cancellationToken);

        return Ok(listings.Select(ProductMapper.ToDto).ToArray());
    }

    [HttpPost]
    public async Task<ActionResult<ProductListingDto>> CreateOrReplace(
        int productId,
        UpsertProductListingRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedPlatform = AllowedValues.Normalize(request.Platform);
        if (!AllowedValues.ListingPlatforms.Contains(normalizedPlatform))
        {
            return BadRequest($"Unsupported platform: {request.Platform}");
        }

        if (!await ProductExists(productId, cancellationToken))
        {
            return NotFound();
        }

        var listing = await db.ProductListings.FirstOrDefaultAsync(
            existing => existing.ProductId == productId && existing.Platform == normalizedPlatform,
            cancellationToken);

        if (listing is null)
        {
            listing = new ProductListing
            {
                ProductId = productId,
                Platform = normalizedPlatform,
                ListingUrl = NormalizeOptional(request.ListingUrl)
            };
            db.ProductListings.Add(listing);
        }
        else
        {
            listing.ListingUrl = NormalizeOptional(request.ListingUrl);
            listing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ProductMapper.ToDto(listing));
    }

    [HttpPut("{listingId:int}")]
    public async Task<ActionResult<ProductListingDto>> Update(
        int productId,
        int listingId,
        UpsertProductListingRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedPlatform = AllowedValues.Normalize(request.Platform);
        if (!AllowedValues.ListingPlatforms.Contains(normalizedPlatform))
        {
            return BadRequest($"Unsupported platform: {request.Platform}");
        }

        var listing = await db.ProductListings.FirstOrDefaultAsync(
            existing => existing.Id == listingId && existing.ProductId == productId,
            cancellationToken);

        if (listing is null)
        {
            return NotFound();
        }

        listing.Platform = normalizedPlatform;
        listing.ListingUrl = NormalizeOptional(request.ListingUrl);
        listing.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ProductMapper.ToDto(listing));
    }

    [HttpDelete("{listingId:int}")]
    public async Task<IActionResult> Delete(int productId, int listingId, CancellationToken cancellationToken)
    {
        var rows = await db.ProductListings
            .Where(listing => listing.Id == listingId && listing.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

        return rows > 0 ? NoContent() : NotFound();
    }

    private Task<bool> ProductExists(int productId, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(product => product.Id == productId, cancellationToken);

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
