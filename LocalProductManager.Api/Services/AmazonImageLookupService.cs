using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalProductManager.Api.Services;

public sealed class AmazonImageLookupService(HttpClient httpClient)
{
    private static readonly Regex OgImageRegex = new(
        """<meta[^>]+(?:property|name)=["']og:image["'][^>]*>""",
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

    public async Task<string> FindImageUrlAsync(
        string listingUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(listingUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Amazon listing URL must be an HTTP or HTTPS URL.");
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
        var imageUrl = FindMetaImage(html) ??
            FindLandingImage(html) ??
            FindDataOldHiresImage(html) ??
            FindDynamicImage(html) ??
            FindAmazonMediaImage(html);
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new InvalidOperationException("Could not find a product image on the Amazon page.");
        }

        return NormalizeImageUrl(imageUrl);
    }

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
            .Where(url =>
                !url.Contains("sprite", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("transparent", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches
            .OrderByDescending(url => url.Contains("images/I/", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(url => url.Length)
            .FirstOrDefault();
    }

    private static string NormalizeImageUrl(string url)
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

        return decoded;
    }
}
