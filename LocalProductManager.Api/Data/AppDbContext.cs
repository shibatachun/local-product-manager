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
    public DbSet<ProductDetailProfile> ProductDetails => Set<ProductDetailProfile>();
    public DbSet<ProductIdentifier> ProductIdentifiers => Set<ProductIdentifier>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>();
    public DbSet<ProcurementLink> ProcurementLinks => Set<ProcurementLink>();
    public DbSet<ProductCompliance> ProductCompliance => Set<ProductCompliance>();
    public DbSet<ProductPackaging> ProductPackaging => Set<ProductPackaging>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<LiveStreamBidProduct> LiveStreamBidProducts => Set<LiveStreamBidProduct>();
    public DbSet<ImportedBidBatch> ImportedBidBatches => Set<ImportedBidBatch>();
    public DbSet<LiveStreamBidShowSession> LiveStreamBidShowSessions => Set<LiveStreamBidShowSession>();
    public DbSet<LiveStreamBidShowOrder> LiveStreamBidShowOrders => Set<LiveStreamBidShowOrder>();
    public DbSet<LiveStreamBidManualCost> LiveStreamBidManualCosts => Set<LiveStreamBidManualCost>();
    public DbSet<LiveStreamBidShowAttachment> LiveStreamBidShowAttachments => Set<LiveStreamBidShowAttachment>();
    public DbSet<LiveStreamBidSale> LiveStreamBidSales => Set<LiveStreamBidSale>();
    public DbSet<DomesticProductInfo> DomesticProductInfos => Set<DomesticProductInfo>();

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
            entity.Property(product => product.Note).HasMaxLength(4000);
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
            entity.Property(item => item.InventoryChannel).HasMaxLength(16).HasDefaultValue("general").IsRequired();
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
            entity.Property(listing => listing.RetailPrice).HasPrecision(10, 2);
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

        modelBuilder.Entity<LiveStreamBidProduct>(entity =>
        {
            entity.ToTable("live_stream_bid_products");
            entity.HasIndex(item => item.Uuid).IsUnique();
            entity.HasIndex(item => item.SourceProductId);
            entity.HasIndex(item => new { item.BatchNumber, item.BatchProductNumber }).IsUnique();
            entity.HasOne(item => item.SourceProduct).WithMany().HasForeignKey(item => item.SourceProductId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(item => item.Uuid).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ImagePath).HasMaxLength(2000);
            entity.Property(item => item.BatchNumber).HasMaxLength(240);
            entity.Property(item => item.ActualSku).HasMaxLength(200);
            entity.Property(item => item.WhatnotAuctionSafePrice).HasPrecision(12, 2);
            entity.Property(item => item.ItemCode).HasMaxLength(20);
            entity.Property(item => item.Title).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.UnitPrice).HasPrecision(12, 2);
            entity.Property(item => item.WeightGrams).HasPrecision(12, 4);
            entity.Property(item => item.WeightOunces).HasPrecision(12, 4);
            entity.Property(item => item.Material).HasMaxLength(300);
            entity.Property(item => item.TotalCost).HasPrecision(12, 2);
            entity.Property(item => item.ImportedFileName).HasMaxLength(260);
            entity.Property(item => item.ImportedFileSha256).HasMaxLength(64);
            entity.HasIndex(item => item.ImportedFileSha256);
        });

        modelBuilder.Entity<DomesticProductInfo>(entity =>
        {
            entity.ToTable("domestic_product_infos");
            entity.HasIndex(item => item.Sku).IsUnique();
            entity.HasIndex(item => item.ProductCode);
            entity.HasIndex(item => item.ProductName);
            entity.Property(item => item.Sku).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ProductCode).HasMaxLength(300);
            entity.Property(item => item.ProductName).HasMaxLength(2000);
            entity.Property(item => item.Material).HasMaxLength(500);
            entity.Property(item => item.Specification).HasMaxLength(500);
            entity.Property(item => item.CostRmb).HasPrecision(14, 4);
            entity.Property(item => item.MinimumSalePriceUsd).HasPrecision(14, 4);
            entity.Property(item => item.WeightGrams).HasPrecision(14, 4);
            entity.Property(item => item.SupplierName).HasMaxLength(500);
            entity.Property(item => item.ChineseDeclarationName).HasMaxLength(1000);
            entity.Property(item => item.EnglishDeclarationName).HasMaxLength(1000);
            entity.Property(item => item.ImagePath).HasMaxLength(2000);
            entity.Property(item => item.SourceFileName).HasMaxLength(260);
            if (Database.IsNpgsql())
                entity.Property<string>("SearchText").HasComputedColumnSql(
                    "lower(\"Sku\" || ' ' || coalesce(\"ProductCode\", '') || ' ' || " +
                    "coalesce(\"ProductName\", '') || ' ' || coalesce(\"Material\", '') || ' ' || " +
                    "coalesce(\"Specification\", '') || ' ' || coalesce(\"ChineseDeclarationName\", '') || ' ' || " +
                    "coalesce(\"EnglishDeclarationName\", '') || ' ' || coalesce(\"SupplierName\", ''))",
                    stored: true);
        });

        modelBuilder.Entity<ImportedBidBatch>(entity =>
        {
            entity.ToTable("imported_bid_batches");
            entity.HasIndex(item => item.FileName).IsUnique();
            entity.HasIndex(item => item.Sha256).IsUnique();
            entity.HasIndex(item => item.BatchNumber).IsUnique();
            entity.Property(item => item.FileName).HasMaxLength(260).IsRequired();
            entity.Property(item => item.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(item => item.BatchNumber).HasMaxLength(240).IsRequired();
        });

        modelBuilder.Entity<LiveStreamBidShowSession>(entity =>
        {
            entity.ToTable("live_stream_bid_show_sessions");
            entity.HasIndex(item => new { item.ShowDate, item.ShowNumber }).IsUnique();
            entity.HasIndex(item => item.EndedAt);
            entity.Property(item => item.StartedBy).HasMaxLength(80).IsRequired();
            entity.Property(item => item.EndedBy).HasMaxLength(80);
            entity.Property(item => item.ShowName).HasMaxLength(300);
            entity.Property(item => item.ImportedFileName).HasMaxLength(260);
            entity.Property(item => item.ImportedFileSha256).HasMaxLength(64);
            entity.HasIndex(item => item.ImportedFileSha256).IsUnique();
        });

        modelBuilder.Entity<LiveStreamBidShowOrder>(entity =>
        {
            entity.ToTable("live_stream_bid_show_orders");
            entity.HasIndex(item => new { item.ShowSessionId, item.ExternalOrderId }).IsUnique();
            entity.HasIndex(item => new { item.ShowSessionId, item.CustomerName });
            entity.Property(item => item.ExternalOrderId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.CustomerName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.OrderName).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.PlacedAt).HasMaxLength(80);
            entity.HasOne(item => item.ShowSession).WithMany(item => item.Orders).HasForeignKey(item => item.ShowSessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LiveStreamBidManualCost>(entity =>
        {
            entity.ToTable("live_stream_bid_manual_costs");
            entity.HasIndex(item => item.ShowOrderId);
            entity.Property(item => item.UnitCost).HasPrecision(12, 2);
            entity.Property(item => item.CreatedBy).HasMaxLength(80).IsRequired();
            entity.HasOne(item => item.ShowOrder).WithMany(item => item.ManualCosts).HasForeignKey(item => item.ShowOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LiveStreamBidShowAttachment>(entity =>
        {
            entity.ToTable("live_stream_bid_show_attachments");
            entity.HasIndex(item => item.ShowSessionId);
            entity.HasIndex(item => item.ShowOrderId);
            entity.HasIndex(item => new { item.ShowSessionId, item.CustomerName });
            entity.Property(item => item.CustomerName).HasMaxLength(200);
            entity.Property(item => item.Kind).HasMaxLength(32).IsRequired();
            entity.Property(item => item.StoredFileName).HasMaxLength(100).IsRequired();
            entity.Property(item => item.OriginalFileName).HasMaxLength(260).IsRequired();
            entity.Property(item => item.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(item => item.UploadedBy).HasMaxLength(80).IsRequired();
            entity.HasOne(item => item.ShowSession).WithMany().HasForeignKey(item => item.ShowSessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.ShowOrder).WithMany().HasForeignKey(item => item.ShowOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LiveStreamBidSale>(entity =>
        {
            entity.ToTable("live_stream_bid_sales");
            entity.HasIndex(item => item.BidProductId);
            entity.HasIndex(item => item.ShowSessionId);
            entity.HasIndex(item => item.ShowOrderId);
            entity.HasIndex(item => item.SaleDate);
            entity.Property(item => item.ProductUuid).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ProductTitle).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Cost).HasPrecision(12, 2);
            entity.Property(item => item.ActualSalePrice).HasPrecision(12, 2);
            entity.Property(item => item.SalePriceRmb).HasPrecision(12, 2);
            entity.Property(item => item.ExchangeRateRmbToUsd).HasPrecision(12, 6);
            entity.Property(item => item.Profit).HasPrecision(12, 2);
            entity.Property(item => item.ProfitUsd).HasPrecision(12, 2);
            entity.Property(item => item.SoldBy).HasMaxLength(80).IsRequired();
            entity.HasOne(item => item.BidProduct).WithMany().HasForeignKey(item => item.BidProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.ShowSession).WithMany(item => item.Sales).HasForeignKey(item => item.ShowSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.ShowOrder).WithMany(item => item.ScannedCosts).HasForeignKey(item => item.ShowOrderId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProductDetailProfile>(entity =>
        {
            entity.ToTable("product_details");
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.Description).HasMaxLength(4000);
            entity.HasOne(item => item.Product).WithOne()
                .HasForeignKey<ProductDetailProfile>(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProductIdentifier>(entity =>
        {
            entity.ToTable("product_identifiers");
            entity.HasIndex(item => new { item.ProductId, item.IdentifierType, item.Value }).IsUnique();
            entity.HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.ToTable("suppliers");
            entity.HasIndex(item => item.Name).IsUnique();
        });
        modelBuilder.Entity<ProductSupplier>(entity =>
        {
            entity.ToTable("product_suppliers");
            entity.HasIndex(item => new { item.ProductId, item.SupplierId }).IsUnique();
            entity.Property(item => item.MinimumPurchasePrice).HasPrecision(12, 2);
            entity.HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Supplier).WithMany().HasForeignKey(item => item.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProcurementLink>(entity =>
        {
            entity.ToTable("procurement_links");
            entity.Property(item => item.Url).HasMaxLength(4096);
            entity.HasOne(item => item.ProductSupplier).WithMany(item => item.ProcurementLinks)
                .HasForeignKey(item => item.ProductSupplierId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProductCompliance>(entity =>
        {
            entity.ToTable("product_compliance");
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.DeclaredValue).HasPrecision(12, 2);
            entity.HasOne(item => item.Product).WithOne().HasForeignKey<ProductCompliance>(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProductPackaging>(entity =>
        {
            entity.ToTable("product_packaging");
            entity.HasKey(item => item.ProductId);
            entity.HasOne(item => item.Product).WithOne().HasForeignKey<ProductPackaging>(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProductPrice>(entity =>
        {
            entity.ToTable("product_prices");
            entity.Property(item => item.Amount).HasPrecision(12, 2);
            entity.HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Cascade);
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
