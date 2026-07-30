using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocalProductManager.Api.Tests;

public sealed class InternalApiWorkflowTests
{
    [Fact]
    public async Task ProductListingsAndAsins_FullLifecycleWorks()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var product = await CreateProductAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/products/lookup?code={product.Sku}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/products/duplicate-check?sku={product.Sku}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products/export")).StatusCode);

        var listingResponse = await client.PostAsJsonAsync($"/api/products/{product.Id}/listings", new
        {
            platform = "walmart",
            listingUrl = "https://example.test/item/1"
        });
        listingResponse.EnsureSuccessStatusCode();
        var listingId = (await ReadJsonAsync(listingResponse)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/products/{product.Id}/listings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/products/{product.Id}/listings/{listingId}",
            new { platform = "ebay", listingUrl = "https://example.test/item/2" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(
            $"/api/products/{product.Id}/listings/{listingId}")).StatusCode);

        var asinResponse = await client.PostAsJsonAsync($"/api/products/{product.Id}/amazon-asins", new
        {
            asin = "B012345678",
            asinType = "child",
            parentAsin = (string?)null,
            amazonAttributeDone = false
        });
        Assert.Equal(HttpStatusCode.Created, asinResponse.StatusCode);
        var asinId = (await ReadJsonAsync(asinResponse)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/products/{product.Id}/amazon-asins")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/products/{product.Id}/amazon-asins/{asinId}",
            new { asin = "B012345678", asinType = "child", parentAsin = (string?)null, amazonAttributeDone = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(
            $"/api/products/{product.Id}/amazon-asins/{asinId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"/api/products/{product.Id}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task WarehouseApi_FullLifecycleKeepsDataIsolated()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        await IsolatedApiFactory.LoginAsAdminAsync(client);
        var product = await CreateProductAsync(client);

        var warehouseResponse = await client.PostAsJsonAsync("/api/inventory/warehouses", new { name = "Test warehouse" });
        warehouseResponse.EnsureSuccessStatusCode();
        var warehouseId = (await ReadJsonAsync(warehouseResponse)).GetProperty("id").GetInt32();
        var shelfResponse = await client.PostAsJsonAsync($"/api/inventory/warehouses/{warehouseId}/shelves", new
        {
            zoneCode = "T",
            rowNumber = 1,
            levelCount = 1,
            x = 10,
            y = 10
        });
        shelfResponse.EnsureSuccessStatusCode();
        var shelfJson = await ReadJsonAsync(shelfResponse);
        var shelfId = shelfJson.GetProperty("id").GetInt32();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/inventory/shelves/{shelfId}");
        var levelId = detail.GetProperty("levels")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/inventory/levels/{levelId}/inventory",
            new { productId = product.Id, qty = 4, binNumber = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync(
            $"/api/inventory/levels/{levelId}/inventory",
            new { productId = product.Id, delta = 2, binNumber = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/inventory/products/{product.Id}/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/inventory/shelves/{shelfId}/layout",
            new { x = 20, y = 20, rotation = 90 })).StatusCode);

        var addLevelResponse = await client.PostAsJsonAsync($"/api/inventory/shelves/{shelfId}/levels", new { });
        addLevelResponse.EnsureSuccessStatusCode();
        var addedLevelId = (await ReadJsonAsync(addLevelResponse)).GetProperty("levels")
            .EnumerateArray().Max(item => item.GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(
            $"/api/inventory/levels/{addedLevelId}")).StatusCode);

        await client.PutAsJsonAsync($"/api/inventory/levels/{levelId}/inventory",
            new { productId = product.Id, qty = 0, binNumber = 1 });
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(
            $"/api/inventory/shelves/{shelfId}")).StatusCode);
    }

    [Fact]
    public async Task AuthSocialVideoAndStoreApis_FullLifecycleWorks()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        await IsolatedApiFactory.LoginAsAdminAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/users")).StatusCode);

        var socialResponse = await client.PostAsJsonAsync("/api/social-accounts", new
        {
            platform = "youtube",
            accountName = "Test channel",
            accountHandle = "@test",
            accountUrl = "https://example.test/channel",
            color = "#123456",
            isActive = true
        });
        Assert.Equal(HttpStatusCode.Created, socialResponse.StatusCode);
        var accountId = (await ReadJsonAsync(socialResponse)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/social-accounts/{accountId}/metrics",
            new { snapshotDate = "2026-07-30", clicks = 1, likes = 2, views = 3, comments = 4, shares = 5, followers = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/social-accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/social-accounts/debug")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(
            $"/api/social-accounts/{accountId}")).StatusCode);

        var videoResponse = await client.PostAsJsonAsync("/api/video-trends", new
        {
            platform = "youtube",
            url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            title = "Test video",
            description = "Isolated",
            author = "Test",
            tags = "test"
        });
        Assert.Equal(HttpStatusCode.Created, videoResponse.StatusCode);
        var videoId = (await ReadJsonAsync(videoResponse)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/video-trends")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(
            $"/api/video-trends/{videoId}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/store-links")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/store-links/walmart", new { url = "https://example.test/store" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
    }

    private static async Task<TestProduct> CreateProductAsync(HttpClient client)
    {
        var sku = $"FULL-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/products", new
        {
            localInventoryId = (string?)null,
            sku,
            productName = "Full API test product",
            category = "Test",
            cost = 1m,
            originalCost = (decimal?)null,
            salePrice = 2m,
            qty = 0,
            weight = (decimal?)null,
            length = (decimal?)null,
            width = (decimal?)null,
            height = (decimal?)null,
            imagePath = (string?)null,
            imageSource = "local"
        });
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return new TestProduct(json.GetProperty("id").GetInt32(), sku);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement.Clone();

    private sealed record TestProduct(int Id, string Sku);
}
