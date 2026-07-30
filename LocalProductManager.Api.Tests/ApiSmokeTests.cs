using System.Net;
using System.Net.Http.Json;

namespace LocalProductManager.Api.Tests;

public sealed class ApiSmokeTests
{
    [Fact]
    public async Task HealthEndpoint_IsHealthy()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task VersionEndpoint_IdentifiesInventoryApi()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<AppVersionResponse>("/api/app/version");

        Assert.NotNull(response);
        Assert.Equal("inventory-api", response.Service);
        Assert.False(string.IsNullOrWhiteSpace(response.Version));
    }

    [Fact]
    public async Task ProductApi_CreatesAndReadsProduct()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var sku = $"API-TEST-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync("/api/products", new
        {
            localInventoryId = (string?)null,
            sku,
            productName = "API smoke test product",
            category = "Test",
            cost = 1.25m,
            originalCost = (decimal?)null,
            salePrice = 2.50m,
            qty = 0,
            weight = (decimal?)null,
            length = (decimal?)null,
            width = (decimal?)null,
            height = (decimal?)null,
            imagePath = (string?)null,
            imageSource = "local"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedProduct>();
        Assert.NotNull(created);
        Assert.Equal(sku, created.Sku);

        var readResponse = await client.GetFromJsonAsync<CreatedProduct>($"/api/products/{created.Id}");
        Assert.NotNull(readResponse);
        Assert.Equal(created.Id, readResponse.Id);
        Assert.Equal(sku, readResponse.Sku);
    }

    private sealed record AppVersionResponse(string Version, string Service);
    private sealed record CreatedProduct(int Id, string Sku, int Qty);
}
