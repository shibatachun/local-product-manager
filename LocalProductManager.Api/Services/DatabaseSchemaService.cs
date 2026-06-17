using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class DatabaseSchemaService
{
    public static void Upgrade(DbContext db)
    {
        var columns = GetProductColumns(db);
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
    }

    private static HashSet<string> GetProductColumns(DbContext db)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('products')";
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
