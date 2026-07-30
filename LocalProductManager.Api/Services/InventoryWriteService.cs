using LocalProductManager.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class InventoryWriteService(AppDbContext db, ILogger<InventoryWriteService> logger)
{
    private const int MaxAttempts = 3;

    public Task SetAsync(
        int levelId,
        int binNumber,
        int productId,
        int qty,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;

            if (qty == 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM shelf_level_inventory
                    WHERE ShelfLevelId = {levelId}
                      AND BinNumber = {binNumber}
                      AND ProductId = {productId}
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

            await UpdateProductTotalAsync(productId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> AdjustAsync(
        int levelId,
        int binNumber,
        int productId,
        int delta,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;

            int affected;
            if (delta > 0)
            {
                affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO shelf_level_inventory
                        (ShelfLevelId, BinNumber, ProductId, Qty, UpdatedAt)
                    VALUES ({levelId}, {binNumber}, {productId}, {delta}, {now})
                    ON CONFLICT(ShelfLevelId, BinNumber, ProductId)
                    DO UPDATE SET
                        Qty = shelf_level_inventory.Qty + excluded.Qty,
                        UpdatedAt = excluded.UpdatedAt
                    """, cancellationToken);
            }
            else
            {
                affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE shelf_level_inventory
                    SET Qty = Qty + {delta}, UpdatedAt = {now}
                    WHERE ShelfLevelId = {levelId}
                      AND BinNumber = {binNumber}
                      AND ProductId = {productId}
                      AND Qty >= {-delta}
                    """, cancellationToken);
            }

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM shelf_level_inventory
                WHERE ShelfLevelId = {levelId}
                  AND BinNumber = {binNumber}
                  AND ProductId = {productId}
                  AND Qty = 0
                """, cancellationToken);

            await UpdateProductTotalAsync(productId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private Task<int> UpdateProductTotalAsync(
        int productId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
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
