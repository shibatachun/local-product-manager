using LocalProductManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public static class WarehouseSchema
{
    public static async Task UpgradeAsync(AppDbContext db)
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
    }
}
