using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/live-stream-bid-products")]
public sealed class LiveStreamBidProductsController(AppDbContext db, LiveStreamBidProductImportService importer, WhatnotShowCostImportService showImporter, AuthService auth, ExchangeRateService exchangeRates, InventoryWriteService inventoryWrites, IHttpClientFactory httpClientFactory) : ControllerBase
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
        var normalizedUpper = normalized.ToUpperInvariant();
        var candidates = await db.LiveStreamBidProducts.AsNoTracking()
            .Where(product => product.Uuid.ToUpper().Contains(normalizedUpper))
            .ToListAsync(cancellationToken);
        var item = candidates.FirstOrDefault(product =>
            string.Equals(NormalizeBidCode(product.Uuid), normalized, StringComparison.OrdinalIgnoreCase));
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
        if (request.Quantity < 1) return BadRequest("Sold quantity must be at least 1.");
        if (!request.ShowSessionId.HasValue)
            return Conflict("Start a BID show cost session before recording sales.");
        var show = await db.LiveStreamBidShowSessions.FirstOrDefaultAsync(
            item => item.Id == request.ShowSessionId.Value, cancellationToken);
        if (show is null) return NotFound("The BID show cost session was not found.");
        if (show.EndedAt.HasValue) return Conflict("This BID show cost session has already been settled.");
        var hasImportedOrders = await db.LiveStreamBidShowOrders.AsNoTracking()
            .AnyAsync(item => item.ShowSessionId == show.Id, cancellationToken);
        LiveStreamBidShowOrder? showOrder = null;
        if (hasImportedOrders)
        {
            if (!request.ShowOrderId.HasValue)
                return Conflict("Choose a customer order before confirming this scanned cost.");
            showOrder = await db.LiveStreamBidShowOrders.FirstOrDefaultAsync(
                item => item.Id == request.ShowOrderId.Value && item.ShowSessionId == show.Id, cancellationToken);
            if (showOrder is null) return NotFound("The selected customer order was not found in this show.");
            if (request.Quantity > showOrder.Quantity - showOrder.RecordedQuantity)
                return Conflict($"Only {Math.Max(0, showOrder.Quantity - showOrder.RecordedQuantity)} item(s) remain in this customer order.");
        }
        var product = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        if (product.Inventory < request.Quantity) return Conflict($"Only {product.Inventory} expected inventory item(s) remain for this BID product.");
        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        // Show cost capture counts merchandise only. Keep TotalCost for suggested-price calculations.
        decimal? effectiveCost = decimal.Round(product.UnitPrice * product.Quantity * request.Quantity, 2);
        decimal? rate = null;
        DateOnly? rateDate = null;
        decimal? salePriceRmb = null;
        decimal? profitRmb = null;
        decimal? profitUsd = null;
        if (request.ActualSalePrice.HasValue)
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
                // Cost capture must remain available during a show even if the optional exchange-rate provider is offline.
                // The existing history backfill will populate conversion and profit fields later.
            }
        }
        var sale = new LiveStreamBidSale
        {
            ShowSessionId = show.Id,
            ShowOrderId = showOrder?.Id,
            BidProductId = product.Id,
            ProductUuid = product.Uuid,
            ProductTitle = product.Title,
            Quantity = product.Quantity,
            SoldQuantity = request.Quantity,
            Cost = effectiveCost,
            ActualSalePrice = request.ActualSalePrice,
            SalePriceRmb = salePriceRmb,
            ExchangeRateRmbToUsd = rate,
            ExchangeRateDate = rateDate,
            Profit = profitRmb,
            ProfitUsd = profitUsd,
            SaleDate = show.ShowDate,
            SoldAt = now,
            SoldBy = user.UserName
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (showOrder is not null)
        {
            var allocated = await db.LiveStreamBidShowOrders
                .Where(item => item.Id == showOrder.Id && item.RecordedQuantity + request.Quantity <= item.Quantity)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.RecordedQuantity, item => item.RecordedQuantity + request.Quantity), cancellationToken);
            if (allocated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict("This customer order no longer has enough unrecorded quantity. Reload and try again.");
            }
        }
        var inventoryUpdated = await db.LiveStreamBidProducts
            .Where(item => item.Id == product.Id && item.Inventory >= request.Quantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Inventory, item => item.Inventory - request.Quantity)
                .SetProperty(item => item.ActualInventory,
                    item => item.ActualInventory >= request.Quantity ? item.ActualInventory - request.Quantity : 0)
                .SetProperty(item => item.IsSold, item => item.Inventory == request.Quantity)
                .SetProperty(item => item.SoldAt, item => item.Inventory == request.Quantity ? now : null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        if (inventoryUpdated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict("BID inventory changed before confirmation. Reload and try again.");
        }
        db.LiveStreamBidSales.Add(sale);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToSaleDto(sale));
    }

    [HttpGet("show-sessions/active")]
    public async Task<ActionResult<LiveStreamBidShowSummaryDto?>> ActiveShowSession(CancellationToken cancellationToken)
    {
        var show = await db.LiveStreamBidShowSessions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.EndedAt == null, cancellationToken);
        if (show is null) return Ok(null);
        var sales = await db.LiveStreamBidSales
            .Where(item => item.ShowSessionId == show.Id).ToListAsync(cancellationToken);
        sales.Sort((left, right) => right.SoldAt.CompareTo(left.SoldAt));
        await PopulateMissingExchangeRatesAsync(sales, cancellationToken);
        return Ok(await BuildShowSummaryAsync(show, sales, true, cancellationToken));
    }

    [HttpPost("show-sessions/start")]
    public async Task<ActionResult<LiveStreamBidShowSummaryDto>> StartShowSession(CancellationToken cancellationToken)
    {
        if (await db.LiveStreamBidShowSessions.AsNoTracking().AnyAsync(item => item.EndedAt == null, cancellationToken))
            return Conflict("A BID show cost session is already active. Stop and settle it before starting another show.");

        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        var date = DateOnly.FromDateTime(DateTime.Now);
        var lastNumber = await db.LiveStreamBidShowSessions.AsNoTracking()
            .Where(item => item.ShowDate == date)
            .Select(item => (int?)item.ShowNumber).MaxAsync(cancellationToken) ?? 0;
        var show = new LiveStreamBidShowSession
        {
            ShowDate = date,
            ShowNumber = lastNumber + 1,
            StartedAt = DateTimeOffset.UtcNow,
            StartedBy = user.UserName
        };
        db.LiveStreamBidShowSessions.Add(show);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            return Conflict("Another BID show cost session was started at the same time. Reload the page to resume it.");
        }
        return Ok(await BuildShowSummaryAsync(show, [], true, cancellationToken));
    }

    [HttpPost("show-sessions/import")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<LiveStreamBidShowImportResult>> ImportShowReport(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0) return BadRequest("Choose a non-empty Whatnot show report CSV.");
        if (await db.LiveStreamBidShowSessions.AsNoTracking().AnyAsync(item => item.EndedAt == null, cancellationToken))
            return Conflict("Settle the active show before importing another show report.");
        ParsedWhatnotShow parsed;
        try
        {
            await using var stream = file.OpenReadStream();
            parsed = showImporter.Read(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or DecoderFallbackException)
        { return BadRequest(ex.Message); }
        if (await db.LiveStreamBidShowSessions.AsNoTracking().AnyAsync(
            item => item.ImportedFileSha256 == parsed.Sha256, cancellationToken))
            return Conflict("This exact Whatnot show report has already been imported.");
        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        var lastNumber = await db.LiveStreamBidShowSessions.AsNoTracking()
            .Where(item => item.ShowDate == parsed.ShowDate).Select(item => (int?)item.ShowNumber)
            .MaxAsync(cancellationToken) ?? 0;
        var show = new LiveStreamBidShowSession
        {
            ShowDate = parsed.ShowDate,
            ShowNumber = lastNumber + 1,
            ShowName = Path.GetFileNameWithoutExtension(file.FileName),
            ImportedFileName = Path.GetFileName(file.FileName),
            ImportedFileSha256 = parsed.Sha256,
            StartedAt = DateTimeOffset.UtcNow,
            StartedBy = user.UserName,
            Orders = parsed.Orders.ToList()
        };
        db.LiveStreamBidShowSessions.Add(show);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { return Conflict("This show report was imported by another request. Reload the page."); }
        var summary = await BuildShowSummaryAsync(show, [], true, cancellationToken);
        return Ok(new LiveStreamBidShowImportResult(summary, parsed.Orders.Count,
            parsed.Orders.Select(item => item.CustomerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(), parsed.IgnoredRows));
    }

    [HttpPost("show-sessions/{showSessionId:int}/stop")]
    public async Task<ActionResult<LiveStreamBidShowSummaryDto>> StopShowSession(int showSessionId, CancellationToken cancellationToken)
    {
        var show = await db.LiveStreamBidShowSessions.FirstOrDefaultAsync(item => item.Id == showSessionId, cancellationToken);
        if (show is null) return NotFound();
        if (show.EndedAt.HasValue) return Conflict("This BID show cost session has already been settled.");
        var hasOrders = await db.LiveStreamBidShowOrders.AsNoTracking()
            .AnyAsync(item => item.ShowSessionId == show.Id, cancellationToken);
        var hasSales = await db.LiveStreamBidSales.AsNoTracking()
            .AnyAsync(item => item.ShowSessionId == show.Id, cancellationToken);
        if (!hasOrders && !hasSales)
        {
            var emptySummary = await BuildShowSummaryAsync(show, [], true, cancellationToken);
            db.LiveStreamBidShowSessions.Remove(show);
            await db.SaveChangesAsync(cancellationToken);
            return Ok(emptySummary);
        }
        var incomplete = await db.LiveStreamBidShowOrders.AsNoTracking()
            .Where(order => order.ShowSessionId == show.Id)
            .Select(order => new
            {
                order.CustomerName,
                order.OrderName,
                order.Quantity,
                Recorded = order.RecordedQuantity
            })
            .Where(item => item.Recorded != item.Quantity)
            .FirstOrDefaultAsync(cancellationToken);
        if (incomplete is not null)
            return Conflict($"Cannot settle: {incomplete.CustomerName} / {incomplete.OrderName} has {incomplete.Recorded} of {incomplete.Quantity} cost item(s) recorded.");
        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        show.EndedAt = DateTimeOffset.UtcNow;
        show.EndedBy = user.UserName;
        await db.SaveChangesAsync(cancellationToken);
        var sales = await db.LiveStreamBidSales
            .Where(item => item.ShowSessionId == show.Id).ToListAsync(cancellationToken);
        sales.Sort((left, right) => right.SoldAt.CompareTo(left.SoldAt));
        await PopulateMissingExchangeRatesAsync(sales, cancellationToken);
        return Ok(await BuildShowSummaryAsync(show, sales, true, cancellationToken));
    }

    [HttpPatch("show-orders/{showOrderId:int}/quantity")]
    public async Task<IActionResult> UpdateShowOrderQuantity(int showOrderId, UpdateLiveStreamBidShowOrderQuantityRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity < 1) return BadRequest("Order item quantity must be at least 1.");
        var order = await db.LiveStreamBidShowOrders.AsNoTracking()
            .Where(item => item.Id == showOrderId)
            .Select(item => new { item.RecordedQuantity, item.ShowSession.EndedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null) return NotFound();
        if (order.EndedAt.HasValue) return Conflict("Settled show orders cannot be changed.");
        if (request.Quantity < order.RecordedQuantity)
            return Conflict($"Quantity cannot be below the {order.RecordedQuantity} item(s) already recorded.");
        var updated = await db.LiveStreamBidShowOrders
            .Where(item => item.Id == showOrderId && item.ShowSession.EndedAt == null && item.RecordedQuantity <= request.Quantity)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Quantity, request.Quantity), cancellationToken);
        return updated == 0 ? Conflict("This order changed before the quantity was saved. Reload and try again.") : NoContent();
    }

    [HttpPost("show-orders/{showOrderId:int}/manual-costs")]
    public async Task<ActionResult<LiveStreamBidManualCostDto>> AddManualCost(int showOrderId, AddLiveStreamBidManualCostRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity < 1) return BadRequest("Quantity must be at least 1.");
        if (request.UnitCost < 0) return BadRequest("Unit cost cannot be negative.");
        var order = await db.LiveStreamBidShowOrders.Include(item => item.ShowSession)
            .FirstOrDefaultAsync(item => item.Id == showOrderId, cancellationToken);
        if (order is null) return NotFound();
        if (order.ShowSession.EndedAt.HasValue) return Conflict("This show has already been settled.");
        if (request.Quantity > order.Quantity - order.RecordedQuantity)
            return Conflict($"Only {Math.Max(0, order.Quantity - order.RecordedQuantity)} item(s) remain without a cost.");
        var user = await auth.GetOrCreateCurrentUserAsync(HttpContext, cancellationToken);
        var entry = new LiveStreamBidManualCost
        {
            ShowOrderId = order.Id, Quantity = request.Quantity,
            UnitCost = decimal.Round(request.UnitCost, 2), CreatedBy = user.UserName
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var allocated = await db.LiveStreamBidShowOrders
            .Where(item => item.Id == order.Id && item.RecordedQuantity + request.Quantity <= item.Quantity)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                item => item.RecordedQuantity, item => item.RecordedQuantity + request.Quantity), cancellationToken);
        if (allocated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict("This customer order no longer has enough unrecorded quantity. Reload and try again.");
        }
        db.LiveStreamBidManualCosts.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToManualCostDto(entry));
    }

    [HttpDelete("manual-costs/{id:int}")]
    public async Task<IActionResult> DeleteManualCost(int id, CancellationToken cancellationToken)
    {
        var entry = await db.LiveStreamBidManualCosts.Include(item => item.ShowOrder).ThenInclude(item => item.ShowSession)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entry is null) return NotFound();
        if (entry.ShowOrder.ShowSession.EndedAt.HasValue) return Conflict("Settled show costs cannot be changed.");
        entry.ShowOrder.RecordedQuantity = Math.Max(0, entry.ShowOrder.RecordedQuantity - entry.Quantity);
        db.LiveStreamBidManualCosts.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPatch("manual-costs/{id:int}")]
    public async Task<ActionResult<LiveStreamBidManualCostDto>> UpdateManualCost(
        int id, UpdateLiveStreamBidManualCostRequest request, CancellationToken cancellationToken)
    {
        if (request.UnitCost < 0) return BadRequest("Unit cost cannot be negative.");
        var entry = await db.LiveStreamBidManualCosts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entry is null) return NotFound();
        entry.UnitCost = decimal.Round(request.UnitCost, 2);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToManualCostDto(entry));
    }

    [HttpPatch("sales/{id:int}/cost")]
    public async Task<ActionResult<LiveStreamBidSaleDto>> UpdateScannedCost(
        int id, UpdateLiveStreamBidScannedCostRequest request, CancellationToken cancellationToken)
    {
        if (request.Cost < 0) return BadRequest("Cost cannot be negative.");
        var sale = await db.LiveStreamBidSales.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (sale is null) return NotFound();
        if (!sale.ShowOrderId.HasValue) return Conflict("Only scanned costs linked to a customer order can be edited here.");
        sale.Cost = decimal.Round(request.Cost, 2);
        if (sale.ActualSalePrice.HasValue && sale.ExchangeRateRmbToUsd is > 0)
        {
            sale.SalePriceRmb = decimal.Round(sale.ActualSalePrice.Value / sale.ExchangeRateRmbToUsd.Value, 2);
            sale.Profit = sale.SalePriceRmb.Value - sale.Cost.Value;
            sale.ProfitUsd = sale.ActualSalePrice.Value - decimal.Round(sale.Cost.Value * sale.ExchangeRateRmbToUsd.Value, 2);
        }
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
        var saleDates = await db.LiveStreamBidSales.AsNoTracking().Select(item => item.SaleDate).Distinct().ToListAsync(cancellationToken);
        var showDates = await db.LiveStreamBidShowSessions.AsNoTracking()
            .Where(show => db.LiveStreamBidShowOrders.Any(order => order.ShowSessionId == show.Id)
                || db.LiveStreamBidSales.Any(sale => sale.ShowSessionId == show.Id))
            .Select(item => item.ShowDate).Distinct().ToListAsync(cancellationToken);
        var result = new List<LiveStreamBidDailySummaryDto>();
        foreach (var date in saleDates.Concat(showDates).Distinct().OrderByDescending(item => item))
            result.Add(await SummaryForDateAsync(date, false, cancellationToken));
        return Ok(result);
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
        var order = sale.ShowOrderId.HasValue
            ? await db.LiveStreamBidShowOrders.FirstOrDefaultAsync(item => item.Id == sale.ShowOrderId.Value, cancellationToken)
            : null;
        if (order is not null) order.RecordedQuantity = Math.Max(0, order.RecordedQuantity - sale.SoldQuantity);
        if (product is not null)
        {
            product.Inventory += sale.SoldQuantity;
            product.ActualInventory += sale.SoldQuantity;
        }
        db.LiveStreamBidSales.Remove(sale);
        await db.SaveChangesAsync(cancellationToken);
        if (product is not null)
        {
            var latestSale = await db.LiveStreamBidSales.AsNoTracking().Where(item => item.BidProductId == product.Id)
                .OrderByDescending(item => item.SoldAt).Select(item => (DateTimeOffset?)item.SoldAt).FirstOrDefaultAsync(cancellationToken);
            product.IsSold = product.Inventory == 0;
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
        var shows = await db.LiveStreamBidShowSessions.Where(item => item.ShowDate == date).ToListAsync(cancellationToken);
        if (sales.Count == 0 && shows.Count == 0) return NotFound();
        var productIds = sales.Select(item => item.BidProductId).Distinct().ToArray();
        db.LiveStreamBidSales.RemoveRange(sales);
        await db.SaveChangesAsync(cancellationToken);
        db.LiveStreamBidShowSessions.RemoveRange(shows);
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

        var externalFileName = string.IsNullOrWhiteSpace(request.ExternalFileName)
            ? null
            : Path.GetFileName(request.ExternalFileName.Trim());
        var externalHash = NormalizeSha256(request.ExternalFileSha256);
        if (externalFileName is not null)
        {
            if (externalHash is null) return BadRequest("External CSV imports require a valid SHA-256 hash.");
            if (await db.LiveStreamBidProducts.AsNoTracking().AnyAsync(
                    item => item.ImportedFileName == externalFileName || item.ImportedFileSha256 == externalHash,
                    cancellationToken))
                return Conflict("This CSV file name or SHA-256 has already been imported.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var existing = await db.LiveStreamBidProducts.AsNoTracking().Where(item => item.BatchNumber != null)
            .Select(item => item.BatchNumber!).Distinct().ToListAsync(cancellationToken);
        var next = existing.Select(ParseBatchNumber).DefaultIfEmpty(0).Max() + 1;
        var batchNumber = externalFileName is null ? $"B{next:000000}" : ExternalBatchName(externalFileName);
        if (await db.LiveStreamBidProducts.AsNoTracking().AnyAsync(item => item.BatchNumber == batchNumber, cancellationToken))
            return Conflict("A batch with this external CSV file name already exists.");
        var now = DateTimeOffset.UtcNow;
        if (externalFileName is not null)
        {
            db.ImportedBidBatches.Add(new ImportedBidBatch
            {
                FileName = externalFileName,
                Sha256 = externalHash!,
                BatchNumber = batchNumber,
                ImportedAt = now
            });
            try
            {
                // The unique PostgreSQL indexes are the authoritative concurrency gate.
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return Conflict("This CSV file name or SHA-256 has already been imported.");
            }
        }
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
            ImportedFileName = externalFileName ?? $"Local Manager · {batchNumber}",
            ImportedFileSha256 = externalHash,
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
        var normalized = batchNumber.Trim();
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
        var normalizedBatch = batchNumber.Trim();
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
        var normalized = batchNumber.Trim();
        var products = await db.LiveStreamBidProducts
            .Where(item => item.BatchNumber == normalized)
            .ToListAsync(cancellationToken);
        if (products.Count == 0) return NotFound();
        var productIds = products.Select(item => item.Id).ToArray();
        if (await db.LiveStreamBidSales.AsNoTracking().AnyAsync(item => productIds.Contains(item.BidProductId), cancellationToken))
            return Conflict("This batch has sales history and cannot be deleted.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.LiveStreamBidProducts.RemoveRange(products);
        var importedBatch = await db.ImportedBidBatches.FirstOrDefaultAsync(
            item => item.BatchNumber == normalized, cancellationToken);
        if (importedBatch is not null) db.ImportedBidBatches.Remove(importedBatch);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

    [HttpPost("{id:int}/import-to-local")]
    public async Task<ActionResult<ImportLiveStreamBidProductToLocalResult>> ImportToLocal(
        int id, ImportLiveStreamBidProductToLocalRequest request, CancellationToken cancellationToken)
    {
        if (!await auth.CanManageStorageAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Inventory management permission is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var bidProduct = await db.LiveStreamBidProducts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (bidProduct is null) return NotFound();
        if (bidProduct.SourceProductId.HasValue)
            return Conflict("This BID product is already linked to a Local product.");
        var uuid = NormalizeBidCode(bidProduct.Uuid);
        var note = $"BID UUID: {uuid}";
        if (await db.Products.AsNoTracking().AnyAsync(item => item.Note == note, cancellationToken))
            return Conflict("A Local product with this BID UUID already exists.");
        if (string.IsNullOrWhiteSpace(bidProduct.Title) || bidProduct.Title.Trim().Length > 256)
            return BadRequest("The BID title must be 1–256 characters to become a Local product name.");
        if (bidProduct.Quantity < 1 || bidProduct.ActualInventory < 0 || bidProduct.UnitPrice < 0)
            return BadRequest("The BID product has invalid quantity, actual inventory, or unit price.");

        decimal cost;
        try { cost = decimal.Round(checked(bidProduct.UnitPrice * bidProduct.Quantity), 2); }
        catch (OverflowException) { return BadRequest("The calculated Local product cost is too large."); }
        if (cost > 99_999_999.99m)
            return BadRequest("The calculated Local product cost exceeds the Local product limit.");
        var weightOunces = bidProduct.WeightOunces ??
            (bidProduct.WeightGrams.HasValue
                ? decimal.Round(bidProduct.WeightGrams.Value / 28.349523125m, 2)
                : null);
        if (weightOunces < 0)
            return BadRequest("The BID product weight cannot be negative.");

        var categoryNumber = await ProductInventoryCategorySchema.NextNumberAsync(
            db, ProductInventoryCategories.Unknown, cancellationToken);
        var localProduct = new Product
        {
            Sku = $"PENDING-{Guid.NewGuid():N}",
            ProductName = bidProduct.Title.Trim(),
            InventoryCategory = ProductInventoryCategories.Unknown,
            CategoryInventoryId = ProductInventoryCategories.BuildId(ProductInventoryCategories.Unknown, categoryNumber),
            Cost = cost,
            Qty = bidProduct.ActualInventory,
            Weight = weightOunces,
            Note = note,
            ReadyToList = false,
            ImageSource = "local"
        };
        db.Products.Add(localProduct);
        await db.SaveChangesAsync(cancellationToken);
        localProduct.Sku = $"LPM-{localProduct.Id:D6}";
        localProduct.LocalInventoryId = ProductService.BuildLocalInventoryId(localProduct.Id);
        bidProduct.SourceProductId = localProduct.Id;
        if (request.TransferInventory)
        {
            bidProduct.Inventory = 0;
            bidProduct.ActualInventory = 0;
        }
        bidProduct.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new ImportLiveStreamBidProductToLocalResult(
            localProduct.Id, localProduct.LocalInventoryId, localProduct.Sku,
            localProduct.Qty, cost, weightOunces, note));
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
        item.Id, item.SourceProductId, item.BatchNumber, item.BatchProductNumber, item.ActualSku, item.RequiresLocalLabel, item.WhatnotAuctionSafePrice, item.ItemCode, NormalizeBidCode(item.Uuid), item.ImagePath, item.Title, item.Quantity, item.Inventory, item.ActualInventory, item.UnitPrice, item.WeightGrams,
        item.WeightOunces, item.Material, EffectiveCost(item), item.ImportedFileName, item.ImportedFileSha256, item.IsSold, item.SoldAt, item.CreatedAt, item.UpdatedAt);

    private static int ParseBatchNumber(string value) =>
        value.Length > 1 && int.TryParse(value[1..], out var number) ? number : 0;

    private static string ExternalBatchName(string fileName)
    {
        var value = Path.GetFileNameWithoutExtension(fileName).Trim();
        value = Regex.Replace(value, "[\\\\/:*?\"<>|]+", "-");
        return value.Length <= 240 ? value : value[..240];
    }

    [HttpPost("cloud-files")]
    public async Task<ActionResult<IReadOnlyList<CloudCsvFileDto>>> CloudFiles(CloudCsvAccessRequest request, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import permission is required.");
        if (string.IsNullOrWhiteSpace(request.Token)) return BadRequest("Cloud upload token is required.");
        var url = $"http://134.175.148.65/csv-inbox/files?token={Uri.EscapeDataString(request.Token.Trim())}";
        using var response = await httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode, "Could not access the cloud CSV inbox.");
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var paths = Regex.Matches(html, "href=\"/csv-inbox/download/([^?\"]+)\\?token=")
            .Select(match => Uri.UnescapeDataString(match.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var importedNames = await db.LiveStreamBidProducts.AsNoTracking().Where(item => item.ImportedFileName != null)
            .Select(item => item.ImportedFileName!).Distinct().ToListAsync(cancellationToken);
        var imported = importedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Ok(paths.Select(path =>
        {
            var fileName = OriginalCloudFileName(Path.GetFileName(path));
            return new CloudCsvFileDto(path, fileName, imported.Contains(fileName));
        }).ToArray());
    }

    [HttpPost("cloud-files/download")]
    public async Task<IActionResult> DownloadCloudFile(CloudCsvDownloadRequest request, CancellationToken cancellationToken)
    {
        if (!await auth.CanImportDataAsync(HttpContext, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, "Import permission is required.");
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.RemotePath)) return BadRequest();
        var remotePath = request.RemotePath.Replace('\\', '/').TrimStart('/');
        if (remotePath.Contains("..", StringComparison.Ordinal)) return BadRequest("Invalid cloud file path.");
        var encodedPath = string.Join('/', remotePath.Split('/').Select(Uri.EscapeDataString));
        var url = $"http://134.175.148.65/csv-inbox/download/{encodedPath}?token={Uri.EscapeDataString(request.Token.Trim())}";
        using var response = await httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode, "Could not download the cloud CSV file.");
        return File(await response.Content.ReadAsByteArrayAsync(cancellationToken), "text/csv; charset=utf-8", Path.GetFileName(remotePath));
    }

    private static string? NormalizeSha256(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && Regex.IsMatch(normalized, "^[0-9a-f]{64}$") ? normalized : null;
    }

    private static string OriginalCloudFileName(string storedName) =>
        Regex.IsMatch(storedName, @"^\d{6}_\d{8}_\d{6}_BarTender\.csv$", RegexOptions.IgnoreCase)
            ? storedName[7..]
            : storedName;

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private async Task<decimal> GetSafePricingExchangeRateAsync(CancellationToken cancellationToken)
    {
        try { return (await exchangeRates.GetRmbToUsdAsync(cancellationToken)).Rate; }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { return 0.15m; }
    }

    private static LiveStreamBidSaleDto ToSaleDto(LiveStreamBidSale item) => new(
        item.Id, item.ShowSessionId, item.ShowOrderId, item.BidProductId, item.ProductUuid, item.ProductTitle, item.Quantity, item.SoldQuantity, item.Cost,
        item.ActualSalePrice, item.SalePriceRmb, item.ExchangeRateRmbToUsd, item.ExchangeRateDate,
        item.Profit, item.ProfitUsd, item.SaleDate, item.SoldAt, item.SoldBy);

    private static LiveStreamBidManualCostDto ToManualCostDto(LiveStreamBidManualCost item) => new(
        item.Id, item.Quantity, item.UnitCost, item.Quantity * item.UnitCost, item.CreatedAt, item.CreatedBy);

    private static LiveStreamBidShowAttachmentDto ToShowAttachmentDto(LiveStreamBidShowAttachment item) => new(
        item.Id, item.Kind, item.OriginalFileName, item.ContentType, item.UploadedAt, item.UploadedBy);

    private async Task<LiveStreamBidDailySummaryDto> SummaryForDateAsync(DateOnly date, bool includeSales, CancellationToken cancellationToken)
    {
        var sales = await db.LiveStreamBidSales.Where(item => item.SaleDate == date).ToListAsync(cancellationToken);
        sales.Sort((left, right) => right.SoldAt.CompareTo(left.SoldAt));
        var shows = await db.LiveStreamBidShowSessions.AsNoTracking().Where(item => item.ShowDate == date)
            .Where(show => db.LiveStreamBidShowOrders.Any(order => order.ShowSessionId == show.Id)
                || db.LiveStreamBidSales.Any(sale => sale.ShowSessionId == show.Id))
            .OrderBy(item => item.ShowNumber).ToListAsync(cancellationToken);
        await PopulateMissingExchangeRatesAsync(sales, cancellationToken);
        var showSummaries = new List<LiveStreamBidShowSummaryDto>();
        foreach (var show in shows)
            showSummaries.Add(await BuildShowSummaryAsync(show, sales.Where(item => item.ShowSessionId == show.Id), includeSales, cancellationToken));
        var legacySales = sales.Where(item => item.ShowSessionId == null).ToArray();
        if (legacySales.Length > 0) showSummaries.Insert(0, BuildLegacyShowSummary(date, legacySales, includeSales));
        return BuildSummary(date, sales, showSummaries, includeSales);
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

    private static LiveStreamBidDailySummaryDto BuildSummary(
        DateOnly date,
        IEnumerable<LiveStreamBidSale> source,
        IReadOnlyList<LiveStreamBidShowSummaryDto> shows,
        bool includeSales)
    {
        var sales = source.ToArray();
        return new LiveStreamBidDailySummaryDto(
            date, sales.Length, shows.Sum(item => item.InventorySold), shows.Sum(item => item.TotalCostRmb),
            shows.Sum(item => item.TotalCostUsd), shows.Sum(item => item.ActualRevenueUsd),
            shows.Sum(item => item.ActualRevenueRmb), shows.Sum(item => item.RealizedProfitRmb),
            shows.Sum(item => item.RealizedProfitUsd),
            shows,
            includeSales ? sales.Select(ToSaleDto).ToArray() : Array.Empty<LiveStreamBidSaleDto>());
    }

    private async Task<LiveStreamBidShowSummaryDto> BuildShowSummaryAsync(
        LiveStreamBidShowSession show,
        IEnumerable<LiveStreamBidSale> source,
        bool includeSales,
        CancellationToken cancellationToken)
    {
        var sales = source.ToArray();
        var orders = await db.LiveStreamBidShowOrders.AsNoTracking()
            .Where(item => item.ShowSessionId == show.Id).OrderBy(item => item.CustomerName).ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(item => item.Id).ToArray();
        var manualCosts = orderIds.Length == 0 ? [] : await db.LiveStreamBidManualCosts.AsNoTracking()
            .Where(item => orderIds.Contains(item.ShowOrderId)).ToListAsync(cancellationToken);
        manualCosts.Sort((left, right) => left.CreatedAt.CompareTo(right.CreatedAt));
        var attachments = await db.LiveStreamBidShowAttachments.AsNoTracking()
            .Where(item => item.ShowSessionId == show.Id)
            .ToListAsync(cancellationToken);
        attachments.Sort((left, right) => left.UploadedAt.CompareTo(right.UploadedAt));
        var rate = sales.Select(item => item.ExchangeRateRmbToUsd).FirstOrDefault(item => item.HasValue) ?? 0.15m;
        var orderDtos = orders.Select(order =>
        {
            var scannedCosts = sales.Where(item => item.ShowOrderId == order.Id).ToArray();
            var manual = manualCosts.Where(item => item.ShowOrderId == order.Id).ToArray();
            var scannedQuantity = scannedCosts.Sum(item => item.SoldQuantity);
            var manualQuantity = manual.Sum(item => item.Quantity);
            return new LiveStreamBidShowOrderDto(
                order.Id, order.ExternalOrderId, order.CustomerName, order.OrderName, order.Quantity,
                scannedQuantity, manualQuantity, order.RecordedQuantity,
                Math.Max(0, order.Quantity - order.RecordedQuantity),
                scannedCosts.Sum(item => item.Cost ?? 0) + manual.Sum(item => item.Quantity * item.UnitCost),
                includeSales ? scannedCosts.Select(ToSaleDto).ToArray() : [], manual.Select(ToManualCostDto).ToArray())
            {
                PriceEvidence = attachments.Where(item => item.ShowOrderId == order.Id && item.Kind == "price-evidence")
                    .Select(ToShowAttachmentDto).ToArray()
            };
        }).ToArray();
        var customers = orderDtos.GroupBy(item => item.CustomerName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LiveStreamBidShowCustomerDto(
                group.First().CustomerName, group.Sum(item => item.Quantity), group.Sum(item => item.RecordedQuantity),
                group.Sum(item => item.RemainingQuantity), group.Sum(item => item.TotalCostRmb), group.ToArray())
            {
                ShipmentDocuments = attachments.Where(item => item.ShowOrderId == null &&
                    string.Equals(item.CustomerName, group.First().CustomerName, StringComparison.OrdinalIgnoreCase))
                    .Select(ToShowAttachmentDto).ToArray()
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var manualCostRmb = manualCosts.Sum(item => item.Quantity * item.UnitCost);
        var scannedCostRmb = sales.Sum(item => item.Cost ?? 0);
        var scannedProfitRmb = sales.Sum(item => item.Profit ?? 0);
        var scannedProfitUsd = sales.Sum(item => item.ProfitUsd ?? 0);
        var orderedQuantity = orders.Sum(item => item.Quantity);
        var recordedQuantity = orders.Sum(item => item.RecordedQuantity);
        return new LiveStreamBidShowSummaryDto(
            show.Id, show.ShowDate, show.ShowNumber, !show.EndedAt.HasValue,
            show.StartedAt, show.EndedAt, show.StartedBy, show.EndedBy,
            show.ShowName, show.ImportedFileName,
            sales.Length, orders.Count > 0 ? recordedQuantity : sales.Sum(item => item.SoldQuantity), scannedCostRmb + manualCostRmb,
            sales.Sum(item => (item.Cost ?? 0) * (item.ExchangeRateRmbToUsd ?? 0)) + manualCostRmb * rate,
            sales.Sum(item => item.ActualSalePrice ?? 0), sales.Sum(item => item.SalePriceRmb ?? 0),
            scannedProfitRmb - manualCostRmb, scannedProfitUsd - manualCostRmb * rate,
            orderedQuantity, recordedQuantity, Math.Max(0, orderedQuantity - recordedQuantity), customers,
            includeSales ? sales.Select(ToSaleDto).ToArray() : Array.Empty<LiveStreamBidSaleDto>());
    }

    private static LiveStreamBidShowSummaryDto BuildLegacyShowSummary(
        DateOnly date,
        IReadOnlyCollection<LiveStreamBidSale> sales,
        bool includeSales) => new(
            null, date, 0, false, sales.Min(item => item.SoldAt), sales.Max(item => item.SoldAt),
            null, null, null, null, sales.Count, sales.Sum(item => item.SoldQuantity), sales.Sum(item => item.Cost ?? 0),
            sales.Sum(item => (item.Cost ?? 0) * (item.ExchangeRateRmbToUsd ?? 0)),
            sales.Sum(item => item.ActualSalePrice ?? 0), sales.Sum(item => item.SalePriceRmb ?? 0),
            sales.Sum(item => item.Profit ?? 0), sales.Sum(item => item.ProfitUsd ?? 0),
            0, sales.Sum(item => item.SoldQuantity), 0, [],
            includeSales ? sales.Select(ToSaleDto).ToArray() : Array.Empty<LiveStreamBidSaleDto>());

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
        var normalized = value.Trim().TrimStart('\uFEFF');
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
