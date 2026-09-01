using System.Data;
using LocalProductManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public static class WarehouseSchema
{
    public static async Task UpgradeAsync(AppDbContext db)
    {
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("""
                DO $$
                BEGIN
                  IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'warehouse_shelves'
                      AND column_name = 'AisleNumber'
                  ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'warehouse_shelves'
                      AND column_name = 'RowNumber'
                  ) THEN
                    ALTER TABLE warehouse_shelves RENAME COLUMN "AisleNumber" TO "RowNumber";
                  END IF;
                END $$;
                """);
            return;
        }

        if (!db.Database.IsSqlite()) return;
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(warehouse_shelves)";
        var hasAisleNumber = false;
        var hasRowNumber = false;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                hasAisleNumber |= string.Equals(name, "AisleNumber", StringComparison.OrdinalIgnoreCase);
                hasRowNumber |= string.Equals(name, "RowNumber", StringComparison.OrdinalIgnoreCase);
            }
        }
        if (hasAisleNumber && !hasRowNumber)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE warehouse_shelves RENAME COLUMN AisleNumber TO RowNumber");
    }
}
