using System.Text.RegularExpressions;

namespace LocalProductManager.Api.Services;

public sealed class VideoTrendLinkParser
{
    private static readonly Regex YouTubeShortRegex = new(@"youtu\.be/(?<id>[A-Za-z0-9_-]{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YouTubeWatchRegex = new(@"[?&]v=(?<id>[A-Za-z0-9_-]{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YouTubeShortsRegex = new(@"youtube\.com/shorts/(?<id>[A-Za-z0-9_-]{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstagramRegex = new(@"instagram\.com/(?:p|reel|tv)/(?<id>[^/?#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TikTokRegex = new(@"tiktok\.com/@(?<author>[^/]+)/video/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PinterestRegex = new(@"pinterest\.com/(?:pin/)?(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FacebookRegex = new(@"facebook\.com/.+/(?:videos|reel)/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public VideoTrendLinkParseResult Parse(string? platform, string url)
    {
        var normalizedUrl = url.Trim();
        var inferred = NormalizePlatform(platform);
        var id = "";
        string? author = null;
        string? embedUrl = null;
        string? thumbnailUrl = null;

        if (TryMatchYouTube(normalizedUrl, out id))
        {
            inferred = "youtube";
            embedUrl = $"https://www.youtube.com/embed/{id}";
            thumbnailUrl = $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
        }
        else if (TryMatch(InstagramRegex, normalizedUrl, out id))
        {
            inferred = "instagram";
        }
        else if (TikTokRegex.Match(normalizedUrl) is { Success: true } tikTokMatch)
        {
            inferred = "tiktok";
            id = tikTokMatch.Groups["id"].Value;
            author = $"@{tikTokMatch.Groups["author"].Value}";
        }
        else if (TryMatch(PinterestRegex, normalizedUrl, out id))
        {
            inferred = "pinterest";
        }
        else if (TryMatch(FacebookRegex, normalizedUrl, out id))
        {
            inferred = "facebook";
        }
        else if (string.IsNullOrWhiteSpace(inferred))
        {
            inferred = InferPlatformFromHost(normalizedUrl);
        }

        return new VideoTrendLinkParseResult(
            string.IsNullOrWhiteSpace(inferred) ? "other" : inferred,
            string.IsNullOrWhiteSpace(id) ? null : id,
            author,
            embedUrl,
            thumbnailUrl);
    }

    private static bool TryMatchYouTube(string url, out string id)
    {
        foreach (var regex in new[] { YouTubeShortRegex, YouTubeWatchRegex, YouTubeShortsRegex })
        {
            var match = regex.Match(url);
            if (match.Success)
            {
                id = match.Groups["id"].Value;
                return true;
            }
        }

        id = "";
        return false;
    }

    private static bool TryMatch(Regex regex, string url, out string id)
    {
        var match = regex.Match(url);
        id = match.Success ? match.Groups["id"].Value : "";
        return match.Success;
    }

    private static string NormalizePlatform(string? platform)
    {
        var normalized = (platform ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "yt" => "youtube",
            "youtube" => "youtube",
            "instagram" or "ins" or "ig" => "instagram",
            "tiktok" or "tik tok" => "tiktok",
            "pinterest" => "pinterest",
            "facebook" or "fb" => "facebook",
            _ => normalized
        };
    }

    private static string InferPlatformFromHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("youtube") || host.Contains("youtu.be")) return "youtube";
        if (host.Contains("instagram")) return "instagram";
        if (host.Contains("tiktok")) return "tiktok";
        if (host.Contains("pinterest")) return "pinterest";
        if (host.Contains("facebook") || host.Contains("fb.watch")) return "facebook";
        return "";
    }
}

public sealed record VideoTrendLinkParseResult(
    string Platform,
    string? PlatformVideoId,
    string? Author,
    string? EmbedUrl,
    string? ThumbnailUrl);
