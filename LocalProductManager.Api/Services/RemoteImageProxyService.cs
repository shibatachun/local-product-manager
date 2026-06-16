namespace LocalProductManager.Api.Services;

public sealed record RemoteImageResult(
    Stream Stream,
    string ContentType,
    string FileName);

public sealed class RemoteImageProxyService(HttpClient httpClient)
{
    public async Task<RemoteImageResult> GetImageAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Remote image URL must be an HTTP or HTTPS URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/125.0 Safari/537.36");

        var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new InvalidOperationException("The remote image could not be loaded.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            throw new InvalidOperationException("The remote URL did not return an image.");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "amazon-image";
        }

        return new RemoteImageResult(
            new ResponseDisposeStream(stream, response),
            contentType,
            fileName);
    }

    private sealed class ResponseDisposeStream(Stream inner, IDisposable response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
