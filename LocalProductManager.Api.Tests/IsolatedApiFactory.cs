using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LocalProductManager.Api.Tests;

public sealed class IsolatedApiFactory : WebApplicationFactory<Program>
{
    private readonly string contentRootPath = Path.Combine(
        Path.GetTempPath(),
        $"local-product-manager-content-{Guid.NewGuid():N}");
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"local-product-manager-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(contentRootPath);
        builder.UseContentRoot(contentRootPath);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"Data Source={databasePath};Default Timeout=10;Pooling=True",
                ["AiAnalysis:GeminiApiKey"] = ""
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Default Timeout=10;Pooling=True"));

            var applicationHostedServices = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType?.Namespace == "LocalProductManager.Api.Services")
                .ToArray();
            foreach (var descriptor in applicationHostedServices)
            {
                services.Remove(descriptor);
            }
        });
    }

    public async Task<InventoryFixture> SeedInventoryAsync(int initialQty = 0)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var product = new Product
        {
            Sku = $"TEST-{Guid.NewGuid():N}",
            ProductName = "Concurrent inventory test product",
            Qty = initialQty
        };
        var warehouse = new Warehouse
        {
            Code = $"WT{Guid.NewGuid():N}"[..16],
            Name = "Isolated test warehouse"
        };
        var shelf = new WarehouseShelf
        {
            Warehouse = warehouse,
            Code = $"S{Guid.NewGuid():N}"[..20],
            ZoneCode = "T",
            RowNumber = 1,
            RackNumber = 1,
            LevelCount = 1
        };
        var level = new ShelfLevel
        {
            WarehouseShelf = shelf,
            Code = $"L{Guid.NewGuid():N}"[..20],
            LevelNumber = 1
        };

        db.AddRange(product, warehouse, shelf, level);
        await db.SaveChangesAsync();

        if (initialQty > 0)
        {
            db.ShelfLevelInventories.Add(new ShelfLevelInventory
            {
                ShelfLevelId = level.Id,
                BinNumber = 1,
                ProductId = product.Id,
                Qty = initialQty
            });
            await db.SaveChangesAsync();
        }

        return new InventoryFixture(product.Id, level.Id);
    }

    public static async Task LoginAsAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "admin123",
            password = "123"
        });
        response.EnsureSuccessStatusCode();
    }

    public async Task<(int LocationQty, int ProductQty)> ReadQuantitiesAsync(InventoryFixture fixture)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locationQty = await db.ShelfLevelInventories
            .Where(item => item.ShelfLevelId == fixture.LevelId && item.ProductId == fixture.ProductId)
            .SumAsync(item => item.Qty);
        var productQty = await db.Products
            .Where(item => item.Id == fixture.ProductId)
            .Select(item => item.Qty)
            .SingleAsync();
        return (locationQty, productQty);
    }

    public async Task SetProductTotalAsync(InventoryFixture fixture, int qty)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.SingleAsync(item => item.Id == fixture.ProductId);
        product.Qty = qty;
        await db.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (Directory.Exists(contentRootPath))
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }
}

public sealed record InventoryFixture(int ProductId, int LevelId);
