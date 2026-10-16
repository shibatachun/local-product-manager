using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LocalProductManager.Api.Tests;

public sealed class MediaLibraryTests
{
    [Fact]
    public async Task Folder_CanReceivePhotosAndCapturedVideosSeparately()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var folder = $"Camera test {Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync("/api/media-library/folders", new { name = folder });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var url = $"/api/media-library/folders/{Uri.EscapeDataString(folder)}";
        using (var form = new MultipartFormDataContent())
        {
            using var photo = new ByteArrayContent([137, 80, 78, 71]);
            photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(photo, "files", "camera.png");
            var upload = await client.PostAsync($"{url}/upload", form);
            upload.EnsureSuccessStatusCode();
            Assert.Equal(1, (await upload.Content.ReadFromJsonAsync<UploadResult>())?.ImageCount);
        }
        using (var capture = new ByteArrayContent([0, 0, 0, 20, 102, 116, 121, 112]))
        {
            capture.Headers.ContentType = new MediaTypeHeaderValue("video/quicktime");
            var upload = await client.PostAsync($"{url}/upload-capture", capture);
            upload.EnsureSuccessStatusCode();
            Assert.Equal(1, (await upload.Content.ReadFromJsonAsync<UploadResult>())?.VideoCount);
        }

        var files = await client.GetFromJsonAsync<MediaFile[]>($"{url}/files");
        Assert.Equal(2, files?.Length);
        Assert.Single(files!, file => !file.IsVideo);
        var video = Assert.Single(files!, file => file.IsVideo);
        using var streamed = await client.GetAsync($"{url}/files/{video.FileName}");
        Assert.Equal(HttpStatusCode.OK, streamed.StatusCode);
        Assert.Equal("video/quicktime", streamed.Content.Headers.ContentType?.MediaType);
        Assert.Contains("bytes", streamed.Headers.AcceptRanges);
    }

    [Fact]
    public async Task Folder_AcceptsManualPhotoAndVideoAsRawUploads()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var folder = $"Raw upload {Guid.NewGuid():N}";
        (await client.PostAsJsonAsync("/api/media-library/folders", new { name = folder })).EnsureSuccessStatusCode();
        var url = $"/api/media-library/folders/{Uri.EscapeDataString(folder)}/upload-file";

        using (var photo = new ByteArrayContent([137, 80, 78, 71]))
        {
            photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            photo.Headers.Add("X-Upload-Expected-Bytes", "4");
            var upload = await client.PostAsync($"{url}?fileName=photo.png", photo);
            upload.EnsureSuccessStatusCode();
            Assert.Equal(1, (await upload.Content.ReadFromJsonAsync<UploadResult>())?.ImageCount);
        }
        using (var video = new ByteArrayContent([0, 0, 0, 20, 102, 116, 121, 112]))
        {
            video.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var upload = await client.PostAsync($"{url}?fileName=video.mov", video);
            upload.EnsureSuccessStatusCode();
            Assert.Equal(1, (await upload.Content.ReadFromJsonAsync<UploadResult>())?.VideoCount);
        }

        Assert.Equal(2, (await client.GetFromJsonAsync<MediaFile[]>($"/api/media-library/folders/{Uri.EscapeDataString(folder)}/files"))?.Length);

        using var incomplete = new ByteArrayContent([1, 2]);
        incomplete.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        incomplete.Headers.Add("X-Upload-Expected-Bytes", "4");
        var rejected = await client.PostAsync($"{url}?fileName=incomplete.png", incomplete);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("browser sent 4 B but the server received 2 B", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(2, (await client.GetFromJsonAsync<MediaFile[]>($"/api/media-library/folders/{Uri.EscapeDataString(folder)}/files"))?.Length);
    }

    [Fact]
    public async Task Folder_AcceptsManualVideoAndPickerFilesWithoutMimeType()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var folder = $"Manual upload {Guid.NewGuid():N}";
        (await client.PostAsJsonAsync("/api/media-library/folders", new { name = folder })).EnsureSuccessStatusCode();
        var url = $"/api/media-library/folders/{Uri.EscapeDataString(folder)}/upload";

        using var form = new MultipartFormDataContent();
        using var photo = new ByteArrayContent([137, 80, 78, 71]);
        photo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(photo, "files", "manual.jpeg");
        using var video = new ByteArrayContent([0, 0, 0, 20, 102, 116, 121, 112]);
        video.Headers.ContentType = new MediaTypeHeaderValue("video/quicktime");
        form.Add(video, "files", "manual.mov");
        var upload = await client.PostAsync(url, form);
        upload.EnsureSuccessStatusCode();
        var result = await upload.Content.ReadFromJsonAsync<UploadResult>();
        Assert.Equal(1, result?.ImageCount);
        Assert.Equal(1, result?.VideoCount);
        Assert.Equal(2, (await client.GetFromJsonAsync<MediaFile[]>($"/api/media-library/folders/{Uri.EscapeDataString(folder)}/files"))?.Length);
    }

    [Fact]
    public async Task Folder_RejectsPathTraversalAndUnsupportedMedia()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var badName = await client.PostAsJsonAsync("/api/media-library/folders", new { name = "../outside" });
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
        var reservedName = await client.PostAsJsonAsync("/api/media-library/folders", new { name = "CON.txt" });
        Assert.Equal(HttpStatusCode.BadRequest, reservedName.StatusCode);

        var create = await client.PostAsJsonAsync("/api/media-library/folders", new { name = "Safe" });
        create.EnsureSuccessStatusCode();
        using var form = new MultipartFormDataContent();
        using var other = new ByteArrayContent([1, 2, 3]);
        other.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(other, "files", "file.pdf");
        var upload = await client.PostAsync("/api/media-library/folders/Safe/upload", form);
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<MediaFile[]>("/api/media-library/folders/Safe/files"))!);
        var open = await client.PostAsync("/api/media-library/folders/Safe/open", null);
        Assert.Equal(HttpStatusCode.Forbidden, open.StatusCode);
    }

    private sealed record UploadResult(int ImageCount, int VideoCount);
    private sealed record MediaFile(string FileName, bool IsVideo);
}
