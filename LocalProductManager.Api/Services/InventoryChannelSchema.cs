using LocalProductManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public static class InventoryChannelSchema
{
    public static async Task UpgradeAsync(AppDbContext db)
    {
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE shelf_level_inventory
                ADD COLUMN IF NOT EXISTS "InventoryChannel" varchar(16) NOT NULL DEFAULT 'general'
                """);
            return;
        }

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(shelf_level_inventory)";
            var exists = false;
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    exists |= string.Equals(reader.GetString(1), "InventoryChannel", StringComparison.OrdinalIgnoreCase);
            }
            if (!exists)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE shelf_level_inventory ADD COLUMN InventoryChannel TEXT NOT NULL DEFAULT 'general'");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
