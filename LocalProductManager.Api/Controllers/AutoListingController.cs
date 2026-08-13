using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/auto-listing")]
public sealed class AutoListingController(AppDbContext db, IConfiguration configuration) : ControllerBase
{
    [HttpGet("platforms")]
    public ActionResult<IReadOnlyList<AutoListingPlatformDto>> Platforms()
    {
        return Ok(new[]
        {
            EbayStatusValue(), WalmartStatusValue(), EtsyStatusValue()
        });
    }

    [HttpGet("ebay/status")]
    public ActionResult<AutoListingPlatformDto> EbayStatus() => Ok(EbayStatusValue());

    [HttpGet("walmart/status")]
    public ActionResult<AutoListingPlatformDto> WalmartStatus() => Ok(WalmartStatusValue());

    [HttpGet("etsy/status")]
    public ActionResult<AutoListingPlatformDto> EtsyStatus() => Ok(EtsyStatusValue());

    [HttpPost("ebay/drafts/{productId:int}")]
    public async Task<ActionResult<EbayListingDraftDto>> EbayDraft(int productId, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return NotFound();

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(product.ImagePath)) missing.Add("product image");
        if (product.SalePrice is null or <= 0) missing.Add("calculated sale price");
        if (product.Qty <= 0) missing.Add("available inventory");
        if (string.IsNullOrWhiteSpace(product.ProductName)) missing.Add("product title");

        var title = product.ProductName.Trim();
        if (title.Length > 80) title = title[..80].TrimEnd();
        var description = $"{product.ProductName}\n\nSKU: {product.Sku}";
        if (!string.IsNullOrWhiteSpace(product.Category)) description += $"\nCategory: {product.Category}";
        if (product.Weight is not null) description += $"\nWeight: {product.Weight:0.##} oz";

        return Ok(new EbayListingDraftDto(
            product.Id, product.Sku, title, description, product.SalePrice, Math.Max(0, product.Qty),
            "NEW", "EBAY_US", product.ImagePath, missing));
    }

    private AutoListingPlatformDto EbayStatusValue()
    {
        var configured = !string.IsNullOrWhiteSpace(configuration["Ebay:ClientId"]) &&
                         !string.IsNullOrWhiteSpace(configuration["Ebay:ClientSecret"]) &&
                         !string.IsNullOrWhiteSpace(configuration["Ebay:RedirectUriName"]);
        return new AutoListingPlatformDto("ebay", "eBay", configured ? "setup" : "configuration_required", true,
            configured ? "eBay credentials found. OAuth and publishing are the next setup step." : "Add eBay developer credentials to enable seller authorization.");
    }

    private static AutoListingPlatformDto WalmartStatusValue() =>
        new("walmart", "Walmart", "coming_soon", false, "Connector reserved for a future release.");

    private static AutoListingPlatformDto EtsyStatusValue() =>
        new("etsy", "Etsy", "coming_soon", false, "Connector reserved for a future release.");
}
