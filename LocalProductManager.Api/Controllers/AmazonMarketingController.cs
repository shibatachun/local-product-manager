using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/amazon-marketing/products")]
public sealed class AmazonMarketingController(AppDbContext db, AuthService auth) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductListItemDto>>> GetAll(CancellationToken cancellationToken)
    {
        var products = await db.AmazonMarketingProducts.AsNoTracking()
            .Include(item => item.Asins)
            .Include(item => item.ChildProducts).ThenInclude(item => item.Asins)
            .Where(item => item.ParentProductId == null)
            .OrderBy(item => item.Sku)
            .ToListAsync(cancellationToken);
        return Ok(products.Select(ToListItem).ToArray());
    }

    [HttpGet("{productId:int}")]
    public async Task<ActionResult<ProductDetailDto>> Get(int productId, CancellationToken cancellationToken)
    {
        var product = await db.AmazonMarketingProducts.AsNoTracking()
            .Include(item => item.Asins)
            .Include(item => item.AttributionLinks)
            .Include(item => item.ChildProducts).ThenInclude(item => item.Asins)
            .FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        return product is null ? NotFound() : Ok(ToDetail(product));
    }

    [HttpPut("{productId:int}/attributions/{platform}")]
    public async Task<ActionResult<AmazonAttributionLinkDto>> Upsert(
        int productId,
        string platform,
        UpsertAmazonAttributionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanEditAttributionAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Only admin can edit attribution links.");
        var normalizedPlatform = AllowedValues.Normalize(platform);
        if (!AllowedValues.AttributionPlatforms.Contains(normalizedPlatform))
            return BadRequest("Unsupported attribution platform.");
        if (string.IsNullOrWhiteSpace(request.AttributionUrl))
            return BadRequest("Attribution URL is required.");
        if (!await db.AmazonMarketingProducts.AnyAsync(item => item.Id == productId, cancellationToken))
            return NotFound();

        var link = await db.AmazonMarketingAttributionLinks.FirstOrDefaultAsync(
            item => item.ProductId == productId &&
                item.AmazonAsinId == request.AmazonAsinId &&
                item.Platform == normalizedPlatform,
            cancellationToken);
        if (link is null)
        {
            link = new AmazonMarketingAttributionLink
            {
                ProductId = productId,
                AmazonAsinId = request.AmazonAsinId,
                Platform = normalizedPlatform,
                AttributionUrl = request.AttributionUrl.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.AmazonMarketingAttributionLinks.Add(link);
        }
        else
        {
            link.AttributionUrl = request.AttributionUrl.Trim();
            link.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToLink(link));
    }

    private static ProductListItemDto ToListItem(AmazonMarketingProduct product) => new(
        product.Id, null, product.Sku, product.ProductName, null, "UNKNOWN", null, null, null, null, 0,
        null, null, null, null, product.ImageUrl, "remote", ["amazon"],
        product.Asins.Select(item => item.Asin).Order().ToArray(), product.Asins.Count,
        product.ProductType, product.SkuBase, product.AmazonParentAsin, product.ParentProductId,
        product.ChildProducts.Select(ToListItem).OrderBy(item => item.Sku).ToArray(),
        product.CreatedAt, product.UpdatedAt);

    private static ProductDetailDto ToDetail(AmazonMarketingProduct product) => new(
        product.Id, null, product.Sku, product.ProductName, null, "UNKNOWN", null, null, null, null, 0,
        null, null, null, null, product.ImageUrl, "remote", [],
        product.Asins.Select(ToAsin).OrderBy(item => item.Asin).ToArray(),
        product.AttributionLinks.Select(ToLink).OrderBy(item => item.Platform).ToArray(),
        product.ProductType, product.SkuBase, product.AmazonParentAsin, product.ParentProductId,
        product.ChildProducts.Select(ToListItem).OrderBy(item => item.Sku).ToArray(),
        product.CreatedAt, product.UpdatedAt);

    private static AmazonAsinDto ToAsin(AmazonMarketingAsin item) => new(
        item.Id, item.ProductId, item.Asin, item.AsinType, item.ParentAsin,
        item.AmazonAttributeDone, DateTimeOffset.MinValue, DateTimeOffset.MinValue);

    private static AmazonAttributionLinkDto ToLink(AmazonMarketingAttributionLink item) => new(
        item.Id, item.ProductId, item.AmazonAsinId, item.Platform, item.AttributionUrl,
        item.ImportedFileName, item.CreatedAt, item.UpdatedAt);
}
