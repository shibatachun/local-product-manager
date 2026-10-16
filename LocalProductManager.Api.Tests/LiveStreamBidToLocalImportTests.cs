using System.Net;
using System.Net.Http.Json;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocalProductManager.Api.Tests;

public sealed class LiveStreamBidToLocalImportTests
{
    [Fact]
    public async Task Transfer_CreatesMappedLocalProduct_AndCannotBeRepeated()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        await IsolatedApiFactory.LoginAsAdminAsync(client);
        var bidId = await SeedBidAsync(factory, "B000021|P004", weightGrams: 34.6m, weightOunces: null);

        var response = await client.PostAsJsonAsync(
            $"/api/live-stream-bid-products/{bidId}/import-to-local", new { transferInventory = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal($"INV-{result.ProductId}", result.LocalInventoryId);
        Assert.Equal($"LPM-{result.ProductId:D6}", result.Sku);
        Assert.Equal((4, 9.60m, 1.22m, "BID UUID: B000021|P004"),
            (result.Qty, result.Cost, result.WeightOunces, result.Note));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var local = await db.Products.SingleAsync(item => item.Id == result.ProductId);
            var bid = await db.LiveStreamBidProducts.SingleAsync(item => item.Id == bidId);
            Assert.Equal("Five flower brooches", local.ProductName);
            Assert.Equal("UNKNOWN", local.InventoryCategory);
            Assert.False(local.ReadyToList);
            Assert.Equal(9.60m, local.Cost);
            Assert.Equal(4, local.Qty);
            Assert.Equal("BID UUID: B000021|P004", local.Note);
            Assert.NotEqual("BID-ORIGINAL-SKU", local.Sku);
            Assert.Equal(result.ProductId, bid.SourceProductId);
            Assert.Equal(0, bid.Inventory);
            Assert.Equal(0, bid.ActualInventory);
        }

        var duplicate = await client.PostAsJsonAsync(
            $"/api/live-stream-bid-products/{bidId}/import-to-local", new { transferInventory = true });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Copy_KeepsBidStock_AndPrefersExistingOunceWeight()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        await IsolatedApiFactory.LoginAsAdminAsync(client);
        var bidId = await SeedBidAsync(factory, "B000022|P001", weightGrams: 34.6m, weightOunces: 2.5m);

        var response = await client.PostAsJsonAsync(
            $"/api/live-stream-bid-products/{bidId}/import-to-local", new { transferInventory = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal(2.5m, result.WeightOunces);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bid = await db.LiveStreamBidProducts.SingleAsync(item => item.Id == bidId);
        Assert.Equal(7, bid.Inventory);
        Assert.Equal(4, bid.ActualInventory);
    }

    private static async Task<int> SeedBidAsync(
        IsolatedApiFactory factory, string uuid, decimal? weightGrams, decimal? weightOunces)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bid = new LiveStreamBidProduct
        {
            Uuid = uuid,
            Title = "Five flower brooches",
            Quantity = 8,
            Inventory = 7,
            ActualInventory = 4,
            ActualSku = "BID-ORIGINAL-SKU",
            UnitPrice = 1.20m,
            TotalCost = 100m,
            WeightGrams = weightGrams,
            WeightOunces = weightOunces
        };
        db.LiveStreamBidProducts.Add(bid);
        await db.SaveChangesAsync();
        return bid.Id;
    }

    private sealed record ImportResult(
        int ProductId, string LocalInventoryId, string Sku, int Qty,
        decimal Cost, decimal? WeightOunces, string Note);
}
