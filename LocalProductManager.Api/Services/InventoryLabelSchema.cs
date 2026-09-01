using LocalProductManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public static class InventoryLabelSchema
{
    public static async Task UpgradeAsync(AppDbContext db)
    {
        var sql = db.Database.IsNpgsql()
            ? """
              DROP VIEW IF EXISTS bartender_inventory_products;
              CREATE VIEW bartender_inventory_products AS
              SELECT
                'LPM|' || COALESCE(NULLIF(product."LocalInventoryId", ''), 'INV-' || product."Id"::text) AS "UUID",
                COALESCE(NULLIF(product."LocalInventoryId", ''), 'INV-' || product."Id"::text) AS "Inventory ID",
                COALESCE(NULLIF(product."CategoryInventoryId", ''), product."InventoryCategory", 'UNKNOWN') AS "Category ID",
                product."Sku" AS "SKU",
                product."ProductName" AS "Title",
                product."Qty" AS "Inventory",
                ROUND(product."Cost", 2) AS "Cost",
                ROUND(product."Weight", 2) AS "Weight (oz)",
                COALESCE(string_agg(
                  DISTINCT warehouse."Code" || '-' || shelf."ZoneCode" || '-' ||
                  LPAD(shelf."RowNumber"::text, 2, '0') || '-R' || LPAD(shelf."RackNumber"::text, 3, '0') ||
                  '-L' || LPAD(level."LevelNumber"::text, 2, '0') || '-B' || LPAD(stock."BinNumber"::text, 2, '0'),
                  ', '
                ) FILTER (WHERE stock."Qty" > 0), '') AS "Warehouse Location"
              FROM products AS product
              LEFT JOIN shelf_level_inventory AS stock ON stock."ProductId" = product."Id"
              LEFT JOIN shelf_levels AS level ON level."Id" = stock."ShelfLevelId"
              LEFT JOIN warehouse_shelves AS shelf ON shelf."Id" = level."WarehouseShelfId"
              LEFT JOIN warehouses AS warehouse ON warehouse."Id" = shelf."WarehouseId"
              GROUP BY product."Id", product."LocalInventoryId", product."CategoryInventoryId", product."InventoryCategory",
                       product."Sku", product."ProductName", product."Qty", product."Cost", product."Weight";
              DO $$
              BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'lpm_bartender') THEN
                  GRANT USAGE ON SCHEMA public TO lpm_bartender;
                  GRANT SELECT ON TABLE public.bartender_bid_products, public.bartender_inventory_products TO lpm_bartender;
                END IF;
              END $$;
              """
            : """
              DROP VIEW IF EXISTS bartender_inventory_products;
              CREATE VIEW bartender_inventory_products AS
              SELECT
                'LPM|' || COALESCE(NULLIF(product.LocalInventoryId, ''), 'INV-' || product.Id) AS "UUID",
                COALESCE(NULLIF(product.LocalInventoryId, ''), 'INV-' || product.Id) AS "Inventory ID",
                COALESCE(NULLIF(product.CategoryInventoryId, ''), product.InventoryCategory, 'UNKNOWN') AS "Category ID",
                product.Sku AS "SKU",
                product.ProductName AS "Title",
                product.Qty AS "Inventory",
                ROUND(product.Cost, 2) AS "Cost",
                ROUND(product.Weight, 2) AS "Weight (oz)",
                COALESCE(group_concat(
                  CASE WHEN stock.Qty > 0 THEN warehouse.Code || '-' || shelf.ZoneCode || '-' ||
                    printf('%02d', shelf.RowNumber) || '-R' || printf('%03d', shelf.RackNumber) ||
                    '-L' || printf('%02d', level.LevelNumber) || '-B' || printf('%02d', stock.BinNumber) END,
                  ', '
                ), '') AS "Warehouse Location"
              FROM products AS product
              LEFT JOIN shelf_level_inventory AS stock ON stock.ProductId = product.Id
              LEFT JOIN shelf_levels AS level ON level.Id = stock.ShelfLevelId
              LEFT JOIN warehouse_shelves AS shelf ON shelf.Id = level.WarehouseShelfId
              LEFT JOIN warehouses AS warehouse ON warehouse.Id = shelf.WarehouseId
              GROUP BY product.Id, product.LocalInventoryId, product.CategoryInventoryId, product.InventoryCategory,
                       product.Sku, product.ProductName, product.Qty, product.Cost, product.Weight;
              """;

        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
