using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace LocalProductManager.Api.Services;

public sealed record ImageThumbnailResult(
    string FullPath,
    string ContentType,
    DateTimeOffset LastModified);

public sealed class ImageThumbnailService(IWebHostEnvironment environment)
{
    private readonly string cacheRoot = Path.Combine(
        environment.ContentRootPath,
        "Data",
        "image-thumbnails");
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);

    public async Task<ImageThumbnailResult> GetOrCreateAsync(
        string sourceKey,
        int size,
        Func<CancellationToken, Task<Stream>> openSource,
        CancellationToken cancellationToken)
    {
        size = Math.Clamp(size, 48, 512);
        Directory.CreateDirectory(cacheRoot);
        var cacheKey = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceKey}|{size}|webp-v1")));
        var thumbnailPath = Path.Combine(cacheRoot, $"{cacheKey}.webp");
        if (File.Exists(thumbnailPath))
        {
            return CreateResult(thumbnailPath);
        }

        var gate = locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(thumbnailPath))
            {
                return CreateResult(thumbnailPath);
            }

            await using var source = await openSource(cancellationToken);
            using var original = SKBitmap.Decode(source)
                ?? throw new InvalidOperationException("The configured image format could not be decoded.");
            var scale = Math.Min((double)size / original.Width, (double)size / original.Height);
            scale = Math.Min(scale, 1d);
            var width = Math.Max(1, (int)Math.Round(original.Width * scale));
            var height = Math.Max(1, (int)Math.Round(original.Height * scale));
            using var resized = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(resized))
            using (var paint = new SKPaint { IsAntialias = true })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(
                    original,
                    new SKRect(0, 0, width, height),
                    new SKSamplingOptions(SKCubicResampler.Mitchell),
                    paint);
            }

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 80)
                ?? throw new InvalidOperationException("The image thumbnail could not be encoded.");
            var temporaryPath = $"{thumbnailPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    useAsync: true))
                {
                    encoded.SaveTo(output);
                    await output.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, thumbnailPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return CreateResult(thumbnailPath);
        }
        finally
        {
            gate.Release();
            locks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(cacheKey, gate));
        }
    }

    private static ImageThumbnailResult CreateResult(string path) =>
        new(path, "image/webp", File.GetLastWriteTimeUtc(path));
}
