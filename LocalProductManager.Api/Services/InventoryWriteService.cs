using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed record InventoryAuditActor(
    string UserName,
    string UserRole,
    string? RemoteIp,
    string? UserAgent);

public sealed class InventoryWriteService(AppDbContext db, ILogger<InventoryWriteService> logger)
{
    private const int MaxAttempts = 3;

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
            var affected = await AdjustLocationQuantityAsync(levelId, binNumber, productId, -quantity, now, cancellationToken);
            if (affected == 0) return null;
            await DeleteZeroQuantityAsync(levelId, binNumber, productId, cancellationToken);
            await UpdateProductTotalAsync(productId, now, cancellationToken);
            await WriteHistoryAsync(levelId, binNumber, productId, beforeQty, beforeQty - quantity, actor, "bid_transfer", now, cancellationToken);
            var bidProduct = new LiveStreamBidProduct
            {
                SourceProductId = product.Id,
                Uuid = Guid.NewGuid().ToString().ToUpperInvariant(),
                Title = product.ProductName,
                Quantity = 1,
                Inventory = quantity,
                UnitPrice = product.Cost ?? 0,
                WeightOunces = product.Weight,
                WeightGrams = product.Weight.HasValue ? decimal.Round(product.Weight.Value * 28.349523125m, 4) : null,
                TotalCost = product.Cost.HasValue ? decimal.Round(product.Cost.Value, 2) : null,
                ImportedFileName = "Local inventory transfer",
                CreatedAt = now,
                UpdatedAt = now
            };
            db.LiveStreamBidProducts.Add(bidProduct);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return bidProduct;
        }, cancellationToken);

    public Task SetAsync(
        int levelId,
        int binNumber,
        int productId,
        int qty,
        InventoryAuditActor actor,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var beforeQty = await CurrentQuantityAsync(levelId, binNumber, productId, cancellationToken);

            await SetLocationQuantityAsync(levelId, binNumber, productId, qty, now, cancellationToken);

            await UpdateProductTotalAsync(productId, now, cancellationToken);
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
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var beforeQty = await CurrentQuantityAsync(levelId, binNumber, productId, cancellationToken);

            var affected = await AdjustLocationQuantityAsync(
                levelId, binNumber, productId, delta, now, cancellationToken);

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await DeleteZeroQuantityAsync(levelId, binNumber, productId, cancellationToken);

            await UpdateProductTotalAsync(productId, now, cancellationToken);
            await WriteHistoryAsync(
                levelId, binNumber, productId, beforeQty, beforeQty + delta, actor, "adjust", now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private async Task SetLocationQuantityAsync(
        int levelId, int binNumber, int productId, int qty, DateTimeOffset now,
        CancellationToken cancellationToken)
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
                    ("ShelfLevelId", "BinNumber", "ProductId", "Qty", "UpdatedAt")
                VALUES ({levelId}, {binNumber}, {productId}, {qty}, {now})
                ON CONFLICT ("ShelfLevelId", "BinNumber", "ProductId")
                DO UPDATE SET "Qty" = EXCLUDED."Qty", "UpdatedAt" = EXCLUDED."UpdatedAt"
                """, cancellationToken);
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO shelf_level_inventory
                    (ShelfLevelId, BinNumber, ProductId, Qty, UpdatedAt)
                VALUES ({levelId}, {binNumber}, {productId}, {qty}, {now})
                ON CONFLICT(ShelfLevelId, BinNumber, ProductId)
                DO UPDATE SET Qty = excluded.Qty, UpdatedAt = excluded.UpdatedAt
                """, cancellationToken);
        }
    }

    private Task<int> AdjustLocationQuantityAsync(
        int levelId, int binNumber, int productId, int delta, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            return delta > 0
                ? db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO shelf_level_inventory
                        ("ShelfLevelId", "BinNumber", "ProductId", "Qty", "UpdatedAt")
                    VALUES ({levelId}, {binNumber}, {productId}, {delta}, {now})
                    ON CONFLICT ("ShelfLevelId", "BinNumber", "ProductId")
                    DO UPDATE SET
                        "Qty" = shelf_level_inventory."Qty" + EXCLUDED."Qty",
                        "UpdatedAt" = EXCLUDED."UpdatedAt"
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
                    (ShelfLevelId, BinNumber, ProductId, Qty, UpdatedAt)
                VALUES ({levelId}, {binNumber}, {productId}, {delta}, {now})
                ON CONFLICT(ShelfLevelId, BinNumber, ProductId)
                DO UPDATE SET Qty = shelf_level_inventory.Qty + excluded.Qty, UpdatedAt = excluded.UpdatedAt
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

    private Task<int> UpdateProductTotalAsync(
        int productId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Database.IsNpgsql()
            ? db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE products
                SET "Qty" = COALESCE((
                        SELECT SUM("Qty")
                        FROM shelf_level_inventory
                        WHERE "ProductId" = {productId}
                    ), 0),
                    "UpdatedAt" = {now}
                WHERE "Id" = {productId}
                """, cancellationToken)
            : db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE products
                SET Qty = COALESCE((
                        SELECT SUM(Qty)
                        FROM shelf_level_inventory
                        WHERE ProductId = {productId}
                    ), 0),
                    UpdatedAt = {now}
                WHERE Id = {productId}
                """, cancellationToken);

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception) when (
                attempt < MaxAttempts
                && IsTransientSqliteLock(exception)
                && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMilliseconds(60 * attempt * attempt);
                logger.LogWarning(
                    exception,
                    "SQLite inventory write was busy; retrying attempt {Attempt}/{MaxAttempts} after {DelayMs} ms.",
                    attempt + 1,
                    MaxAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransientSqliteLock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqliteException sqliteException
                && sqliteException.SqliteErrorCode is 5 or 6)
            {
                return true;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        return false;
    }
}
