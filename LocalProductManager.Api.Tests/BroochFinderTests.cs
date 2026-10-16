using System.Net;
using System.Net.Http.Json;
using LocalProductManager.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace LocalProductManager.Api.Tests;

public sealed class BroochFinderTests
{
    [Fact]
    public async Task CanSaveAndFindBroochWithoutCreatingInventoryProduct()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var image = Image(SKColors.Blue);
        BroochRecord? record;
        using (var content = PhotoContent(image))
        {
            var response = await client.PostAsync("/api/brooch-finder/create-with-photo?label=Blue%20dragon&location=Box%20B%20%2F%20slot%203", content);
            response.EnsureSuccessStatusCode();
            record = await response.Content.ReadFromJsonAsync<BroochRecord>();
        }
        Assert.NotNull(record);

        var saved = await client.GetFromJsonAsync<BroochRecord[]>("/api/brooch-finder");
        var photoId = Assert.Single(Assert.Single(saved!).Photos).Id;
        Assert.Equal("Box B / slot 3", saved![0].Location);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/brooch-finder/{record.Id}/photos/{photoId}")).StatusCode);

        using (var query = PhotoContent(image))
        {
            var results = await client.PostAsync("/api/brooch-finder/search", query);
            results.EnsureSuccessStatusCode();
            var matches = await results.Content.ReadFromJsonAsync<BroochMatch[]>();
            Assert.Equal(record.Id, Assert.Single(matches!).Record.Id);
            Assert.True(matches![0].VisualScore > 95);
        }

        using var scope = factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().Products);
    }

    [Fact]
    public async Task RejectsUnreadableAndTruncatedReferencePhotos()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/brooch-finder", new { location = "Tray 1" });
        var record = await response.Content.ReadFromJsonAsync<BroochRecord>();
        Assert.NotNull(record);

        using var badImage = PhotoContent([1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync($"/api/brooch-finder/{record.Id}/photos", badImage)).StatusCode);

        using var truncated = PhotoContent(Image(SKColors.Green));
        truncated.Headers.Add("X-Upload-Expected-Bytes", "99999");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync($"/api/brooch-finder/{record.Id}/photos", truncated)).StatusCode);

        var saved = await client.GetFromJsonAsync<BroochRecord[]>("/api/brooch-finder");
        Assert.Empty(Assert.Single(saved!).Photos);

        using var invalidCreate = PhotoContent([1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/api/brooch-finder/create-with-photo?location=Tray%202", invalidCreate)).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<BroochRecord[]>("/api/brooch-finder"))!);
    }

    private static ByteArrayContent PhotoContent(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return content;
    }

    private static byte[] Image(SKColor color)
    {
        using var bitmap = new SKBitmap(80, 80);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawOval(new SKRect(16, 10, 64, 70), paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed record BroochRecord(Guid Id, string Location, List<BroochPhoto> Photos);
    private sealed record BroochPhoto(Guid Id);
    private sealed record BroochMatch(BroochRecord Record, double VisualScore);
}
