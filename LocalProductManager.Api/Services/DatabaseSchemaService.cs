using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class DatabaseSchemaService
{
    public static void Upgrade(DbContext db)
    {
        var columns = GetColumns(db, "products");
        if (!columns.Contains("ParentProductId"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN ParentProductId INTEGER NULL");
        }
        if (!columns.Contains("ProductType"))
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE products ADD COLUMN ProductType TEXT NOT NULL DEFAULT 'standalone'");
        }
        if (!columns.Contains("SkuBase"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN SkuBase TEXT NULL");
        }
        if (!columns.Contains("AmazonParentAsin"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN AmazonParentAsin TEXT NULL");
        }
        if (!columns.Contains("ImageSource"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN ImageSource TEXT NOT NULL DEFAULT 'local'");
        }
        if (!columns.Contains("Category"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN Category TEXT NULL");
        }
        if (!columns.Contains("LocalInventoryId"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN LocalInventoryId TEXT NULL");
            db.Database.ExecuteSqlRaw("UPDATE products SET LocalInventoryId = 'INV-' || Id WHERE LocalInventoryId IS NULL OR trim(LocalInventoryId) = ''");
        }
        if (!columns.Contains("OriginalCost"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN OriginalCost TEXT NULL");
        }
        if (!columns.Contains("SalePrice"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN SalePrice TEXT NULL");
        }
        if (!columns.Contains("Qty"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN Qty INTEGER NOT NULL DEFAULT 0");
        }
        if (!columns.Contains("Weight"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN Weight TEXT NULL");
        }
        if (!columns.Contains("Length"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN Length TEXT NULL");
        }
        if (!columns.Contains("Width"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN Width TEXT NULL");
        }
        if (!columns.Contains("Height"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE products ADD COLUMN Height TEXT NULL");
        }
        db.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_products_ParentProductId ON products (ParentProductId)");
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_products_LocalInventoryId ON products (LocalInventoryId)");
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS warehouses (
                Id INTEGER NOT NULL CONSTRAINT PK_warehouses PRIMARY KEY AUTOINCREMENT,
                Code TEXT NOT NULL,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_warehouses_Code ON warehouses (Code);
            CREATE TABLE IF NOT EXISTS warehouse_shelves (
                Id INTEGER NOT NULL CONSTRAINT PK_warehouse_shelves PRIMARY KEY AUTOINCREMENT,
                WarehouseId INTEGER NOT NULL,
                Code TEXT NOT NULL,
                ZoneCode TEXT NOT NULL,
                AisleNumber INTEGER NOT NULL,
                RackNumber INTEGER NOT NULL,
                LevelCount INTEGER NOT NULL,
                X TEXT NOT NULL,
                Y TEXT NOT NULL,
                Width TEXT NOT NULL,
                Height TEXT NOT NULL,
                Rotation INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                CONSTRAINT FK_warehouse_shelves_warehouses_WarehouseId FOREIGN KEY (WarehouseId) REFERENCES warehouses (Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_warehouse_shelves_Code ON warehouse_shelves (Code);
            CREATE INDEX IF NOT EXISTS IX_warehouse_shelves_WarehouseId ON warehouse_shelves (WarehouseId);
            CREATE TABLE IF NOT EXISTS shelf_levels (
                Id INTEGER NOT NULL CONSTRAINT PK_shelf_levels PRIMARY KEY AUTOINCREMENT,
                WarehouseShelfId INTEGER NOT NULL,
                LevelNumber INTEGER NOT NULL,
                Code TEXT NOT NULL,
                CONSTRAINT FK_shelf_levels_warehouse_shelves_WarehouseShelfId FOREIGN KEY (WarehouseShelfId) REFERENCES warehouse_shelves (Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_shelf_levels_WarehouseShelfId_LevelNumber ON shelf_levels (WarehouseShelfId, LevelNumber);
            CREATE TABLE IF NOT EXISTS shelf_level_inventory (
                Id INTEGER NOT NULL CONSTRAINT PK_shelf_level_inventory PRIMARY KEY AUTOINCREMENT,
                ShelfLevelId INTEGER NOT NULL,
                BinNumber INTEGER NOT NULL DEFAULT 1,
                ProductId INTEGER NOT NULL,
                Qty INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CONSTRAINT FK_shelf_level_inventory_shelf_levels_ShelfLevelId FOREIGN KEY (ShelfLevelId) REFERENCES shelf_levels (Id) ON DELETE CASCADE,
                CONSTRAINT FK_shelf_level_inventory_products_ProductId FOREIGN KEY (ProductId) REFERENCES products (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_shelf_level_inventory_ProductId ON shelf_level_inventory (ProductId);
            """);
        var inventoryColumns = GetColumns(db, "shelf_level_inventory");
        if (inventoryColumns.Count > 0 && !inventoryColumns.Contains("BinNumber"))
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE shelf_level_inventory ADD COLUMN BinNumber INTEGER NOT NULL DEFAULT 1");
        }
        db.Database.ExecuteSqlRaw(
            "DROP INDEX IF EXISTS IX_shelf_level_inventory_ShelfLevelId_ProductId");
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_shelf_level_inventory_ShelfLevelId_BinNumber_ProductId ON shelf_level_inventory (ShelfLevelId, BinNumber, ProductId)");
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS inventory_history (
                Id INTEGER NOT NULL CONSTRAINT PK_inventory_history PRIMARY KEY AUTOINCREMENT,
                ProductId INTEGER NOT NULL,
                LocalInventoryId TEXT NULL,
                Sku TEXT NOT NULL,
                ProductName TEXT NOT NULL,
                ShelfLevelId INTEGER NOT NULL,
                BinNumber INTEGER NOT NULL,
                LocationCode TEXT NOT NULL,
                LocationDescription TEXT NOT NULL,
                Operation TEXT NOT NULL,
                BeforeQty INTEGER NOT NULL,
                AfterQty INTEGER NOT NULL,
                Delta INTEGER NOT NULL,
                UserName TEXT NOT NULL,
                UserRole TEXT NOT NULL,
                RemoteIp TEXT NULL,
                UserAgent TEXT NULL,
                Source TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_inventory_history_CreatedAt ON inventory_history (CreatedAt);
            CREATE INDEX IF NOT EXISTS IX_inventory_history_ProductId ON inventory_history (ProductId);
            CREATE INDEX IF NOT EXISTS IX_inventory_history_ShelfLevelId ON inventory_history (ShelfLevelId);
            """);
        var shelfColumns = GetColumns(db, "warehouse_shelves");
        if (shelfColumns.Count > 0 && !shelfColumns.Contains("Rotation"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE warehouse_shelves ADD COLUMN Rotation INTEGER NOT NULL DEFAULT 0");
        }
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS social_accounts (
                Id INTEGER NOT NULL CONSTRAINT PK_social_accounts PRIMARY KEY AUTOINCREMENT,
                Platform TEXT NOT NULL,
                AccountName TEXT NOT NULL,
                AccountHandle TEXT NULL,
                AccountUrl TEXT NULL,
                Color TEXT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS social_metric_snapshots (
                Id INTEGER NOT NULL CONSTRAINT PK_social_metric_snapshots PRIMARY KEY AUTOINCREMENT,
                SocialAccountId INTEGER NOT NULL,
                SnapshotDate TEXT NOT NULL,
                Clicks INTEGER NOT NULL,
                Likes INTEGER NOT NULL,
                Views INTEGER NOT NULL,
                Comments INTEGER NOT NULL,
                Shares INTEGER NOT NULL,
                Followers INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CONSTRAINT FK_social_metric_snapshots_social_accounts_SocialAccountId
                    FOREIGN KEY (SocialAccountId) REFERENCES social_accounts (Id) ON DELETE CASCADE
            )
            """);
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_social_metric_snapshots_SocialAccountId_SnapshotDate ON social_metric_snapshots (SocialAccountId, SnapshotDate)");
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS social_video_metric_snapshots (
                Id INTEGER NOT NULL CONSTRAINT PK_social_video_metric_snapshots PRIMARY KEY AUTOINCREMENT,
                SocialAccountId INTEGER NOT NULL,
                PlatformVideoId TEXT NOT NULL,
                Title TEXT NOT NULL,
                ThumbnailUrl TEXT NULL,
                PublishedAt TEXT NULL,
                SnapshotDate TEXT NOT NULL,
                Clicks INTEGER NOT NULL,
                Views INTEGER NOT NULL,
                Likes INTEGER NOT NULL,
                Comments INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CONSTRAINT FK_social_video_metric_snapshots_social_accounts_SocialAccountId
                    FOREIGN KEY (SocialAccountId) REFERENCES social_accounts (Id) ON DELETE CASCADE
            )
            """);
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_social_video_metric_snapshots_SocialAccountId_PlatformVideoId_SnapshotDate ON social_video_metric_snapshots (SocialAccountId, PlatformVideoId, SnapshotDate)");
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS social_daily_metric_deltas (
                Id INTEGER NOT NULL CONSTRAINT PK_social_daily_metric_deltas PRIMARY KEY AUTOINCREMENT,
                SocialAccountId INTEGER NOT NULL,
                Scope TEXT NOT NULL,
                MetricKey TEXT NOT NULL,
                Title TEXT NOT NULL,
                SnapshotDate TEXT NOT NULL,
                Clicks INTEGER NOT NULL,
                Likes INTEGER NOT NULL,
                Views INTEGER NOT NULL,
                Comments INTEGER NOT NULL,
                Shares INTEGER NOT NULL,
                Followers INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CONSTRAINT FK_social_daily_metric_deltas_social_accounts_SocialAccountId
                    FOREIGN KEY (SocialAccountId) REFERENCES social_accounts (Id) ON DELETE CASCADE
            )
            """);
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_social_daily_metric_deltas_SocialAccountId_Scope_MetricKey_SnapshotDate ON social_daily_metric_deltas (SocialAccountId, Scope, MetricKey, SnapshotDate)");
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS app_users (
                Id INTEGER NOT NULL CONSTRAINT PK_app_users PRIMARY KEY AUTOINCREMENT,
                UserName TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                CanManageStorage INTEGER NOT NULL,
                CanEditAttribution INTEGER NOT NULL,
                CanManageSocial INTEGER NOT NULL,
                CanImportData INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);
        db.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_app_users_UserName ON app_users (UserName)");
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS app_user_sessions (
                Id TEXT NOT NULL CONSTRAINT PK_app_user_sessions PRIMARY KEY,
                AppUserId INTEGER NULL,
                UserName TEXT NOT NULL,
                Role TEXT NOT NULL,
                RemoteIp TEXT NULL,
                IsHost INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                LastSeenAt TEXT NOT NULL,
                CONSTRAINT FK_app_user_sessions_app_users_AppUserId
                    FOREIGN KEY (AppUserId) REFERENCES app_users (Id) ON DELETE SET NULL
            )
            """);
        db.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_app_user_sessions_AppUserId ON app_user_sessions (AppUserId)");
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS video_trend_items (
                Id INTEGER NOT NULL CONSTRAINT PK_video_trend_items PRIMARY KEY AUTOINCREMENT,
                Platform TEXT NOT NULL,
                Url TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NULL,
                Author TEXT NULL,
                PlatformVideoId TEXT NULL,
                EmbedUrl TEXT NULL,
                ThumbnailUrl TEXT NULL,
                Tags TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )
            """);
        db.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_video_trend_items_Platform ON video_trend_items (Platform)");

        CreateAmazonMarketingSchema(db);
        SeparateAmazonMarketingData(db);
        RemoveDemoSocialData(db);
    }

    private static void CreateAmazonMarketingSchema(DbContext db)
    {
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS app_schema_state (
                Key TEXT NOT NULL PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS amazon_marketing_products (
                Id INTEGER NOT NULL PRIMARY KEY,
                Sku TEXT NOT NULL,
                ProductName TEXT NOT NULL,
                ImageUrl TEXT NULL,
                ProductType TEXT NOT NULL,
                SkuBase TEXT NULL,
                AmazonParentAsin TEXT NULL,
                ParentProductId INTEGER NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CONSTRAINT FK_amazon_marketing_products_parent
                    FOREIGN KEY (ParentProductId) REFERENCES amazon_marketing_products (Id) ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_amazon_marketing_products_Sku ON amazon_marketing_products (Sku);
            CREATE TABLE IF NOT EXISTS amazon_marketing_asins (
                Id INTEGER NOT NULL PRIMARY KEY,
                ProductId INTEGER NOT NULL,
                Asin TEXT NOT NULL,
                AsinType TEXT NOT NULL,
                ParentAsin TEXT NULL,
                AmazonAttributeDone INTEGER NOT NULL,
                CONSTRAINT FK_amazon_marketing_asins_product
                    FOREIGN KEY (ProductId) REFERENCES amazon_marketing_products (Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_amazon_marketing_asins_ProductId_Asin
                ON amazon_marketing_asins (ProductId, Asin);
            CREATE TABLE IF NOT EXISTS amazon_marketing_attribution_links (
                Id INTEGER NOT NULL PRIMARY KEY,
                ProductId INTEGER NOT NULL,
                AmazonAsinId INTEGER NULL,
                Platform TEXT NOT NULL,
                AttributionUrl TEXT NOT NULL,
                ImportedFileName TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CONSTRAINT FK_amazon_marketing_links_product
                    FOREIGN KEY (ProductId) REFERENCES amazon_marketing_products (Id) ON DELETE CASCADE,
                CONSTRAINT FK_amazon_marketing_links_asin
                    FOREIGN KEY (AmazonAsinId) REFERENCES amazon_marketing_asins (Id) ON DELETE SET NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_amazon_marketing_links_ProductId_Asin_Platform
                ON amazon_marketing_attribution_links (ProductId, AmazonAsinId, Platform);
            """);
    }

    private static void SeparateAmazonMarketingData(DbContext db)
    {
        const string migrationKey = "separate-amazon-marketing-v1";
        using var check = db.Database.GetDbConnection().CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM app_schema_state WHERE Key = $key";
        var parameter = check.CreateParameter();
        parameter.ParameterName = "$key";
        parameter.Value = migrationKey;
        check.Parameters.Add(parameter);
        if (Convert.ToInt32(check.ExecuteScalar()) > 0) return;

        using var transaction = db.Database.BeginTransaction();
        db.Database.ExecuteSqlRaw(
            """
            CREATE TEMP TABLE amazon_migration_ids AS
            SELECT p.Id,
                CASE WHEN EXISTS (
                    SELECT 1 FROM product_listings other
                    WHERE other.ProductId = p.Id AND lower(other.Platform) <> 'amazon'
                ) THEN 1 ELSE 0 END AS HasNonAmazon
            FROM products p
            WHERE EXISTS (SELECT 1 FROM product_listings l WHERE l.ProductId = p.Id AND lower(l.Platform) = 'amazon')
               OR EXISTS (SELECT 1 FROM amazon_asins a WHERE a.ProductId = p.Id)
               OR EXISTS (SELECT 1 FROM amazon_attribution_links x WHERE x.ProductId = p.Id);

            INSERT INTO amazon_marketing_products
                (Id, Sku, ProductName, ImageUrl, ProductType, SkuBase, AmazonParentAsin, ParentProductId, CreatedAt, UpdatedAt)
            SELECT p.Id, p.Sku, p.ProductName, p.ImagePath, p.ProductType, p.SkuBase, p.AmazonParentAsin,
                CASE WHEN parent.Id IS NOT NULL THEN p.ParentProductId ELSE NULL END,
                p.CreatedAt, p.UpdatedAt
            FROM products p
            JOIN amazon_migration_ids ids ON ids.Id = p.Id
            LEFT JOIN amazon_migration_ids parent ON parent.Id = p.ParentProductId;

            INSERT INTO amazon_marketing_asins
                (Id, ProductId, Asin, AsinType, ParentAsin, AmazonAttributeDone)
            SELECT a.Id, a.ProductId, a.Asin, a.AsinType, a.ParentAsin, a.AmazonAttributeDone
            FROM amazon_asins a JOIN amazon_migration_ids ids ON ids.Id = a.ProductId;

            INSERT INTO amazon_marketing_attribution_links
                (Id, ProductId, AmazonAsinId, Platform, AttributionUrl, ImportedFileName, CreatedAt, UpdatedAt)
            SELECT x.Id, x.ProductId, x.AmazonAsinId, x.Platform, x.AttributionUrl,
                x.ImportedFileName, x.CreatedAt, x.UpdatedAt
            FROM amazon_attribution_links x JOIN amazon_migration_ids ids ON ids.Id = x.ProductId;
            """);

        var oldLinks = db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM amazon_attribution_links").Single();
        var newLinks = db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM amazon_marketing_attribution_links").Single();
        if (oldLinks != newLinks)
            throw new InvalidOperationException($"Amazon attribution migration validation failed: {oldLinks} source links, {newLinks} copied links.");

        db.Database.ExecuteSqlRaw(
            """
            DELETE FROM shelf_level_inventory;
            UPDATE products SET Qty = 0, UpdatedAt = CURRENT_TIMESTAMP WHERE Qty <> 0;
            DELETE FROM amazon_attribution_links;
            DELETE FROM amazon_asins;
            DELETE FROM product_listings WHERE lower(Platform) = 'amazon';
            UPDATE products SET ParentProductId = NULL
            WHERE ParentProductId IN (SELECT Id FROM amazon_migration_ids WHERE HasNonAmazon = 0);
            DELETE FROM products
            WHERE Id IN (SELECT Id FROM amazon_migration_ids WHERE HasNonAmazon = 0);
            INSERT INTO app_schema_state (Key, AppliedAt) VALUES ('separate-amazon-marketing-v1', CURRENT_TIMESTAMP);
            DROP TABLE amazon_migration_ids;
            """);
        transaction.Commit();
    }

    private static void RemoveDemoSocialData(DbContext db)
    {
        const string demoAccountFilter =
            """
            SELECT Id
            FROM social_accounts
            WHERE lower(AccountName) LIKE 'demo %'
                OR lower(AccountName) LIKE '% demo %'
                OR lower(AccountHandle) LIKE '@demo%'
                OR lower(AccountHandle) LIKE 'demo%'
                OR lower(AccountUrl) LIKE '%/demo%'
            """;

        db.Database.ExecuteSqlRaw(
            $"""
            DELETE FROM social_video_metric_snapshots
            WHERE SocialAccountId IN ({demoAccountFilter})
            """);
        db.Database.ExecuteSqlRaw(
            $"""
            DELETE FROM social_metric_snapshots
            WHERE SocialAccountId IN ({demoAccountFilter})
            """);
        db.Database.ExecuteSqlRaw(
            $"""
            DELETE FROM social_daily_metric_deltas
            WHERE SocialAccountId IN ({demoAccountFilter})
            """);
        db.Database.ExecuteSqlRaw(
            $"""
            DELETE FROM social_accounts
            WHERE Id IN ({demoAccountFilter})
            """);
    }

    private static HashSet<string> GetColumns(DbContext db, string tableName)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName.Replace("'", "''")}')";
        db.Database.OpenConnection();
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

}
