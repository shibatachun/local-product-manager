using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocalProductManager.Api.Tests;

public sealed class InventoryChannelTests
{
    [Fact]
    public async Task MovingStockToEmptyBin_CanLabelItForEbay()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var fixture = await factory.SeedInventoryAsync(initialQty: 5);

        var move = await client.PostAsJsonAsync($"/api/inventory/products/{fixture.ProductId}/move", new
        {
            sourceLevelId = fixture.LevelId, sourceBinNumber = 1,
            targetLevelId = fixture.LevelId, targetBinNumber = 2,
            quantity = 2, targetInventoryChannel = "ebay"
        });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        using var locations = JsonDocument.Parse(await move.Content.ReadAsStringAsync());
        var bins = locations.RootElement.EnumerateArray()
            .Where(item => item.GetProperty("levelId").GetInt32() == fixture.LevelId)
            .ToDictionary(item => item.GetProperty("binNumber").GetInt32());
        Assert.Equal(3, bins[1].GetProperty("qty").GetInt32());
        Assert.Equal("general", bins[1].GetProperty("inventoryChannel").GetString());
        Assert.Equal(2, bins[2].GetProperty("qty").GetInt32());
        Assert.Equal("ebay", bins[2].GetProperty("inventoryChannel").GetString());
        var quantities = await factory.ReadQuantitiesAsync(fixture);
        Assert.Equal(5, quantities.ProductQty);
    }

    [Fact]
    public async Task AssigningAndRelabelingLocation_PersistsChannelWithoutChangingQuantity()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var fixture = await factory.SeedInventoryAsync();

        var assigned = await client.PutAsJsonAsync($"/api/inventory/levels/{fixture.LevelId}/inventory",
            new { productId = fixture.ProductId, qty = 5, binNumber = 1, inventoryChannel = "ebay" });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var relabeled = await client.PutAsJsonAsync($"/api/inventory/levels/{fixture.LevelId}/inventory",
            new { productId = fixture.ProductId, qty = 5, binNumber = 1, inventoryChannel = "walmart" });
        Assert.Equal(HttpStatusCode.OK, relabeled.StatusCode);

        using var locations = JsonDocument.Parse(await client.GetStringAsync($"/api/inventory/products/{fixture.ProductId}/locations"));
        var bin = locations.RootElement.EnumerateArray().Single(item =>
            item.GetProperty("levelId").GetInt32() == fixture.LevelId && item.GetProperty("binNumber").GetInt32() == 1);
        Assert.Equal("walmart", bin.GetProperty("inventoryChannel").GetString());
        Assert.Equal(5, bin.GetProperty("qty").GetInt32());
        var quantities = await factory.ReadQuantitiesAsync(fixture);
        Assert.Equal(5, quantities.ProductQty);
    }

    [Fact]
    public async Task MovingIntoOccupiedLocationWithDifferentChannel_IsRejectedWithoutLosingStock()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var fixture = await factory.SeedInventoryAsync(initialQty: 5);
        var assigned = await client.PutAsJsonAsync($"/api/inventory/levels/{fixture.LevelId}/inventory",
            new { productId = fixture.ProductId, qty = 2, binNumber = 2, inventoryChannel = "ebay" });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var move = await client.PostAsJsonAsync($"/api/inventory/products/{fixture.ProductId}/move", new
        {
            sourceLevelId = fixture.LevelId, sourceBinNumber = 1,
            targetLevelId = fixture.LevelId, targetBinNumber = 2,
            quantity = 1, targetInventoryChannel = "walmart"
        });
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);

        using var locations = JsonDocument.Parse(await client.GetStringAsync($"/api/inventory/products/{fixture.ProductId}/locations"));
        var bins = locations.RootElement.EnumerateArray()
            .Where(item => item.GetProperty("levelId").GetInt32() == fixture.LevelId)
            .ToDictionary(item => item.GetProperty("binNumber").GetInt32());
        Assert.Equal(5, bins[1].GetProperty("qty").GetInt32());
        Assert.Equal(2, bins[2].GetProperty("qty").GetInt32());
        Assert.Equal("ebay", bins[2].GetProperty("inventoryChannel").GetString());
    }
}
