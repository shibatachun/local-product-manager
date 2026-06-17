using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalProductManager.Api.Services;

public sealed class AmazonImageLookupService(HttpClient httpClient)
{
    private static readonly Regex OgImageRegex = new(
        """<meta[^>]+(?:property|name)=["'](?:og:image|og:image:secure_url|twitter:image|twitter:image:src)["'][^>]*>""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ContentRegex = new(
        """content=["'](?<url>[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LandingImageRegex = new(
        """"landingImageUrl"\s*:\s*"(?<url>[^"]+)"""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DataOldHiresRegex = new(
        """data-old-hires=["'](?<url>[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DynamicImageRegex = new(
        """data-a-dynamic-image=["'](?<json>[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AmazonMediaRegex = new(
        """https?://[^"'\\<>\s]+?m\.media-amazon\.com/images/I/[^"'\\<>\s]+?\.(?:jpg|jpeg|png|webp)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsonLdRegex = new(
        """<script[^>]+type=["']application/ld\+json["'][^>]*>(?<json>.*?)</script>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex GenericImageRegex = new(
        """https?://[^"'\\<>\s]+?\.(?:jpg|jpeg|png|webp)(?:\?[^"'\\<>\s]*)?""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] PreferredImageHosts =
    [
        "i5.walmartimages.com",
        "i.ebayimg.com",
        "i.etsystatic.com",
        "m.media-amazon.com"
    ];

    public async Task<string> FindImageUrlAsync(
        string listingUrl,
        string? platform,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(listingUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Listing URL must be an HTTP or HTTPS URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/125.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var strategy = NormalizePlatform(platform) ?? PlatformFromHost(uri.Host);
        var imageUrl = strategy switch
        {
            "amazon" => FindAmazonImage(html),
            "walmart" => FindMarketplaceImage(html, ["i5.walmartimages.com", "i5-qa.walmartimages.com"]),
            "ebay" => FindMarketplaceImage(html, ["i.ebayimg.com", "thumbs.ebaystatic.com"]),
            "etsy" => FindMarketplaceImage(html, ["i.etsystatic.com", "i.etsystatic.com"]),
            _ => FindMarketplaceImage(html, PreferredImageHosts)
        };
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new InvalidOperationException("Could not find a product image on the listing page.");
        }

        return NormalizeImageUrl(imageUrl, uri);
    }

    public Task<string> FindImageUrlAsync(
        string listingUrl,
        CancellationToken cancellationToken) =>
        FindImageUrlAsync(listingUrl, platform: null, cancellationToken);

    private static string? FindAmazonImage(string html) =>
        FindMetaImage(html) ??
        FindLandingImage(html) ??
        FindDataOldHiresImage(html) ??
        FindDynamicImage(html) ??
        FindAmazonMediaImage(html) ??
        FindJsonLdImage(html) ??
        FindPreferredRemoteImage(html, ["m.media-amazon.com"]) ??
        FindGenericRemoteImage(html);

    private static string? FindMarketplaceImage(string html, IReadOnlyList<string> preferredHosts) =>
        FindMetaImage(html) ??
        FindJsonLdImage(html) ??
        FindPreferredRemoteImage(html, preferredHosts) ??
        FindGenericRemoteImage(html);

    private static string? FindMetaImage(string html)
    {
        var match = OgImageRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var content = ContentRegex.Match(match.Value);
        return content.Success ? content.Groups["url"].Value : null;
    }

    private static string? FindJsonLdImage(string html)
    {
        foreach (Match match in JsonLdRegex.Matches(html))
        {
            var decoded = WebUtility.HtmlDecode(match.Groups["json"].Value);
            try
            {
                using var document = JsonDocument.Parse(decoded);
                var image = FindImageInJson(document.RootElement);
                if (!string.IsNullOrWhiteSpace(image))
                {
                    return image;
                }
            }
            catch (JsonException)
            {
                // Keep scanning other JSON-LD blocks.
            }
        }

        return null;
    }

    private static string? FindImageInJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("image"))
                {
                    var image = ReadImageValue(property.Value);
                    if (!string.IsNullOrWhiteSpace(image))
                    {
                        return image;
                    }
                }

                var nested = FindImageInJson(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindImageInJson(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? ReadImageValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Array => element.EnumerateArray().Select(ReadImageValue).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
        JsonValueKind.Object => element.TryGetProperty("url", out var url) ? ReadImageValue(url) : null,
        _ => null
    };

    private static string? FindLandingImage(string html)
    {
        var match = LandingImageRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>($"\"{match.Groups["url"].Value}\"");
        }
        catch (JsonException)
        {
            return match.Groups["url"].Value;
        }
    }

    private static string? FindDataOldHiresImage(string html)
    {
        var match = DataOldHiresRegex.Match(html);
        return match.Success ? match.Groups["url"].Value : null;
    }

    private static string? FindDynamicImage(string html)
    {
        var match = DynamicImageRegex.Match(html);
        if (!match.Success)
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(match.Groups["json"].Value);
        try
        {
            var imageMap = JsonSerializer.Deserialize<Dictionary<string, int[]>>(decoded);
            return imageMap?
                .OrderByDescending(item => item.Value.Length >= 2 ? item.Value[0] * item.Value[1] : 0)
                .Select(item => item.Key)
                .FirstOrDefault();
        }
        catch (JsonException)
        {
            var mediaMatch = AmazonMediaRegex.Match(decoded);
            return mediaMatch.Success ? mediaMatch.Value : null;
        }
    }

    private static string? FindAmazonMediaImage(string html)
    {
        var matches = AmazonMediaRegex.Matches(html)
            .Select(match => NormalizeImageUrl(match.Value))
            .Where(IsUsefulImage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches
            .OrderByDescending(url => url.Contains("images/I/", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(url => url.Length)
            .FirstOrDefault();
    }

    private static string? FindPreferredRemoteImage(string html, IReadOnlyList<string> preferredHosts)
    {
        var images = GenericImageRegex.Matches(html)
            .Select(match => NormalizeImageUrl(match.Value))
            .Where(IsUsefulImage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return images
            .Where(url => preferredHosts.Any(host => url.Contains(host, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(url => ScoreImageUrl(url, preferredHosts))
            .FirstOrDefault();
    }

    private static string? FindGenericRemoteImage(string html)
    {
        var matches = GenericImageRegex.Matches(html)
            .Select(match => NormalizeImageUrl(match.Value))
            .Where(IsUsefulImage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches
            .OrderByDescending(url => ScoreImageUrl(url, PreferredImageHosts))
            .FirstOrDefault();
    }

    private static bool IsUsefulImage(string url) =>
        !url.Contains("sprite", StringComparison.OrdinalIgnoreCase) &&
        !url.Contains("transparent", StringComparison.OrdinalIgnoreCase) &&
        !url.Contains("icon", StringComparison.OrdinalIgnoreCase) &&
        !url.Contains("logo", StringComparison.OrdinalIgnoreCase) &&
        !url.Contains("loading", StringComparison.OrdinalIgnoreCase);

    private static int ScoreImageUrl(string url, IReadOnlyList<string> preferredHosts)
    {
        var score = url.Length;
        if (preferredHosts.Any(host => url.Contains(host, StringComparison.OrdinalIgnoreCase))) score += 1000;
        if (url.Contains("product", StringComparison.OrdinalIgnoreCase)) score += 200;
        if (url.Contains("images/I/", StringComparison.OrdinalIgnoreCase)) score += 200;
        if (url.Contains("s-l1600", StringComparison.OrdinalIgnoreCase)) score += 200;
        if (url.Contains("odnHeight", StringComparison.OrdinalIgnoreCase)) score += 150;
        return score;
    }

    private static string? NormalizePlatform(string? platform)
    {
        var normalized = platform?.Trim().ToLowerInvariant();
        return normalized is "amazon" or "walmart" or "ebay" or "etsy" ? normalized : null;
    }

    private static string? PlatformFromHost(string host)
    {
        var normalized = host.ToLowerInvariant();
        if (normalized.Contains("amazon.")) return "amazon";
        if (normalized.Contains("walmart.")) return "walmart";
        if (normalized.Contains("ebay.")) return "ebay";
        if (normalized.Contains("etsy.")) return "etsy";
        return null;
    }

    private static string NormalizeImageUrl(string url, Uri? baseUri = null)
    {
        var decoded = WebUtility.HtmlDecode(url).Replace("\\/", "/");
        try
        {
            decoded = JsonSerializer.Deserialize<string>($"\"{decoded}\"") ?? decoded;
        }
        catch (JsonException)
        {
            // Keep the decoded URL when it is not a JSON escaped string.
        }

        if (baseUri is not null && Uri.TryCreate(baseUri, decoded, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return decoded;
    }
}
