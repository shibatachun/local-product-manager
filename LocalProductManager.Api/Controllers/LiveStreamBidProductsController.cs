using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using LocalProductManager.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Controllers;

[ApiController]
[Route("api/live-stream-bid-products")]
public sealed class LiveStreamBidProductsController(AppDbContext db, LiveStreamBidProductImportService importer, AuthService auth, ExchangeRateService exchangeRates, InventoryWriteService inventoryWrites) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LiveStreamBidProductDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok((await db.LiveStreamBidProducts.AsNoTracking().OrderByDescending(item => item.UpdatedAt).ToListAsync(cancellationToken)).Select(ToDto));

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
        if (product.Inventory <= 0) return Conflict("This bid product has no inventory remaining.");
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

    private static LiveStreamBidProductDto ToDto(LiveStreamBidProduct item) => new(
        item.Id, item.SourceProductId, NormalizeBidCode(item.Uuid), item.Title, item.Quantity, item.Inventory, item.UnitPrice, item.WeightGrams,
        item.WeightOunces, item.Material, EffectiveCost(item), item.ImportedFileName, item.IsSold, item.SoldAt, item.CreatedAt, item.UpdatedAt);

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
