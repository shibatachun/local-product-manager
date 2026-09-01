using System.Net;
using System.Net.Http.Json;

namespace LocalProductManager.Api.Tests;

public sealed class InventoryConcurrencyTests
{
    [Fact]
    public async Task ConcurrentStockIn_AccumulatesEveryRequestWithoutLostUpdates()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var fixture = await factory.SeedInventoryAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            client.PatchAsJsonAsync(
                $"/api/inventory/levels/{fixture.LevelId}/inventory",
                new { productId = fixture.ProductId, delta = 1, binNumber = 1 })));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var quantities = await factory.ReadQuantitiesAsync(fixture);
        Assert.Equal(20, quantities.LocationQty);
        Assert.Equal(20, quantities.ProductQty);
    }

    [Fact]
    public async Task ConcurrentStockOut_NeverDropsBelowZero_AndKeepsProductTotalInSync()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var fixture = await factory.SeedInventoryAsync(initialQty: 10);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            client.PatchAsJsonAsync(
                $"/api/inventory/levels/{fixture.LevelId}/inventory",
                new { productId = fixture.ProductId, delta = -1, binNumber = 1 })));

        Assert.Equal(10, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(10, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var quantities = await factory.ReadQuantitiesAsync(fixture);
        Assert.Equal(0, quantities.LocationQty);
        Assert.Equal(0, quantities.ProductQty);
    }

    [Fact]
    public async Task InsufficientStock_ReturnsConflictWithoutChangingEitherQuantity()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var fixture = await factory.SeedInventoryAsync(initialQty: 3);

        var response = await client.PatchAsJsonAsync(
            $"/api/inventory/levels/{fixture.LevelId}/inventory",
            new { productId = fixture.ProductId, delta = -4, binNumber = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var quantities = await factory.ReadQuantitiesAsync(fixture);
        Assert.Equal(3, quantities.LocationQty);
        Assert.Equal(3, quantities.ProductQty);
    }

    [Fact]
    public async Task StockOperation_PreservesUnassignedQuantityFromProductDetailEdit()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var fixture = await factory.SeedInventoryAsync(initialQty: 5);
        await factory.SetProductTotalAsync(fixture, 10);

        var response = await client.PatchAsJsonAsync(
            $"/api/inventory/levels/{fixture.LevelId}/inventory",
            new { productId = fixture.ProductId, delta = 1, binNumber = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quantities = await factory.ReadQuantitiesAsync(fixture);
        Assert.Equal(6, quantities.LocationQty);
        Assert.Equal(11, quantities.ProductQty);
    }
}
