using System.Net;
using System.Net.Http.Json;

namespace LocalProductManager.Api.Tests;

public sealed class ProductInventoryCategoryTests
{
    [Fact]
    public async Task ProductCategoryIds_UseIndependentSequencesAndDefaultToUnknown()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();

        var charm1 = await CreateAsync(client, "CHARM");
        var eye1 = await CreateAsync(client, "EYE");
        var charm2 = await CreateAsync(client, "charms");
        var unknown1 = await CreateAsync(client, null);

        Assert.Equal(("CHARM", "CHARM-001"), (charm1.InventoryCategory, charm1.CategoryInventoryId));
        Assert.Equal(("EYE", "EYE-001"), (eye1.InventoryCategory, eye1.CategoryInventoryId));
        Assert.Equal(("CHARM", "CHARM-002"), (charm2.InventoryCategory, charm2.CategoryInventoryId));
        Assert.Equal(("UNKNOWN", "UNKNOWN-001"), (unknown1.InventoryCategory, unknown1.CategoryInventoryId));

        var lookup = await client.GetFromJsonAsync<CategoryProduct>(
            $"/api/products/lookup?code={charm2.CategoryInventoryId}");
        Assert.NotNull(lookup);
        Assert.Equal(charm2.Id, lookup.Id);
    }

    private static async Task<CategoryProduct> CreateAsync(HttpClient client, string? inventoryCategory)
    {
        var sku = $"CATEGORY-TEST-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/products", new
        {
            localInventoryId = (string?)null,
            sku,
            productName = "Category numbering test product",
            category = (string?)null,
            inventoryCategory,
            cost = (decimal?)null,
            originalCost = (decimal?)null,
            salePrice = (decimal?)null,
            qty = 0,
            weight = (decimal?)null,
            length = (decimal?)null,
            width = (decimal?)null,
            height = (decimal?)null,
            imagePath = (string?)null,
            imageSource = "local"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryProduct>())!;
    }

    private sealed record CategoryProduct(
        int Id,
        string InventoryCategory,
        string CategoryInventoryId);
}
