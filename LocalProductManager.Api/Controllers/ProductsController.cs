using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Services;
using LocalProductManager.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(
    ProductService products,
    ProductExportService productExport,
    AppDbContext db,
    AuthService auth) : ControllerBase
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

    [HttpGet("lookup")]
    public async Task<ActionResult<ProductListItemDto>> Lookup(
        [FromQuery] string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest("Inventory ID or SKU is required.");
        }

        var product = await products.LookupExactAsync(code, cancellationToken);
        return product is null ? NotFound() : Ok(product);
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
        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            return BadRequest("Product name is required.");
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
        var occupiedRows = await db.ShelfLevelInventories
            .AsNoTracking()
            .Where(item => item.ProductId == productId && item.Qty > 0)
            .Include(item => item.ShelfLevel)
            .OrderBy(item => item.ShelfLevel!.Code)
            .ThenBy(item => item.BinNumber)
            .Select(item => new
            {
                levelCode = item.ShelfLevel!.Code,
                item.BinNumber,
                qty = item.Qty
            })
            .ToArrayAsync(cancellationToken);
        if (occupiedRows.Length > 0)
        {
            return Conflict(new
            {
                message = "This product cannot be deleted while it still has stock in a warehouse layer. Clear or move the stock first.",
                locations = occupiedRows.Select(item => new
                {
                    location = $"{item.levelCode}-B{item.BinNumber:00}",
                    item.qty
                })
            });
        }

        var deleted = await products.DeleteAsync(productId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{productId:int}/extended-data")]
    public async Task<ActionResult<ProductExtendedDataDto>> GetExtendedData(int productId, CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(item => item.Id == productId, cancellationToken)) return NotFound();
        var details = await db.ProductDetails.AsNoTracking().SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        var compliance = await db.ProductCompliance.AsNoTracking().SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        var packaging = await db.ProductPackaging.AsNoTracking().SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        var identifiers = await db.ProductIdentifiers.AsNoTracking().Where(item => item.ProductId == productId).OrderByDescending(item => item.IsPrimary).ThenBy(item => item.IdentifierType).ToListAsync(cancellationToken);
        var supplierRows = await db.ProductSuppliers.AsNoTracking().Where(item => item.ProductId == productId).Include(item => item.Supplier).Include(item => item.ProcurementLinks).ToListAsync(cancellationToken);
        var prices = await db.ProductPrices.AsNoTracking().Where(item => item.ProductId == productId).OrderBy(item => item.PriceType).ThenBy(item => item.Currency).ToListAsync(cancellationToken);
        return Ok(new ProductExtendedDataDto
        {
            Details = details is null ? new() : new() { ExternalProductCode = details.ExternalProductCode, VariantName = details.VariantName, Material = details.Material, Specification = details.Specification, Unit = details.Unit, Style = details.Style, Description = details.Description, DevelopmentDate = details.DevelopmentDate },
            Identifiers = identifiers.Select(item => new ProductIdentifierDto { IdentifierType = item.IdentifierType, Value = item.Value, IsPrimary = item.IsPrimary, Notes = item.Notes }).ToList(),
            Suppliers = supplierRows.Select(item => new ProductSupplierDto { Name = item.Supplier.Name, ContactName = item.Supplier.ContactName, ContactDetails = item.Supplier.ContactDetails, ProcurementChannel = item.ProcurementChannel, Buyer = item.Buyer, MinimumPurchasePrice = item.MinimumPurchasePrice, MinimumOrderQuantity = item.MinimumOrderQuantity, Currency = item.Currency, IsPreferred = item.IsPreferred, Provenance = item.Provenance, ProcurementLinks = item.ProcurementLinks.OrderBy(link => link.SortOrder).Select(link => new ProcurementLinkDto { Url = link.Url, SortOrder = link.SortOrder, Source = link.Source }).ToList() }).ToList(),
            Compliance = compliance is null ? new() : new() { DeclarationName = compliance.DeclarationName, CountryOfOrigin = compliance.CountryOfOrigin, HsCode = compliance.HsCode, DeclaredValue = compliance.DeclaredValue, DeclaredValueCurrency = compliance.DeclaredValueCurrency, ContainsBattery = compliance.ContainsBattery, ContainsPowder = compliance.ContainsPowder, ContainsLiquid = compliance.ContainsLiquid, ContainsMagnet = compliance.ContainsMagnet, ShippingMethod = compliance.ShippingMethod, ExpirationDate = compliance.ExpirationDate },
            Packaging = packaging is null ? new() : new() { PackQuantity = packaging.PackQuantity, PackageLength = packaging.PackageLength, PackageWidth = packaging.PackageWidth, PackageHeight = packaging.PackageHeight, DimensionUnit = packaging.DimensionUnit, PackageWeight = packaging.PackageWeight, CartonWeight = packaging.CartonWeight, BillableWeight = packaging.BillableWeight, WeightUnit = packaging.WeightUnit, PackagingCost = packaging.PackagingCost, PackagingCostCurrency = packaging.PackagingCostCurrency, PackagingDifficulty = packaging.PackagingDifficulty, LabelRequirements = packaging.LabelRequirements },
            Prices = prices.Select(item => new ProductPriceDto { PriceType = item.PriceType, Currency = item.Currency, Amount = item.Amount, QuantityThreshold = item.QuantityThreshold, EffectiveFrom = item.EffectiveFrom, EffectiveTo = item.EffectiveTo, Source = item.Source }).ToList()
        });
    }

    [HttpPut("{productId:int}/extended-data")]
    public async Task<ActionResult<ProductExtendedDataDto>> SaveExtendedData(int productId, ProductExtendedDataDto request, CancellationToken cancellationToken)
    {
        if (!await auth.CanEditProductDetailsAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Editing detailed product information requires administrator approval.");
        if (!await db.Products.AnyAsync(item => item.Id == productId, cancellationToken)) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var details = await db.ProductDetails.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken) ?? new ProductDetailProfile { ProductId = productId };
        if (db.Entry(details).State == EntityState.Detached) db.ProductDetails.Add(details);
        details.ExternalProductCode = request.Details.ExternalProductCode; details.VariantName = request.Details.VariantName; details.Material = request.Details.Material; details.Specification = request.Details.Specification; details.Unit = request.Details.Unit; details.Style = request.Details.Style; details.Description = request.Details.Description; details.DevelopmentDate = request.Details.DevelopmentDate;

        var compliance = await db.ProductCompliance.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken) ?? new ProductCompliance { ProductId = productId };
        if (db.Entry(compliance).State == EntityState.Detached) db.ProductCompliance.Add(compliance);
        compliance.DeclarationName = request.Compliance.DeclarationName; compliance.CountryOfOrigin = request.Compliance.CountryOfOrigin; compliance.HsCode = request.Compliance.HsCode; compliance.DeclaredValue = request.Compliance.DeclaredValue; compliance.DeclaredValueCurrency = request.Compliance.DeclaredValueCurrency; compliance.ContainsBattery = request.Compliance.ContainsBattery; compliance.ContainsPowder = request.Compliance.ContainsPowder; compliance.ContainsLiquid = request.Compliance.ContainsLiquid; compliance.ContainsMagnet = request.Compliance.ContainsMagnet; compliance.ShippingMethod = request.Compliance.ShippingMethod; compliance.ExpirationDate = request.Compliance.ExpirationDate;

        var packaging = await db.ProductPackaging.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken) ?? new ProductPackaging { ProductId = productId };
        if (db.Entry(packaging).State == EntityState.Detached) db.ProductPackaging.Add(packaging);
        packaging.PackQuantity = request.Packaging.PackQuantity; packaging.PackageLength = request.Packaging.PackageLength; packaging.PackageWidth = request.Packaging.PackageWidth; packaging.PackageHeight = request.Packaging.PackageHeight; packaging.DimensionUnit = request.Packaging.DimensionUnit; packaging.PackageWeight = request.Packaging.PackageWeight; packaging.CartonWeight = request.Packaging.CartonWeight; packaging.BillableWeight = request.Packaging.BillableWeight; packaging.WeightUnit = request.Packaging.WeightUnit; packaging.PackagingCost = request.Packaging.PackagingCost; packaging.PackagingCostCurrency = request.Packaging.PackagingCostCurrency; packaging.PackagingDifficulty = request.Packaging.PackagingDifficulty; packaging.LabelRequirements = request.Packaging.LabelRequirements;

        db.ProductIdentifiers.RemoveRange(await db.ProductIdentifiers.Where(item => item.ProductId == productId).ToListAsync(cancellationToken));
        db.ProductPrices.RemoveRange(await db.ProductPrices.Where(item => item.ProductId == productId).ToListAsync(cancellationToken));
        db.ProductSuppliers.RemoveRange(await db.ProductSuppliers.Where(item => item.ProductId == productId).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        db.ProductIdentifiers.AddRange(request.Identifiers.Where(item => !string.IsNullOrWhiteSpace(item.Value)).Select(item => new ProductIdentifier { ProductId = productId, IdentifierType = item.IdentifierType.Trim(), Value = item.Value.Trim(), IsPrimary = item.IsPrimary, Notes = item.Notes }));
        db.ProductPrices.AddRange(request.Prices.Select(item => new ProductPrice { ProductId = productId, PriceType = item.PriceType, Currency = item.Currency, Amount = item.Amount, QuantityThreshold = item.QuantityThreshold, EffectiveFrom = item.EffectiveFrom, EffectiveTo = item.EffectiveTo, Source = item.Source }));
        foreach (var item in request.Suppliers.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            var name = item.Name.Trim();
            var supplier = await db.Suppliers.SingleOrDefaultAsync(row => row.Name == name, cancellationToken) ?? new Supplier { Name = name };
            if (supplier.Id == 0) db.Suppliers.Add(supplier);
            supplier.ContactName = item.ContactName; supplier.ContactDetails = item.ContactDetails; supplier.UpdatedAt = DateTimeOffset.UtcNow;
            var relation = new ProductSupplier { ProductId = productId, Supplier = supplier, ProcurementChannel = item.ProcurementChannel, Buyer = item.Buyer, MinimumPurchasePrice = item.MinimumPurchasePrice, MinimumOrderQuantity = item.MinimumOrderQuantity, Currency = item.Currency, IsPreferred = item.IsPreferred, Provenance = item.Provenance, UpdatedAt = DateTimeOffset.UtcNow };
            relation.ProcurementLinks = item.ProcurementLinks.Where(link => !string.IsNullOrWhiteSpace(link.Url)).Select(link => new ProcurementLink { Url = link.Url.Trim(), SortOrder = link.SortOrder, Source = link.Source }).ToList();
            db.ProductSuppliers.Add(relation);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetExtendedData(productId, cancellationToken);
    }
}
