using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/live-stream-bid-products")]
public sealed class LiveStreamBidProductsController(AppDbContext db, LiveStreamBidProductImportService importer, AuthService auth, ExchangeRateService exchangeRates, InventoryWriteService inventoryWrites) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LiveStreamBidProductDto>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok((await db.LiveStreamBidProducts.AsNoTracking().OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken)).Select(ToDto));
    }

    [HttpGet("lookup")]
    public async Task<ActionResult<LiveStreamBidProductDto>> Lookup([FromQuery] string uuid, CancellationToken cancellationToken)
    {
        var normalized = NormalizeBidCode(uuid);
        if (string.IsNullOrWhiteSpace(normalized)) return BadRequest("BID UUID is required.");
        var normalizedUpper = normalized.ToUpper();
        var prefixedUpper = $"BID|{normalized}".ToUpper();
        var item = await db.LiveStreamBidProducts.AsNoTracking().FirstOrDefaultAsync(
            product => product.Uuid.ToUpper() == normalizedUpper ||
                       product.Uuid.Replace(" ", "").ToUpper() == prefixedUpper,
            cancellationToken);
        if (item is null)
        {
            var localCode = NormalizeLocalCode(uuid);
            int? sourceProductId = null;
            if (localCode.StartsWith("INV-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(localCode[4..], out var generatedId))
                sourceProductId = generatedId;
            sourceProductId ??= await db.Products.AsNoTracking()
                .Where(product => product.LocalInventoryId != null && product.LocalInventoryId.ToUpper() == localCode.ToUpper())
                .Select(product => (int?)product.Id).FirstOrDefaultAsync(cancellationToken);
            if (sourceProductId.HasValue)
                item = await db.LiveStreamBidProducts.AsNoTracking()
                    .Where(product => product.SourceProductId == sourceProductId.Value)
                    .OrderByDescending(product => product.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
        }
        return item is null ? NotFound() : Ok(ToDto(item));
    }

    [HttpPost("{id:int}/confirm-sale")]
    public async Task<ActionResult<LiveStreamBidSaleDto>> ConfirmSale(int id, ConfirmLiveStreamBidSaleRequest request, CancellationToken cancellationToken)
    {
        if (request.ActualSalePrice < 0) return BadRequest("Actual sale price cannot be negative.");
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        if (product.Inventory <= 0) return Conflict("This bid product has no expected inventory remaining.");
        if (product.ActualInventory <= 0) return Conflict("This bid product has not been physically received or has no actual inventory remaining.");
        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var effectiveCost = EffectiveCost(product);
        decimal? rate = null;
        DateOnly? rateDate = null;
        decimal? salePriceRmb = null;
        decimal? profitRmb = null;
        decimal? profitUsd = null;
        if (effectiveCost.HasValue || request.ActualSalePrice.HasValue)
        {
            try
            {
                var exchange = await exchangeRates.GetRmbToUsdAsync(cancellationToken);
                rate = exchange.Rate;
                rateDate = exchange.Date;
                if (request.ActualSalePrice.HasValue)
                {
                    salePriceRmb = decimal.Round(request.ActualSalePrice.Value / rate.Value, 2);
                    if (effectiveCost.HasValue)
                    {
                        profitRmb = salePriceRmb.Value - effectiveCost.Value;
                        profitUsd = request.ActualSalePrice.Value - decimal.Round(effectiveCost.Value * rate.Value, 2);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "The live exchange rate is unavailable. Please retry before confirming this sale.");
            }
        }
        var sale = new LiveStreamBidSale
        {
            BidProductId = product.Id,
            ProductUuid = product.Uuid,
            ProductTitle = product.Title,
            Quantity = product.Quantity,
            Cost = effectiveCost,
            ActualSalePrice = request.ActualSalePrice,
            SalePriceRmb = salePriceRmb,
            ExchangeRateRmbToUsd = rate,
            ExchangeRateDate = rateDate,
            Profit = profitRmb,
            ProfitUsd = profitUsd,
            SaleDate = DateOnly.FromDateTime(DateTime.Now),
            SoldAt = now,
            SoldBy = user.UserName
        };
        product.Inventory -= 1;
        product.ActualInventory -= 1;
        product.IsSold = product.Inventory == 0;
        product.SoldAt = product.IsSold ? now : null;
        product.UpdatedAt = now;
        db.LiveStreamBidSales.Add(sale);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToSaleDto(sale));
    }

    [HttpGet("sales/today")]
    public async Task<ActionResult<LiveStreamBidDailySummaryDto>> Today(CancellationToken cancellationToken)
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        return Ok(await SummaryForDateAsync(date, true, cancellationToken));
    }

    [HttpGet("sales/history")]
    public async Task<ActionResult<IReadOnlyList<LiveStreamBidDailySummaryDto>>> SalesHistory(CancellationToken cancellationToken)
    {
        var sales = await db.LiveStreamBidSales.OrderByDescending(item => item.SoldAt).ToListAsync(cancellationToken);
        await PopulateMissingExchangeRatesAsync(sales, cancellationToken);
        return Ok(sales.GroupBy(item => item.SaleDate).OrderByDescending(group => group.Key)
            .Select(group => BuildSummary(group.Key, group, false)).ToArray());
    }

    [HttpGet("sales/by-date")]
    public async Task<ActionResult<LiveStreamBidDailySummaryDto>> SalesByDate([FromQuery] DateOnly date, CancellationToken cancellationToken) =>
        Ok(await SummaryForDateAsync(date, true, cancellationToken));

    [HttpDelete("sales/{saleId:int}")]
    public async Task<IActionResult> DeleteSale(int saleId, CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Administrator access is required.");
        var sale = await db.LiveStreamBidSales.FirstOrDefaultAsync(item => item.Id == saleId, cancellationToken);
        if (sale is null) return NotFound();
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == sale.BidProductId, cancellationToken);
        db.LiveStreamBidSales.Remove(sale);
        await db.SaveChangesAsync(cancellationToken);
        if (product is not null)
        {
            var latestSale = await db.LiveStreamBidSales.AsNoTracking().Where(item => item.BidProductId == product.Id)
                .OrderByDescending(item => item.SoldAt).Select(item => (DateTimeOffset?)item.SoldAt).FirstOrDefaultAsync(cancellationToken);
            product.IsSold = latestSale.HasValue;
            product.SoldAt = latestSale;
            product.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    [HttpDelete("sales/by-date")]
    public async Task<IActionResult> DeleteSalesByDate([FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        if (!await auth.IsAdminAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Administrator access is required.");
        var sales = await db.LiveStreamBidSales.Where(item => item.SaleDate == date).ToListAsync(cancellationToken);
        if (sales.Count == 0) return NotFound();
        var productIds = sales.Select(item => item.BidProductId).Distinct().ToArray();
        db.LiveStreamBidSales.RemoveRange(sales);
        await db.SaveChangesAsync(cancellationToken);
        var products = await db.LiveStreamBidProducts.Where(item => productIds.Contains(item.Id)).ToListAsync(cancellationToken);
        foreach (var product in products)
        {
            var latestSale = await db.LiveStreamBidSales.AsNoTracking().Where(item => item.BidProductId == product.Id)
                .OrderByDescending(item => item.SoldAt).Select(item => (DateTimeOffset?)item.SoldAt).FirstOrDefaultAsync(cancellationToken);
            product.IsSold = latestSale.HasValue;
            product.SoldAt = latestSale;
            product.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("import")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<LiveStreamBidImportResult>> Import(IFormFile file, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import permission is required.");
        if (file.Length == 0) return BadRequest("The CSV file is empty.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Select a CSV file.");
        try { return Ok(await importer.ImportAsync(file, cancellationToken)); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("batches")]
    public async Task<ActionResult<CreateLiveStreamBidListResult>> CreateBatch(
        CreateLiveStreamBidListRequest request, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken) &&
            !await auth.CanManageStorageAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import or inventory management permission is required.");
        if (request.Rows.Count == 0) return BadRequest("Add at least one product.");
        if (request.Rows.Count > 1000) return BadRequest("A batch can contain at most 1,000 products.");
        if (request.Rows.Any(row => string.IsNullOrWhiteSpace(row.Title) || row.Quantity < 1 || row.ExpectedInventory < 0 || row.ActualInventory < 0 || row.UnitPrice < 0 || row.TotalCost < 0 || row.WhatnotAuctionSafePrice < 0))
            return BadRequest("Every row needs a title and valid non-negative values.");

        var (resolvedUuids, uuidChanges) = await ResolveImportedUuidsAsync(request.Rows, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var existing = await db.LiveStreamBidProducts.AsNoTracking().Where(item => item.BatchNumber != null)
            .Select(item => item.BatchNumber!).Distinct().ToListAsync(cancellationToken);
        var next = existing.Select(ParseBatchNumber).DefaultIfEmpty(0).Max() + 1;
        var batchNumber = $"B{next:000000}";
        var now = DateTimeOffset.UtcNow;
        var rmbToUsdRate = await GetSafePricingExchangeRateAsync(cancellationToken);
        var products = request.Rows.Select((row, index) => new LiveStreamBidProduct
        {
            SourceProductId = row.SourceProductId,
            BatchNumber = batchNumber,
            BatchProductNumber = index + 1,
            ActualSku = string.IsNullOrWhiteSpace(row.ActualSku) ? null : row.ActualSku.Trim(),
            RequiresLocalLabel = string.IsNullOrWhiteSpace(row.ExternalUuid) || uuidChanges.ContainsKey(index),
            WhatnotAuctionSafePrice = WhatnotSafePriceCalculator.Calculate(row.TotalCost, rmbToUsdRate),
            ItemCode = WhatnotSafePriceCalculator.ToItemCode(WhatnotSafePriceCalculator.Calculate(row.TotalCost, rmbToUsdRate)),
            Uuid = string.IsNullOrWhiteSpace(row.ExternalUuid) ? $"{batchNumber}|P{index + 1:000}" : resolvedUuids[index]!,
            Title = row.Title.Trim(),
            Quantity = row.Quantity,
            Inventory = row.ExpectedInventory,
            ActualInventory = row.ActualInventory,
            UnitPrice = row.UnitPrice,
            WeightGrams = row.WeightGrams,
            WeightOunces = row.WeightOunces ?? (row.WeightGrams.HasValue ? decimal.Round(row.WeightGrams.Value / 28.349523125m, 4) : null),
            Material = string.IsNullOrWhiteSpace(row.Material) ? null : row.Material.Trim(),
            TotalCost = row.TotalCost,
            ImportedFileName = string.IsNullOrWhiteSpace(row.ExternalUuid) ? $"Local Manager · {batchNumber}" : $"External CSV · {batchNumber}",
            CreatedAt = now,
            UpdatedAt = now
        }).ToArray();
        db.LiveStreamBidProducts.AddRange(products);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new CreateLiveStreamBidListResult(batchNumber, products.Select(ToDto).ToArray(),
            BuildRegeneratedUuidDtos(products, uuidChanges)));
    }

    [HttpGet("batches/{batchNumber}/bartender.csv")]
    public async Task<IActionResult> ExportBatch(string batchNumber, CancellationToken cancellationToken)
    {
        var normalized = batchNumber.Trim().ToUpperInvariant();
        var rows = await db.LiveStreamBidProducts.AsNoTracking()
            .Where(item => item.BatchNumber == normalized)
            .OrderBy(item => item.BatchProductNumber).ToListAsync(cancellationToken);
        if (rows.Count == 0) return NotFound();
        var csv = new StringBuilder("UUID,Title,Quantity,Inventory,Unit Price,Weight (g),Weight (oz),Material,Total Cost,Whatnot Auction Safe Price,Item Code,Expected Inventory\r\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', new[] {
                Csv($"BID|{NormalizeBidCode(row.Uuid)}"), Csv(row.Title), row.Quantity.ToString(CultureInfo.InvariantCulture),
                row.ActualInventory.ToString(CultureInfo.InvariantCulture), row.UnitPrice.ToString("F2", CultureInfo.InvariantCulture),
                row.WeightGrams?.ToString("F2", CultureInfo.InvariantCulture) ?? "", row.WeightOunces?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
                Csv(row.Material ?? ""), row.TotalCost?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
                row.WhatnotAuctionSafePrice?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
                Csv(row.ItemCode ?? ""),
                row.Inventory.ToString(CultureInfo.InvariantCulture) }));
        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"{normalized}-bartender.csv");
    }

    [HttpPost("batches/{batchNumber}/products")]
    public async Task<ActionResult<CreateLiveStreamBidListResult>> AddBatchProducts(
        string batchNumber, CreateLiveStreamBidListRequest request, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken) &&
            !await auth.CanManageStorageAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import or inventory management permission is required.");
        var normalizedBatch = batchNumber.Trim().ToUpperInvariant();
        if (request.Rows.Count == 0 || request.Rows.Count > 1000) return BadRequest("Add between 1 and 1,000 products.");
        if (request.Rows.Any(row => string.IsNullOrWhiteSpace(row.Title) || row.Quantity < 1 || row.ExpectedInventory < 0 || row.ActualInventory < 0 || row.UnitPrice < 0 || row.TotalCost < 0 || row.WhatnotAuctionSafePrice < 0))
            return BadRequest("Every row needs a title and valid non-negative values.");
        if (!await db.LiveStreamBidProducts.AsNoTracking().AnyAsync(item => item.BatchNumber == normalizedBatch, cancellationToken))
            return NotFound("Batch not found.");
        var (resolvedUuids, uuidChanges) = await ResolveImportedUuidsAsync(request.Rows, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var nextProductNumber = (await db.LiveStreamBidProducts.Where(item => item.BatchNumber == normalizedBatch)
            .MaxAsync(item => item.BatchProductNumber, cancellationToken) ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var rmbToUsdRate = await GetSafePricingExchangeRateAsync(cancellationToken);
        var products = request.Rows.Select((row, index) =>
        {
            var productNumber = nextProductNumber + index;
            return new LiveStreamBidProduct
            {
                SourceProductId = row.SourceProductId, BatchNumber = normalizedBatch, BatchProductNumber = productNumber,
                ActualSku = string.IsNullOrWhiteSpace(row.ActualSku) ? null : row.ActualSku.Trim(),
                RequiresLocalLabel = string.IsNullOrWhiteSpace(row.ExternalUuid) || uuidChanges.ContainsKey(index),
                WhatnotAuctionSafePrice = WhatnotSafePriceCalculator.Calculate(row.TotalCost, rmbToUsdRate),
                ItemCode = WhatnotSafePriceCalculator.ToItemCode(WhatnotSafePriceCalculator.Calculate(row.TotalCost, rmbToUsdRate)),
                Uuid = string.IsNullOrWhiteSpace(row.ExternalUuid) ? $"{normalizedBatch}|P{productNumber:000}" : resolvedUuids[index]!,
                Title = row.Title.Trim(), Quantity = row.Quantity, Inventory = row.ExpectedInventory, UnitPrice = row.UnitPrice,
                ActualInventory = row.ActualInventory,
                WeightGrams = row.WeightGrams,
                WeightOunces = row.WeightOunces ?? (row.WeightGrams.HasValue ? decimal.Round(row.WeightGrams.Value / 28.349523125m, 4) : null),
                Material = string.IsNullOrWhiteSpace(row.Material) ? null : row.Material.Trim(), TotalCost = row.TotalCost,
                ImportedFileName = string.IsNullOrWhiteSpace(row.ExternalUuid) ? $"Local Manager · {normalizedBatch}" : $"External CSV · {normalizedBatch}",
                CreatedAt = now, UpdatedAt = now
            };
        }).ToArray();
        db.LiveStreamBidProducts.AddRange(products);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new CreateLiveStreamBidListResult(normalizedBatch, products.Select(ToDto).ToArray(),
            BuildRegeneratedUuidDtos(products, uuidChanges)));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteProduct(int id, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken) &&
            !await auth.CanManageStorageAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import or inventory management permission is required.");
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        if (await db.LiveStreamBidSales.AsNoTracking().AnyAsync(item => item.BidProductId == id, cancellationToken))
            return Conflict("This BID product has sales history and cannot be deleted.");
        db.LiveStreamBidProducts.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("batches/{batchNumber}")]
    public async Task<IActionResult> DeleteBatch(string batchNumber, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken) &&
            !await auth.CanManageStorageAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import or inventory management permission is required.");
        var normalized = batchNumber.Trim().ToUpperInvariant();
        var products = await db.LiveStreamBidProducts
            .Where(item => item.BatchNumber == normalized)
            .ToListAsync(cancellationToken);
        if (products.Count == 0) return NotFound();
        var productIds = products.Select(item => item.Id).ToArray();
        if (await db.LiveStreamBidSales.AsNoTracking().AnyAsync(item => productIds.Contains(item.BidProductId), cancellationToken))
            return Conflict("This batch has sales history and cannot be deleted.");
        db.LiveStreamBidProducts.RemoveRange(products);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<LiveStreamBidProductDto>> UpdateProduct(
        int id, UpdateLiveStreamBidProductRequest request, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken) &&
            !await auth.CanManageStorageAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import or inventory management permission is required.");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Quantity < 1 || request.ExpectedInventory < 0 || request.ActualInventory < 0 ||
            request.UnitPrice < 0 || request.WeightGrams < 0 || request.WeightOunces < 0 || request.TotalCost < 0 || request.WhatnotAuctionSafePrice < 0)
            return BadRequest("Enter a title and valid non-negative values.");
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        product.Title = request.Title.Trim();
        product.ActualSku = string.IsNullOrWhiteSpace(request.ActualSku) ? null : request.ActualSku.Trim();
        product.WhatnotAuctionSafePrice = WhatnotSafePriceCalculator.Calculate(request.TotalCost, await GetSafePricingExchangeRateAsync(cancellationToken));
        product.ItemCode = WhatnotSafePriceCalculator.ToItemCode(product.WhatnotAuctionSafePrice);
        product.Quantity = request.Quantity;
        // In the edit form Inventory represents the physically counted stock.
        // Expected inventory remains the imported/batch quantity of record.
        product.Inventory = request.ExpectedInventory;
        product.ActualInventory = request.ActualInventory;
        product.UnitPrice = request.UnitPrice;
        product.WeightGrams = request.WeightGrams;
        product.WeightOunces = request.WeightOunces ?? (request.WeightGrams.HasValue ? decimal.Round(request.WeightGrams.Value / 28.349523125m, 4) : null);
        product.Material = string.IsNullOrWhiteSpace(request.Material) ? null : request.Material.Trim();
        product.TotalCost = request.TotalCost;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(product));
    }

    [HttpPost("move-from-inventory")]
    public async Task<ActionResult<LiveStreamBidProductDto>> MoveFromInventory(
        MoveProductToLiveStreamBidRequest request,
        CancellationToken cancellationToken)
    {
        if (!await auth.CanManageStorageAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Inventory management permission is required.");
        if (request.Quantity < 1 || request.BinNumber is < 1 or > 2)
            return BadRequest("Choose a valid location and quantity.");
        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        var actor = new InventoryAuditActor(
            user.UserName, user.Role, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
        var bidProduct = await inventoryWrites.MoveToBidAsync(
            request.LevelId, request.BinNumber, request.ProductId, request.Quantity, actor, cancellationToken);
        return bidProduct is null
            ? Conflict("The selected warehouse location does not have enough inventory.")
            : Ok(ToDto(bidProduct));
    }

    [HttpPost("{id:int}/actual-inventory")]
    public async Task<ActionResult<LiveStreamBidProductDto>> AdjustActualInventory(
        int id, AdjustLiveStreamBidActualInventoryRequest request, CancellationToken cancellationToken)
    {
        if (request.Delta is 0 or < -1000 or > 1000) return BadRequest("Adjustment must be between -1000 and 1000 and cannot be zero.");
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        var next = product.ActualInventory + request.Delta;
        if (next < 0) return Conflict("Actual inventory cannot be negative.");
        if (next > product.Inventory) return Conflict("Actual inventory cannot exceed expected inventory.");
        product.ActualInventory = next;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(product));
    }

    private static LiveStreamBidProductDto ToDto(LiveStreamBidProduct item) => new(
        item.Id, item.SourceProductId, item.BatchNumber, item.BatchProductNumber, item.ActualSku, item.RequiresLocalLabel, item.WhatnotAuctionSafePrice, item.ItemCode, NormalizeBidCode(item.Uuid), item.Title, item.Quantity, item.Inventory, item.ActualInventory, item.UnitPrice, item.WeightGrams,
        item.WeightOunces, item.Material, EffectiveCost(item), item.ImportedFileName, item.IsSold, item.SoldAt, item.CreatedAt, item.UpdatedAt);

    private static int ParseBatchNumber(string value) =>
        value.Length > 1 && int.TryParse(value[1..], out var number) ? number : 0;

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private async Task<decimal> GetSafePricingExchangeRateAsync(CancellationToken cancellationToken)
    {
        try { return (await exchangeRates.GetRmbToUsdAsync(cancellationToken)).Rate; }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { return 0.15m; }
    }

    private static LiveStreamBidSaleDto ToSaleDto(LiveStreamBidSale item) => new(
        item.Id, item.BidProductId, item.ProductUuid, item.ProductTitle, item.Quantity, item.Cost,
        item.ActualSalePrice, item.SalePriceRmb, item.ExchangeRateRmbToUsd, item.ExchangeRateDate,
        item.Profit, item.ProfitUsd, item.SaleDate, item.SoldAt, item.SoldBy);

    private async Task<LiveStreamBidDailySummaryDto> SummaryForDateAsync(DateOnly date, bool includeSales, CancellationToken cancellationToken)
    {
        var sales = await db.LiveStreamBidSales.Where(item => item.SaleDate == date)
            .OrderByDescending(item => item.SoldAt).ToListAsync(cancellationToken);
        await PopulateMissingExchangeRatesAsync(sales, cancellationToken);
        return BuildSummary(date, sales, includeSales);
    }

    private async Task PopulateMissingExchangeRatesAsync(List<LiveStreamBidSale> sales, CancellationToken cancellationToken)
    {
        var missing = sales.Where(item => item.Cost.HasValue && !item.ExchangeRateRmbToUsd.HasValue).ToArray();
        if (missing.Length == 0) return;
        try
        {
            var exchange = await exchangeRates.GetRmbToUsdAsync(cancellationToken);
            foreach (var sale in missing)
            {
                sale.ExchangeRateRmbToUsd = exchange.Rate;
                sale.ExchangeRateDate = exchange.Date;
                if (sale.ActualSalePrice.HasValue)
                {
                    sale.SalePriceRmb = decimal.Round(sale.ActualSalePrice.Value / exchange.Rate, 2);
                    sale.Profit = sale.SalePriceRmb.Value - sale.Cost!.Value;
                    sale.ProfitUsd = sale.ActualSalePrice.Value - decimal.Round(sale.Cost.Value * exchange.Rate, 2);
                }
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // RMB history remains available if the exchange-rate provider is temporarily unavailable.
        }
    }

    private static LiveStreamBidDailySummaryDto BuildSummary(DateOnly date, IEnumerable<LiveStreamBidSale> source, bool includeSales)
    {
        var sales = source.ToArray();
        return new LiveStreamBidDailySummaryDto(
            date, sales.Length, sales.Sum(item => item.Quantity), sales.Sum(item => item.Cost ?? 0),
            sales.Sum(item => (item.Cost ?? 0) * (item.ExchangeRateRmbToUsd ?? 0)),
            sales.Sum(item => item.ActualSalePrice ?? 0), sales.Sum(item => item.SalePriceRmb ?? 0),
            sales.Sum(item => item.Profit ?? 0), sales.Sum(item => item.ProfitUsd ?? 0),
            includeSales ? sales.Select(ToSaleDto).ToArray() : Array.Empty<LiveStreamBidSaleDto>());
    }

    private async Task<(string?[] Resolved, Dictionary<int, (string Original, string New)> Changes)> ResolveImportedUuidsAsync(
        IReadOnlyList<CreateLiveStreamBidListRow> rows, CancellationToken cancellationToken)
    {
        var existing = await db.LiveStreamBidProducts.AsNoTracking().Select(item => item.Uuid).ToListAsync(cancellationToken);
        var used = existing.Select(NormalizeBidCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolved = new string?[rows.Count];
        var changes = new Dictionary<int, (string Original, string New)>();
        for (var index = 0; index < rows.Count; index++)
        {
            var external = rows[index].ExternalUuid;
            if (string.IsNullOrWhiteSpace(external)) continue;
            var original = NormalizeBidCode(external);
            if (!string.IsNullOrWhiteSpace(original) && used.Add(original))
            {
                resolved[index] = original;
                continue;
            }

            string replacement;
            do replacement = Guid.NewGuid().ToString().ToUpperInvariant();
            while (!used.Add(replacement));
            resolved[index] = replacement;
            changes[index] = (original, replacement);
        }
        return (resolved, changes);
    }

    private static IReadOnlyList<LiveStreamBidRegeneratedUuidDto> BuildRegeneratedUuidDtos(
        IReadOnlyList<LiveStreamBidProduct> products, IReadOnlyDictionary<int, (string Original, string New)> changes) =>
        changes.OrderBy(change => change.Key).Select(change => new LiveStreamBidRegeneratedUuidDto(
            change.Key + 2, products[change.Key].Id, change.Value.Original, change.Value.New, products[change.Key].Title)).ToArray();

    private static string NormalizeBidCode(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("BID", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..].TrimStart(' ', '|', ':', '-');
        return normalized.Trim();
    }

    private static string NormalizeLocalCode(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("LPM", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..].TrimStart(' ', '|', ':', '-');
        return normalized.Trim();
    }

    private static decimal? EffectiveCost(LiveStreamBidProduct product) =>
        product.TotalCost ?? decimal.Round(
            (product.UnitPrice * product.Quantity) + ((product.WeightGrams ?? 0) * 0.05m), 2);
}
