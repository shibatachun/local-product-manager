using System.Text.Json;
using SkiaSharp;

namespace LocalProductManager.Api.Services;

// Deliberately separate from Product and shelf_level_inventory: these are unidentified finds.
public sealed class BroochFinderService(IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string root = Path.Combine(environment.ContentRootPath, "Data", "brooch-finder");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string IndexPath => Path.Combine(root, "records.json");
    private string ImageRoot => Path.Combine(root, "images");

    public async Task<IReadOnlyList<BroochFindRecord>> ListAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return (await ReadAsync(token)).OrderByDescending(item => item.UpdatedAt).ToArray(); }
        finally { gate.Release(); }
    }

    public async Task<BroochFindRecord> CreateAsync(string? label, string? location, CancellationToken token)
    {
        var cleanLocation = Clean(location, 160);
        if (cleanLocation.Length == 0) throw new ArgumentException("Enter a physical location.");
        await gate.WaitAsync(token);
        try
        {
            var records = await ReadAsync(token);
            var record = new BroochFindRecord(Guid.NewGuid(), Clean(label, 100), cleanLocation,
                DateTimeOffset.UtcNow, []);
            records.Add(record);
            await WriteAsync(records, token);
            return record;
        }
        finally { gate.Release(); }
    }

    public async Task<BroochFindRecord> CreateWithPhotoAsync(string? label, string? location, byte[] bytes, CancellationToken token)
    {
        var cleanLocation = Clean(location, 160);
        if (cleanLocation.Length == 0) throw new ArgumentException("Enter a physical location.");
        var (feature, imageBytes) = PreparePhoto(bytes);
        await gate.WaitAsync(token);
        try
        {
            var records = await ReadAsync(token);
            var id = Guid.NewGuid();
            var photoId = Guid.NewGuid();
            var record = new BroochFindRecord(id, Clean(label, 100), cleanLocation,
                DateTimeOffset.UtcNow, [new BroochFindPhoto(photoId, feature)]);
            Directory.CreateDirectory(ImageRoot);
            var path = Path.Combine(ImageRoot, $"{photoId:N}.jpg");
            await File.WriteAllBytesAsync(path, imageBytes, token);
            try
            {
                records.Add(record);
                await WriteAsync(records, token);
            }
            catch
            {
                File.Delete(path);
                throw;
            }
            return record;
        }
        finally { gate.Release(); }
    }

    public async Task<BroochFindRecord?> UpdateAsync(Guid id, string? label, string? location, CancellationToken token)
    {
        var cleanLocation = Clean(location, 160);
        if (cleanLocation.Length == 0) throw new ArgumentException("Enter a physical location.");
        await gate.WaitAsync(token);
        try
        {
            var records = await ReadAsync(token);
            var index = records.FindIndex(item => item.Id == id);
            if (index < 0) return null;
            records[index] = records[index] with { Label = Clean(label, 100), Location = cleanLocation, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteAsync(records, token);
            return records[index];
        }
        finally { gate.Release(); }
    }

    public async Task<BroochFindRecord?> AddPhotoAsync(Guid id, byte[] bytes, CancellationToken token)
    {
        var (feature, imageBytes) = PreparePhoto(bytes);

        await gate.WaitAsync(token);
        try
        {
            var records = await ReadAsync(token);
            var index = records.FindIndex(item => item.Id == id);
            if (index < 0) return null;
            if (records[index].Photos.Count >= 6) throw new ArgumentException("Maximum 6 reference photos per brooch.");
            Directory.CreateDirectory(ImageRoot);
            var photoId = Guid.NewGuid();
            var path = Path.Combine(ImageRoot, $"{photoId:N}.jpg");
            await File.WriteAllBytesAsync(path, imageBytes, token);
            try
            {
                var photos = records[index].Photos.ToList();
                photos.Add(new BroochFindPhoto(photoId, feature));
                records[index] = records[index] with { Photos = photos, UpdatedAt = DateTimeOffset.UtcNow };
                await WriteAsync(records, token);
            }
            catch
            {
                File.Delete(path);
                throw;
            }
            return records[index];
        }
        finally { gate.Release(); }
    }

    public async Task<string?> PhotoPathAsync(Guid id, Guid photoId, CancellationToken token)
    {
        var path = Path.Combine(ImageRoot, $"{photoId:N}.jpg");
        if (!File.Exists(path)) return null;
        await gate.WaitAsync(token);
        try
        {
            var records = await ReadAsync(token);
            return records.Any(item => item.Id == id && item.Photos.Any(photo => photo.Id == photoId)) ? path : null;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<BroochFindMatch>> SearchAsync(byte[] bytes, CancellationToken token)
    {
        using var bitmap = Decode(bytes);
        var query = Describe(bitmap);
        var records = await ListAsync(token);
        return records.Where(item => item.Photos.Count > 0)
            .Select(item =>
            {
                var best = item.Photos.MaxBy(photo => Similarity(query, photo.Feature))!;
                return new BroochFindMatch(item, best.Id, Math.Round(Similarity(query, best.Feature) * 100, 1));
            })
            .OrderByDescending(item => item.VisualScore)
            .Take(5)
            .ToArray();
    }

    private async Task<List<BroochFindRecord>> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(IndexPath)) return [];
        await using var stream = File.OpenRead(IndexPath);
        return await JsonSerializer.DeserializeAsync<List<BroochFindRecord>>(stream, JsonOptions, token) ?? [];
    }

    private async Task WriteAsync(List<BroochFindRecord> records, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, $"records-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, records, JsonOptions, token);
            File.Move(temporary, IndexPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string Clean(string? value, int maximum)
    {
        var clean = (value ?? "").Trim();
        return clean[..Math.Min(clean.Length, maximum)];
    }

    private static SKBitmap Decode(byte[] bytes)
    {
        if (bytes.Length is 0 or > 15 * 1024 * 1024) throw new ArgumentException("Choose an image between 1 byte and 15 MB.");
        using (var inspectionStream = new MemoryStream(bytes))
        using (var codec = SKCodec.Create(inspectionStream))
        {
            if (codec is null || codec.Info.Width < 24 || codec.Info.Height < 24 ||
                (long)codec.Info.Width * codec.Info.Height > 30_000_000)
                throw new ArgumentException("Choose a valid image of at least 24 × 24 pixels and no more than 30 megapixels.");
        }
        // SKCodec may own/close its input stream; decoding needs a separate stream.
        using var decodingStream = new MemoryStream(bytes);
        var bitmap = SKBitmap.Decode(decodingStream);
        if (bitmap is null)
        {
            throw new ArgumentException("Could not decode the selected image.");
        }
        return bitmap;
    }

    private static (float[] Feature, byte[] Encoded) PreparePhoto(byte[] bytes)
    {
        using var bitmap = Decode(bytes);
        var feature = Describe(bitmap);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 86)
            ?? throw new ArgumentException("Could not encode the image.");
        return (feature, encoded.ToArray());
    }

    // Lightweight foreground-aware visual fingerprint. This is not DINOv2 or a semantic embedding.
    private static float[] Describe(SKBitmap source)
    {
        using var small = source.Resize(new SKImageInfo(96, 96), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new ArgumentException("Could not resize the image.");
        var corners = new[] { small.GetPixel(0, 0), small.GetPixel(95, 0), small.GetPixel(0, 95), small.GetPixel(95, 95) };
        var background = new SKColor((byte)corners.Average(c => c.Red), (byte)corners.Average(c => c.Green), (byte)corners.Average(c => c.Blue));
        var minX = 95; var minY = 95; var maxX = 0; var maxY = 0;
        for (var y = 0; y < 96; y++)
        for (var x = 0; x < 96; x++)
        {
            var c = small.GetPixel(x, y);
            var distance = Math.Abs(c.Red - background.Red) + Math.Abs(c.Green - background.Green) + Math.Abs(c.Blue - background.Blue);
            if (c.Alpha < 50 || distance < 65) continue;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        if (maxX <= minX || maxY <= minY) { minX = 0; minY = 0; maxX = 95; maxY = 95; }
        var padding = 3;
        minX = Math.Max(0, minX - padding); minY = Math.Max(0, minY - padding);
        maxX = Math.Min(95, maxX + padding); maxY = Math.Min(95, maxY + padding);

        var output = new float[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            var px = minX + (x + .5) * (maxX - minX + 1) / 8;
            var py = minY + (y + .5) * (maxY - minY + 1) / 8;
            var color = small.GetPixel(Math.Clamp((int)px, 0, 95), Math.Clamp((int)py, 0, 95));
            var i = (y * 8 + x) * 4;
            output[i] = color.Red / 255f;
            output[i + 1] = color.Green / 255f;
            output[i + 2] = color.Blue / 255f;
            output[i + 3] = color.Alpha < 50 ? 0 :
                Math.Min(1f, (Math.Abs(color.Red - background.Red) + Math.Abs(color.Green - background.Green) + Math.Abs(color.Blue - background.Blue)) / 150f);
        }
        return output;
    }

    private static double Similarity(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0) return 0;
        double difference = 0;
        for (var i = 0; i < left.Length; i += 4)
        {
            difference += (Math.Abs(left[i] - right[i]) + Math.Abs(left[i + 1] - right[i + 1]) +
                           Math.Abs(left[i + 2] - right[i + 2])) / 3 * .7;
            difference += Math.Abs(left[i + 3] - right[i + 3]) * .3;
        }
        return Math.Clamp(1 - difference / (left.Length / 4), 0, 1);
    }
}

public sealed record BroochFindPhoto(Guid Id, float[] Feature);
public sealed record BroochFindRecord(Guid Id, string Label, string Location, DateTimeOffset UpdatedAt, List<BroochFindPhoto> Photos);
public sealed record BroochFindMatch(BroochFindRecord Record, Guid BestPhotoId, double VisualScore);
