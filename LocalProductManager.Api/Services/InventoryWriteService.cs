using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed record InventoryAuditActor(
    string UserName,
    string UserRole,
    string? RemoteIp,
    string? UserAgent);

public sealed class InventoryWriteService(AppDbContext db, ExchangeRateService exchangeRates)
{

    public Task<bool> MoveAsync(
        int productId, int? sourceLevelId, int? sourceBinNumber,
        int? targetLevelId, int? targetBinNumber, int quantity,
        InventoryAuditActor actor, CancellationToken cancellationToken, string? targetInventoryChannel = null) =>
        ExecuteWithRetryAsync(async () =>
        {
            if (quantity < 1 || !sourceLevelId.HasValue && !targetLevelId.HasValue
                || sourceLevelId.HasValue && sourceLevelId == targetLevelId && sourceBinNumber == targetBinNumber)
                return false;
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var sourceChannel = sourceLevelId.HasValue && sourceBinNumber.HasValue
                ? await CurrentChannelAsync(sourceLevelId.Value, sourceBinNumber.Value, productId, cancellationToken)
                : "general";
            var targetChannel = targetInventoryChannel ?? sourceChannel ?? "general";
            if (!IsValidChannel(targetChannel)) return false;
            if (targetLevelId.HasValue && targetBinNumber.HasValue)
            {
                var existingTargetChannel = await CurrentChannelAsync(targetLevelId.Value, targetBinNumber.Value, productId, cancellationToken);
                if (existingTargetChannel is not null && existingTargetChannel != targetChannel) return false;
            }
            if (sourceLevelId.HasValue && sourceBinNumber.HasValue)
            {
                var sourceBefore = await CurrentQuantityAsync(sourceLevelId.Value, sourceBinNumber.Value, productId, cancellationToken);
                if (sourceBefore < quantity) return false;
                if (await AdjustLocationQuantityAsync(sourceLevelId.Value, sourceBinNumber.Value, productId, -quantity, now, cancellationToken) == 0)
                    return false;
                await DeleteZeroQuantityAsync(sourceLevelId.Value, sourceBinNumber.Value, productId, cancellationToken);
                await WriteHistoryAsync(sourceLevelId.Value, sourceBinNumber.Value, productId, sourceBefore, sourceBefore - quantity,
                    actor, targetLevelId.HasValue ? $"move_out:{targetLevelId}:{targetBinNumber}" : "move_to_unassigned", now, cancellationToken);
            }
            else
            {
                var totalQty = await db.Products.Where(item => item.Id == productId).Select(item => item.Qty).FirstOrDefaultAsync(cancellationToken);
                var locatedQty = await db.ShelfLevelInventories.Where(item => item.ProductId == productId)
                    .SumAsync(item => (int?)item.Qty, cancellationToken) ?? 0;
                if (totalQty - locatedQty < quantity) return false;
            }
            if (targetLevelId.HasValue && targetBinNumber.HasValue)
            {
                var targetBefore = await CurrentQuantityAsync(targetLevelId.Value, targetBinNumber.Value, productId, cancellationToken);
                if (await AdjustLocationQuantityAsync(targetLevelId.Value, targetBinNumber.Value, productId, quantity, now, cancellationToken, targetChannel) == 0)
                    return false;
                await WriteHistoryAsync(targetLevelId.Value, targetBinNumber.Value, productId, targetBefore, targetBefore + quantity,
                    actor, sourceLevelId.HasValue ? $"move_in:{sourceLevelId}:{sourceBinNumber}" : "place_unassigned", now, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public Task<LiveStreamBidProduct?> MoveToBidAsync(
        int levelId,
        int binNumber,
        int productId,
        int quantity,
        InventoryAuditActor actor,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);
            if (product is null) return null;
            var beforeQty = await CurrentQuantityAsync(levelId, binNumber, productId, cancellationToken);
            if (quantity < 1 || beforeQty < quantity) return null;
            var now = DateTimeOffset.UtcNow;
            var safePricingRate = await GetSafePricingExchangeRateAsync(cancellationToken);
            var affected = await AdjustLocationQuantityAsync(levelId, binNumber, productId, -quantity, now, cancellationToken);
            if (affected == 0) return null;
            await DeleteZeroQuantityAsync(levelId, binNumber, productId, cancellationToken);
            await AdjustProductTotalAsync(productId, -quantity, now, cancellationToken);
            await WriteHistoryAsync(levelId, binNumber, productId, beforeQty, beforeQty - quantity, actor, "bid_transfer", now, cancellationToken);
            var bidProduct = new LiveStreamBidProduct
            {
                SourceProductId = product.Id,
                ActualSku = product.Sku,
                RequiresLocalLabel = true,
                Uuid = Guid.NewGuid().ToString().ToUpperInvariant(),
                Title = product.ProductName,
                Quantity = 1,
                Inventory = quantity,
                ActualInventory = quantity,
                UnitPrice = product.Cost ?? 0,
                WeightOunces = product.Weight,
                WeightGrams = product.Weight.HasValue ? decimal.Round(product.Weight.Value * 28.349523125m, 4) : null,
                TotalCost = product.Cost.HasValue ? decimal.Round(product.Cost.Value, 2) : null,
                WhatnotAuctionSafePrice = WhatnotSafePriceCalculator.Calculate(product.Cost, safePricingRate),
                ItemCode = WhatnotSafePriceCalculator.ToItemCode(WhatnotSafePriceCalculator.Calculate(product.Cost, safePricingRate)),
                ImportedFileName = "Local inventory transfer",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.LiveStreamBidProducts.Add(bidProduct);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return bidProduct;
        }, cancellationToken);

    private async Task<decimal> GetSafePricingExchangeRateAsync(CancellationToken cancellationToken)
    {
        try { return (await exchangeRates.GetRmbToUsdAsync(cancellationToken)).Rate; }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { return 0.15m; }
    }

    public Task SetAsync(
        int levelId,
        int binNumber,
        int productId,
        int qty,
        InventoryAuditActor actor,
        CancellationToken cancellationToken,
        string? inventoryChannel = null) =>
        ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var beforeQty = await CurrentQuantityAsync(levelId, binNumber, productId, cancellationToken);
            var channel = inventoryChannel ?? await CurrentChannelAsync(levelId, binNumber, productId, cancellationToken) ?? "general";
            if (!IsValidChannel(channel)) throw new ArgumentException("Invalid inventory channel.", nameof(inventoryChannel));
            await SetLocationQuantityAsync(levelId, binNumber, productId, qty, now, cancellationToken, channel);

            await AdjustProductTotalAsync(productId, qty - beforeQty, now, cancellationToken);
            await WriteHistoryAsync(
                levelId, binNumber, productId, beforeQty, qty, actor, "set", now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> AdjustAsync(
        int levelId,
        int binNumber,
        int productId,
        int delta,
        InventoryAuditActor actor,
        CancellationToken cancellationToken,
        string? inventoryChannel = null) =>
        ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var beforeQty = await CurrentQuantityAsync(levelId, binNumber, productId, cancellationToken);
            var existingChannel = await CurrentChannelAsync(levelId, binNumber, productId, cancellationToken);
            var channel = inventoryChannel ?? existingChannel ?? "general";
            if (!IsValidChannel(channel) || delta > 0 && existingChannel is not null && existingChannel != channel)
                return false;

            var affected = await AdjustLocationQuantityAsync(
                levelId, binNumber, productId, delta, now, cancellationToken, channel);

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await DeleteZeroQuantityAsync(levelId, binNumber, productId, cancellationToken);

            await AdjustProductTotalAsync(productId, delta, now, cancellationToken);
            await WriteHistoryAsync(
                levelId, binNumber, productId, beforeQty, beforeQty + delta, actor, "adjust", now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private async Task SetLocationQuantityAsync(
        int levelId, int binNumber, int productId, int qty, DateTimeOffset now,
        CancellationToken cancellationToken, string channel)
    {
        if (qty == 0)
        {
            await db.ShelfLevelInventories
                .Where(item => item.ShelfLevelId == levelId
                    && item.BinNumber == binNumber
                    && item.ProductId == productId)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO shelf_level_inventory
                    ("ShelfLevelId", "BinNumber", "ProductId", "Qty", "UpdatedAt", "InventoryChannel")
                VALUES ({levelId}, {binNumber}, {productId}, {qty}, {now}, {channel})
                ON CONFLICT ("ShelfLevelId", "BinNumber", "ProductId")
                DO UPDATE SET "Qty" = EXCLUDED."Qty", "UpdatedAt" = EXCLUDED."UpdatedAt", "InventoryChannel" = EXCLUDED."InventoryChannel"
                """, cancellationToken);
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO shelf_level_inventory
                    (ShelfLevelId, BinNumber, ProductId, Qty, UpdatedAt, InventoryChannel)
                VALUES ({levelId}, {binNumber}, {productId}, {qty}, {now}, {channel})
                ON CONFLICT(ShelfLevelId, BinNumber, ProductId)
                DO UPDATE SET Qty = excluded.Qty, UpdatedAt = excluded.UpdatedAt, InventoryChannel = excluded.InventoryChannel
                """, cancellationToken);
        }
    }

    private Task<int> AdjustLocationQuantityAsync(
        int levelId, int binNumber, int productId, int delta, DateTimeOffset now,
        CancellationToken cancellationToken, string channel = "general")
    {
        if (db.Database.IsNpgsql())
        {
            return delta > 0
                ? db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO shelf_level_inventory
                        ("ShelfLevelId", "BinNumber", "ProductId", "Qty", "UpdatedAt", "InventoryChannel")
                    VALUES ({levelId}, {binNumber}, {productId}, {delta}, {now}, {channel})
                    ON CONFLICT ("ShelfLevelId", "BinNumber", "ProductId")
                    DO UPDATE SET
                        "Qty" = shelf_level_inventory."Qty" + EXCLUDED."Qty",
                        "UpdatedAt" = EXCLUDED."UpdatedAt"
                    WHERE shelf_level_inventory."InventoryChannel" = EXCLUDED."InventoryChannel"
                    """, cancellationToken)
                : db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE shelf_level_inventory
                    SET "Qty" = "Qty" + {delta}, "UpdatedAt" = {now}
                    WHERE "ShelfLevelId" = {levelId}
                      AND "BinNumber" = {binNumber}
                      AND "ProductId" = {productId}
                      AND "Qty" >= {-delta}
                    """, cancellationToken);
        }

        return delta > 0
            ? db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO shelf_level_inventory
                    (ShelfLevelId, BinNumber, ProductId, Qty, UpdatedAt, InventoryChannel)
                VALUES ({levelId}, {binNumber}, {productId}, {delta}, {now}, {channel})
                ON CONFLICT(ShelfLevelId, BinNumber, ProductId)
                DO UPDATE SET Qty = shelf_level_inventory.Qty + excluded.Qty, UpdatedAt = excluded.UpdatedAt
                WHERE shelf_level_inventory.InventoryChannel = excluded.InventoryChannel
                """, cancellationToken)
            : db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE shelf_level_inventory
                SET Qty = Qty + {delta}, UpdatedAt = {now}
                WHERE ShelfLevelId = {levelId} AND BinNumber = {binNumber}
                  AND ProductId = {productId} AND Qty >= {-delta}
                """, cancellationToken);
    }

    private Task<int> DeleteZeroQuantityAsync(
        int levelId, int binNumber, int productId, CancellationToken cancellationToken) =>
        db.ShelfLevelInventories
            .Where(item => item.ShelfLevelId == levelId
                && item.BinNumber == binNumber
                && item.ProductId == productId
                && item.Qty == 0)
            .ExecuteDeleteAsync(cancellationToken);

    private Task<int> CurrentQuantityAsync(
        int levelId,
        int binNumber,
        int productId,
        CancellationToken cancellationToken) =>
        db.ShelfLevelInventories
            .Where(item => item.ShelfLevelId == levelId
                && item.BinNumber == binNumber
                && item.ProductId == productId)
            .Select(item => item.Qty)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<string?> CurrentChannelAsync(int levelId, int binNumber, int productId, CancellationToken cancellationToken) =>
        db.ShelfLevelInventories
            .Where(item => item.ShelfLevelId == levelId && item.BinNumber == binNumber && item.ProductId == productId)
            .Select(item => item.InventoryChannel)
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsValidChannel(string channel) => channel is "general" or "walmart" or "ebay";

    private async Task WriteHistoryAsync(
        int levelId,
        int binNumber,
        int productId,
        int beforeQty,
        int afterQty,
        InventoryAuditActor actor,
        string source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (beforeQty == afterQty) return;

        var product = await db.Products.AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new { item.LocalInventoryId, item.Sku, item.ProductName })
            .FirstAsync(cancellationToken);
        var location = await db.ShelfLevels.AsNoTracking()
            .Where(item => item.Id == levelId)
            .Select(item => new
            {
                item.Code,
                item.LevelNumber,
                RowNumber = item.WarehouseShelf!.RowNumber,
                RackNumber = item.WarehouseShelf.RackNumber
            })
            .FirstAsync(cancellationToken);
        var locationCode = $"{location.Code}-B{binNumber:00}";
        var locationDescription = $"Row {location.RowNumber} · Rack {location.RackNumber} · Level {location.LevelNumber} · Bin {binNumber}";
        var delta = afterQty - beforeQty;
        var operation = delta > 0 ? "stock_in" : "stock_out";
        var userAgent = string.IsNullOrWhiteSpace(actor.UserAgent)
            ? null
            : actor.UserAgent[..Math.Min(actor.UserAgent.Length, 512)];

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inventory_history
                    ("ProductId", "LocalInventoryId", "Sku", "ProductName", "ShelfLevelId", "BinNumber",
                     "LocationCode", "LocationDescription", "Operation", "BeforeQty", "AfterQty", "Delta",
                     "UserName", "UserRole", "RemoteIp", "UserAgent", "Source", "CreatedAt")
                VALUES
                    ({productId}, {product.LocalInventoryId}, {product.Sku}, {product.ProductName},
                     {levelId}, {binNumber}, {locationCode}, {locationDescription}, {operation},
                     {beforeQty}, {afterQty}, {delta}, {actor.UserName}, {actor.UserRole},
                     {actor.RemoteIp}, {userAgent}, {source}, {now})
                """, cancellationToken);
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inventory_history
                    (ProductId, LocalInventoryId, Sku, ProductName, ShelfLevelId, BinNumber,
                     LocationCode, LocationDescription, Operation, BeforeQty, AfterQty, Delta,
                     UserName, UserRole, RemoteIp, UserAgent, Source, CreatedAt)
                VALUES
                    ({productId}, {product.LocalInventoryId}, {product.Sku}, {product.ProductName},
                     {levelId}, {binNumber}, {locationCode}, {locationDescription}, {operation},
                     {beforeQty}, {afterQty}, {delta}, {actor.UserName}, {actor.UserRole},
                     {actor.RemoteIp}, {userAgent}, {source}, {now})
                """, cancellationToken);
        }
    }

    private Task<int> AdjustProductTotalAsync(
        int productId,
        int delta,
        DateTimeOffset now,
        CancellationToken cancellationToken) => db.Products
            .Where(item => item.Id == productId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Qty, item => item.Qty + delta < 0 ? 0 : item.Qty + delta)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

    private static Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken) => operation();
}
