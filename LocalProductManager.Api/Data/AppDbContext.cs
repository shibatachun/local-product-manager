using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductListing> ProductListings => Set<ProductListing>();
    public DbSet<AmazonAsin> AmazonAsins => Set<AmazonAsin>();
    public DbSet<AmazonAttributionLink> AmazonAttributionLinks => Set<AmazonAttributionLink>();
    public DbSet<AmazonMarketingProduct> AmazonMarketingProducts => Set<AmazonMarketingProduct>();
    public DbSet<AmazonMarketingAsin> AmazonMarketingAsins => Set<AmazonMarketingAsin>();
    public DbSet<AmazonMarketingAttributionLink> AmazonMarketingAttributionLinks => Set<AmazonMarketingAttributionLink>();
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<SocialMetricSnapshot> SocialMetricSnapshots => Set<SocialMetricSnapshot>();
    public DbSet<SocialVideoMetricSnapshot> SocialVideoMetricSnapshots => Set<SocialVideoMetricSnapshot>();
    public DbSet<SocialDailyMetricDelta> SocialDailyMetricDeltas => Set<SocialDailyMetricDelta>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppUserSession> AppUserSessions => Set<AppUserSession>();
    public DbSet<VideoTrendItem> VideoTrendItems => Set<VideoTrendItem>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseShelf> WarehouseShelves => Set<WarehouseShelf>();
    public DbSet<ShelfLevel> ShelfLevels => Set<ShelfLevel>();
    public DbSet<ShelfLevelInventory> ShelfLevelInventories => Set<ShelfLevelInventory>();
    public DbSet<InventoryHistoryEntry> InventoryHistory => Set<InventoryHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.HasIndex(product => product.LocalInventoryId).IsUnique();
            entity.Property(product => product.LocalInventoryId).HasMaxLength(64);
            entity.Property(product => product.Sku).HasMaxLength(128).IsRequired();
            entity.Property(product => product.ProductName).HasMaxLength(256).IsRequired();
            entity.Property(product => product.Category).HasMaxLength(80);
            entity.Property(product => product.InventoryCategory).HasMaxLength(16).IsRequired();
            entity.Property(product => product.CategoryInventoryId).HasMaxLength(32);
            entity.HasIndex(product => product.CategoryInventoryId).IsUnique();
            entity.Property(product => product.Cost).HasPrecision(10, 2);
            entity.Property(product => product.OriginalCost).HasPrecision(10, 2);
            entity.Property(product => product.SalePrice).HasPrecision(10, 2);
            entity.Property(product => product.Weight).HasPrecision(10, 2);
            entity.Property(product => product.Length).HasPrecision(10, 2);
            entity.Property(product => product.Width).HasPrecision(10, 2);
            entity.Property(product => product.Height).HasPrecision(10, 2);
            entity.Property(product => product.ImageSource).HasMaxLength(16).IsRequired();
            entity.Property(product => product.ProductType).HasMaxLength(16).IsRequired();
            entity.Property(product => product.SkuBase).HasMaxLength(64);
            entity.Property(product => product.AmazonParentAsin).HasMaxLength(32);
            entity.HasIndex(product => product.ParentProductId);
            entity.HasOne(product => product.ParentProduct)
                .WithMany(product => product.ChildProducts)
                .HasForeignKey(product => product.ParentProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.ToTable("warehouses");
            entity.HasIndex(item => item.Code).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(160).IsRequired();
        });

        modelBuilder.Entity<WarehouseShelf>(entity =>
        {
            entity.ToTable("warehouse_shelves");
            entity.HasIndex(item => item.Code).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ZoneCode).HasMaxLength(8).IsRequired();
            entity.Property(item => item.RowNumber).HasColumnName("AisleNumber");
            entity.HasOne(item => item.Warehouse).WithMany(item => item.Shelves)
                .HasForeignKey(item => item.WarehouseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShelfLevel>(entity =>
        {
            entity.ToTable("shelf_levels");
            entity.HasIndex(item => new { item.WarehouseShelfId, item.LevelNumber }).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(80).IsRequired();
            entity.HasOne(item => item.WarehouseShelf).WithMany(item => item.Levels)
                .HasForeignKey(item => item.WarehouseShelfId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShelfLevelInventory>(entity =>
        {
            entity.ToTable("shelf_level_inventory");
            entity.HasIndex(item => new { item.ShelfLevelId, item.BinNumber, item.ProductId }).IsUnique();
            entity.HasOne(item => item.ShelfLevel).WithMany(item => item.Inventory)
                .HasForeignKey(item => item.ShelfLevelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Product).WithMany()
                .HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductListing>(entity =>
        {
            entity.ToTable("product_listings");
            entity.HasIndex(listing => new { listing.ProductId, listing.Platform }).IsUnique();
            entity.Property(listing => listing.Platform).HasMaxLength(32).IsRequired();
            entity.Property(listing => listing.ListingUrl).HasMaxLength(2048);
            entity.HasOne(listing => listing.Product)
                .WithMany(product => product.Listings)
                .HasForeignKey(listing => listing.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AmazonAsin>(entity =>
        {
            entity.ToTable("amazon_asins");
            entity.HasIndex(asin => new { asin.ProductId, asin.Asin }).IsUnique();
            entity.Property(asin => asin.Asin).HasMaxLength(32).IsRequired();
            entity.Property(asin => asin.AsinType).HasMaxLength(16).IsRequired();
            entity.Property(asin => asin.ParentAsin).HasMaxLength(32);
            entity.HasOne(asin => asin.Product)
                .WithMany(product => product.AmazonAsins)
                .HasForeignKey(asin => asin.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AmazonAttributionLink>(entity =>
        {
            entity.ToTable("amazon_attribution_links");
            entity.HasIndex(link => new { link.ProductId, link.AmazonAsinId, link.Platform }).IsUnique();
            entity.Property(link => link.Platform).HasMaxLength(32).IsRequired();
            entity.Property(link => link.AttributionUrl).HasMaxLength(4096).IsRequired();
            entity.Property(link => link.ImportedFileName).HasMaxLength(256);
            entity.HasOne(link => link.Product)
                .WithMany(product => product.AmazonAttributionLinks)
                .HasForeignKey(link => link.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.AmazonAsin)
                .WithMany(asin => asin.AttributionLinks)
                .HasForeignKey(link => link.AmazonAsinId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InventoryHistoryEntry>(entity =>
        {
            entity.ToTable("inventory_history");
            entity.HasIndex(item => item.CreatedAt);
            entity.HasIndex(item => item.ProductId);
            entity.HasIndex(item => item.ShelfLevelId);
            entity.Property(item => item.LocalInventoryId).HasMaxLength(64);
            entity.Property(item => item.Sku).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ProductName).HasMaxLength(256).IsRequired();
            entity.Property(item => item.LocationCode).HasMaxLength(96).IsRequired();
            entity.Property(item => item.LocationDescription).HasMaxLength(256).IsRequired();
            entity.Property(item => item.Operation).HasMaxLength(32).IsRequired();
            entity.Property(item => item.UserName).HasMaxLength(80).IsRequired();
            entity.Property(item => item.UserRole).HasMaxLength(32).IsRequired();
            entity.Property(item => item.RemoteIp).HasMaxLength(80);
            entity.Property(item => item.UserAgent).HasMaxLength(512);
            entity.Property(item => item.Source).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AmazonMarketingProduct>(entity =>
        {
            entity.ToTable("amazon_marketing_products");
            entity.HasIndex(item => item.Sku).IsUnique();
            entity.Property(item => item.Sku).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ProductName).HasMaxLength(256).IsRequired();
            entity.Property(item => item.ImageUrl).HasMaxLength(2048);
            entity.Property(item => item.ProductType).HasMaxLength(16).IsRequired();
            entity.HasOne(item => item.ParentProduct).WithMany(item => item.ChildProducts)
                .HasForeignKey(item => item.ParentProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AmazonMarketingAsin>(entity =>
        {
            entity.ToTable("amazon_marketing_asins");
            entity.HasIndex(item => new { item.ProductId, item.Asin }).IsUnique();
            entity.HasOne(item => item.Product).WithMany(item => item.Asins)
                .HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AmazonMarketingAttributionLink>(entity =>
        {
            entity.ToTable("amazon_marketing_attribution_links");
            entity.HasIndex(item => new { item.ProductId, item.AmazonAsinId, item.Platform }).IsUnique();
            entity.HasOne(item => item.Product).WithMany(item => item.AttributionLinks)
                .HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.AmazonAsin).WithMany()
                .HasForeignKey(item => item.AmazonAsinId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SocialAccount>(entity =>
        {
            entity.ToTable("social_accounts");
            entity.Property(account => account.Platform).HasMaxLength(32).IsRequired();
            entity.Property(account => account.AccountName).HasMaxLength(160).IsRequired();
            entity.Property(account => account.AccountHandle).HasMaxLength(160);
            entity.Property(account => account.AccountUrl).HasMaxLength(2048);
            entity.Property(account => account.Color).HasMaxLength(16);
        });

        modelBuilder.Entity<SocialMetricSnapshot>(entity =>
        {
            entity.ToTable("social_metric_snapshots");
            entity.HasIndex(metric => new { metric.SocialAccountId, metric.SnapshotDate }).IsUnique();
            entity.HasOne(metric => metric.SocialAccount)
                .WithMany(account => account.Metrics)
                .HasForeignKey(metric => metric.SocialAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SocialVideoMetricSnapshot>(entity =>
        {
            entity.ToTable("social_video_metric_snapshots");
            entity.HasIndex(metric => new { metric.SocialAccountId, metric.PlatformVideoId, metric.SnapshotDate }).IsUnique();
            entity.Property(metric => metric.PlatformVideoId).HasMaxLength(128).IsRequired();
            entity.Property(metric => metric.Title).HasMaxLength(512).IsRequired();
            entity.Property(metric => metric.ThumbnailUrl).HasMaxLength(2048);
            entity.HasOne(metric => metric.SocialAccount)
                .WithMany()
                .HasForeignKey(metric => metric.SocialAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SocialDailyMetricDelta>(entity =>
        {
            entity.ToTable("social_daily_metric_deltas");
            entity.HasIndex(metric => new { metric.SocialAccountId, metric.Scope, metric.MetricKey, metric.SnapshotDate }).IsUnique();
            entity.Property(metric => metric.Scope).HasMaxLength(32).IsRequired();
            entity.Property(metric => metric.MetricKey).HasMaxLength(160).IsRequired();
            entity.Property(metric => metric.Title).HasMaxLength(512).IsRequired();
            entity.HasOne(metric => metric.SocialAccount)
                .WithMany()
                .HasForeignKey(metric => metric.SocialAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_users");
            entity.HasIndex(user => user.UserName).IsUnique();
            entity.Property(user => user.UserName).HasMaxLength(80).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(128).IsRequired();
            entity.Property(user => user.Role).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AppUserSession>(entity =>
        {
            entity.ToTable("app_user_sessions");
            entity.Property(session => session.Id).HasMaxLength(64).IsRequired();
            entity.Property(session => session.UserName).HasMaxLength(80).IsRequired();
            entity.Property(session => session.Role).HasMaxLength(32).IsRequired();
            entity.Property(session => session.RemoteIp).HasMaxLength(80);
            entity.HasOne(session => session.AppUser)
                .WithMany()
                .HasForeignKey(session => session.AppUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<VideoTrendItem>(entity =>
        {
            entity.ToTable("video_trend_items");
            entity.Property(item => item.Platform).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Url).HasMaxLength(2048).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(2000);
            entity.Property(item => item.Author).HasMaxLength(160);
            entity.Property(item => item.PlatformVideoId).HasMaxLength(160);
            entity.Property(item => item.EmbedUrl).HasMaxLength(2048);
            entity.Property(item => item.ThumbnailUrl).HasMaxLength(2048);
            entity.Property(item => item.Tags).HasMaxLength(500);
            entity.HasIndex(item => item.Platform);
        });
    }
}
