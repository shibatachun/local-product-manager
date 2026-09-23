using LocalProductManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public static class ProductListingPriceSchema
{
    public static Task UpgradeAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE product_listings ADD COLUMN IF NOT EXISTS \"RetailPrice\" numeric(10,2);");
}
