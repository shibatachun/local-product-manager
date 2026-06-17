using System.Text.Json;

namespace LocalProductManager.Api.Services;

public sealed record StoreLinkDto(string Platform, string Label, string? Url);

public sealed class StoreLinkService(IWebHostEnvironment environment)
{
    private static readonly string[] Platforms = ["amazon", "walmart", "ebay", "etsy"];
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amazon"] = "Amazon",
        ["walmart"] = "Walmart",
        ["ebay"] = "eBay",
        ["etsy"] = "Etsy"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object sync = new();
    private readonly string filePath = Path.Combine(environment.ContentRootPath, "Data", "store-links.json");

    public IReadOnlyList<StoreLinkDto> GetAll()
    {
        var links = Load();
        return Platforms
            .Select(platform => new StoreLinkDto(platform, Labels[platform], links.GetValueOrDefault(platform)))
            .ToArray();
    }

    public StoreLinkDto Save(string platform, string? url)
    {
        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        if (!Platforms.Contains(normalizedPlatform, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Unsupported store platform.");
        }

        var normalizedUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        if (normalizedUrl is not null &&
            !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Store URL must be a valid absolute URL.");
        }

        var links = Load();
        links[normalizedPlatform] = normalizedUrl;
        SaveLinks(links);
        return new StoreLinkDto(normalizedPlatform, Labels[normalizedPlatform], normalizedUrl);
    }

    private Dictionary<string, string?> Load()
    {
        lock (sync)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ??
                        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (JsonException)
            {
                // Reset below when the file is invalid.
            }

            var links = Platforms.ToDictionary(platform => platform, _ => (string?)null);
            SaveLinks(links);
            return links;
        }
    }

    private void SaveLinks(Dictionary<string, string?> links)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(links, JsonOptions));
    }
}
