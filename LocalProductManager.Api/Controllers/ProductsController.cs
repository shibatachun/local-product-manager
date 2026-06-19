using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(
    ProductService products,
    ProductExportService productExport) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductListItemDto>>> Search(
        [FromQuery] string? keyword,
        [FromQuery] string? platform,
        [FromQuery] bool? hasAmazon,
        CancellationToken cancellationToken)
    {
        var result = await products.SearchAsync(keyword, platform, hasAmazon, cancellationToken);
        return Ok(result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var content = await productExport.ExportProductsAsync(cancellationToken);
        var fileName = $"local-products-{DateTimeOffset.Now:yyyyMMdd-HHmm}.xlsx";
        return File(
            content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpGet("{productId:int}")]
    public async Task<ActionResult<ProductDetailDto>> Get(int productId, CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(productId, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("duplicate-check")]
    public async Task<ActionResult<ProductDuplicateCheckDto>> DuplicateCheck(
        [FromQuery] string sku,
        [FromQuery] int? excludeProductId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return BadRequest("SKU is required.");
        }

        return Ok(await products.CheckDuplicateAsync(sku, excludeProductId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ProductDetailDto>> Create(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.ProductName))
        {
            return BadRequest("SKU and product name are required.");
        }

        try
        {
            var product = await products.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { productId = product.Id }, product);
        }
        catch (DuplicateSkuException ex)
        {
            return Conflict(new
            {
                message = "A product with this SKU already exists.",
                sku = ex.Sku,
                existingProductId = ex.ExistingProductId
            });
        }
        catch (DbUpdateException)
        {
            return Conflict("A product with this SKU already exists.");
        }
    }

    [HttpPut("{productId:int}")]
    public async Task<ActionResult<ProductDetailDto>> Update(
        int productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.ProductName))
        {
            return BadRequest("SKU and product name are required.");
        }

        try
        {
            var product = await products.UpdateAsync(productId, request, cancellationToken);
            return product is null ? NotFound() : Ok(product);
        }
        catch (DuplicateSkuException ex)
        {
            return Conflict(new
            {
                message = "A product with this SKU already exists.",
                sku = ex.Sku,
                existingProductId = ex.ExistingProductId
            });
        }
        catch (DbUpdateException)
        {
            return Conflict("A product with this SKU already exists.");
        }
    }

    [HttpDelete("{productId:int}")]
    public async Task<IActionResult> Delete(int productId, CancellationToken cancellationToken)
    {
        var deleted = await products.DeleteAsync(productId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
