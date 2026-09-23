using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocalProductManager.Api.Tests;

public sealed class LiveStreamBidVideoTests
{
    [Fact]
    public async Task VideoUploads_AreListedAndStreamedWithoutReplacingThePhoto()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        int productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = new LiveStreamBidProduct
            {
                Uuid = Guid.NewGuid().ToString("D"),
                Title = "Video test BID product",
                Quantity = 1,
                Inventory = 1,
                UnitPrice = 1
            };
            db.LiveStreamBidProducts.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        }

        var baseUrl = $"/api/live-stream-bid-products/{productId}/image";
        using (var photoForm = new MultipartFormDataContent())
        {
            using var photo = new ByteArrayContent([1, 2, 3]);
            photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            photoForm.Add(photo, "files", "sample.png");
            var rejected = await client.PostAsync($"{baseUrl}/videos/upload-many", photoForm);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using (var form = new MultipartFormDataContent())
        {
            using var video = new ByteArrayContent([0, 0, 0, 20, 102, 116, 121, 112]);
            video.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            form.Add(video, "files", "sample.mp4");
            var upload = await client.PostAsync($"{baseUrl}/videos/upload-many", form);
            upload.EnsureSuccessStatusCode();
            Assert.Equal(1, (await upload.Content.ReadFromJsonAsync<VideoUpload>())?.VideoCount);
        }

        var files = await client.GetFromJsonAsync<VideoFile[]>($"{baseUrl}/videos");
        var saved = Assert.Single(files!);
        using (var stream = await client.GetAsync($"{baseUrl}/videos/{saved.FileName}"))
        {
            Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
            Assert.Equal("video/mp4", stream.Content.Headers.ContentType?.MediaType);
            Assert.Contains("bytes", stream.Headers.AcceptRanges);
        }

        using (var captured = new ByteArrayContent([0, 0, 0, 20, 102, 116, 121, 112]))
        {
            captured.Headers.ContentType = new MediaTypeHeaderValue("video/quicktime");
            captured.Headers.Add("X-Upload-File-Name", "capture.mov");
            var upload = await client.PostAsync($"{baseUrl}/videos/upload-capture", captured);
            upload.EnsureSuccessStatusCode();
        }
        Assert.Equal(2, (await client.GetFromJsonAsync<VideoFile[]>($"{baseUrl}/videos"))?.Length);

        await using var checkScope = factory.Services.CreateAsyncScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await checkDb.LiveStreamBidProducts.SingleAsync(item => item.Id == productId);
        Assert.Null(stored.ImagePath);
    }

    private sealed record VideoUpload(int VideoCount);
    private sealed record VideoFile(string FileName);
}
