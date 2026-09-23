using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;

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

    [Fact]
    public async Task ProductApi_GeneratesSkuWhenItIsBlank()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/products", new
        {
            localInventoryId = (string?)null,
            sku = "",
            productName = "Auto SKU test product",
            category = "Test",
            inventoryCategory = "UNKNOWN",
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

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedProduct>();
        Assert.NotNull(created);
        Assert.Equal($"LPM-{created.Id:D6}", created.Sku);

        var lookup = await client.GetFromJsonAsync<CreatedProduct>($"/api/products/lookup?code={created.Sku}");
        Assert.NotNull(lookup);
        Assert.Equal(created.Id, lookup.Id);
    }

    [Fact]
    public async Task ProductMediaApi_UploadsAndStreamsVideoWithoutMakingItPrimary()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var sku = $"VIDEO-TEST-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/products", new
        {
            localInventoryId = (string?)null,
            sku,
            productName = "Video upload test product",
            category = "Test",
            inventoryCategory = "UNKNOWN",
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
        createResponse.EnsureSuccessStatusCode();
        var product = await createResponse.Content.ReadFromJsonAsync<CreatedProduct>();
        Assert.NotNull(product);

        using var form = new MultipartFormDataContent();
        using var video = new ByteArrayContent([0, 0, 0, 20, 102, 116, 121, 112]);
        video.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(video, "files", "sample.mp4");
        var uploadResponse = await client.PostAsync($"/api/products/{product.Id}/image/upload-many", form);
        uploadResponse.EnsureSuccessStatusCode();
        var upload = await uploadResponse.Content.ReadFromJsonAsync<ProductMediaUpload>();
        Assert.NotNull(upload);
        Assert.Equal(0, upload.ImageCount);
        Assert.Equal(1, upload.VideoCount);
        Assert.Null(upload.ImagePath);

        var files = await client.GetFromJsonAsync<ProductMediaFile[]>($"/api/products/{product.Id}/image/files");
        var savedVideo = Assert.Single(files!);
        Assert.True(savedVideo.IsVideo);
        Assert.Equal("video", savedVideo.MediaType);
        Assert.False(savedVideo.IsPrimary);

        var preview = await client.GetAsync($"/api/products/{product.Id}/image/files/{savedVideo.FileName}");
        Assert.Equal("video/mp4", preview.Content.Headers.ContentType?.MediaType);
        Assert.Contains("bytes", preview.Headers.AcceptRanges);

        var primaryResponse = await client.PostAsJsonAsync($"/api/products/{product.Id}/image/primary", new
        {
            fileName = savedVideo.FileName
        });
        Assert.Equal(HttpStatusCode.NotFound, primaryResponse.StatusCode);

        var archive = await client.GetAsync($"/api/products/{product.Id}/image/files/download-all-media");
        archive.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", archive.Content.Headers.ContentType?.MediaType);
    }

    private sealed record AppVersionResponse(string Version, string Service);
    private sealed record CreatedProduct(int Id, string Sku, int Qty);
    private sealed record ProductMediaUpload(string? ImagePath, int ImageCount, int VideoCount);
    private sealed record ProductMediaFile(string FileName, string MediaType, bool IsVideo, bool IsPrimary);
}
