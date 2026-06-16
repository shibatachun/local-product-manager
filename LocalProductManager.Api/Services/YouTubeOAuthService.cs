using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class YouTubeOAuthService
{
    private const string Scope =
        "https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/yt-analytics.readonly";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpClient httpClient;
    private readonly IConfiguration configuration;
    private readonly string tokenPath;
    private readonly string defaultSecretPath;
    private static readonly object StateSync = new();
    private static string? pendingState;

    public YouTubeOAuthService(
        HttpClient httpClient,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        this.httpClient = httpClient;
        this.configuration = configuration;
        tokenPath = Path.Combine(environment.ContentRootPath, "Data", "youtube-oauth-token.json");
        defaultSecretPath = Path.Combine(environment.ContentRootPath, "Data", "youtube-client-secret.json");
    }

    public YouTubeAuthStatusDto GetStatus()
    {
        var configured = IsConfigured(out _, out _);
        var token = LoadToken();
        return new YouTubeAuthStatusDto(
            configured,
            token is not null,
            token?.ChannelId,
            token?.ChannelTitle,
            token?.ChannelThumbnailUrl,
            configured
                ? null
                : "Put the Google OAuth client secret file at Data/youtube-client-secret.json or set YouTubeOAuth:ClientId and YouTubeOAuth:ClientSecret.",
            RedirectUri());
    }

    public string RedirectUri() =>
        configuration["YouTubeOAuth:RedirectUri"] ??
        "http://localhost:5077/api/youtube/oauth/callback";

    public string BuildAuthorizationUrl(string redirectUri)
    {
        if (!IsConfigured(out var clientId, out _))
        {
            throw new InvalidOperationException("YouTube OAuth is not configured.");
        }

        var state = RandomUrlSafeString(32);
        lock (StateSync)
        {
            pendingState = state;
        }

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        };

        return "https://accounts.google.com/o/oauth2/v2/auth?" + BuildQuery(query);
    }

    public async Task CompleteAuthorizationAsync(
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured(out var clientId, out var clientSecret))
        {
            throw new InvalidOperationException("YouTube OAuth is not configured.");
        }

        lock (StateSync)
        {
            if (!string.Equals(pendingState, state, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("OAuth state did not match.");
            }
            pendingState = null;
        }

        var response = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            }),
            cancellationToken);
        await EnsureGoogleSuccessAsync(response, "Google token exchange failed.", cancellationToken);

        var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("Google token response was empty.");
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Google token response did not include an access token.");
        }

        var storedToken = new StoredYouTubeToken
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60))
        };

        var channel = await GetChannelAsync(storedToken.AccessToken, cancellationToken);
        storedToken.ChannelId = channel.Id;
        storedToken.ChannelTitle = channel.Title;
        storedToken.ChannelThumbnailUrl = channel.ThumbnailUrl;
        storedToken.UploadsPlaylistId = channel.UploadsPlaylistId;
        SaveToken(storedToken);
    }

    public async Task<int> SyncChannelMetricsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var token = LoadToken() ?? throw new InvalidOperationException("Connect YouTube before syncing.");
        token = await EnsureAccessTokenAsync(token, cancellationToken);
        var channel = await GetChannelAsync(token.AccessToken, cancellationToken);

        var account = await UpsertYouTubeAccountAsync(db, channel, cancellationToken);
        await UpsertChannelSnapshotAsync(db, account.Id, channel, DateOnly.FromDateTime(DateTime.Now), cancellationToken);

        token.ChannelId = channel.Id;
        token.ChannelTitle = channel.Title;
        token.ChannelThumbnailUrl = channel.ThumbnailUrl;
        token.UploadsPlaylistId = channel.UploadsPlaylistId;
        SaveToken(token);
        return 1;
    }

    public async Task<int> RecordDailySnapshotAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var token = LoadToken();
        if (token is null)
        {
            return 0;
        }

        token = await EnsureAccessTokenAsync(token, cancellationToken);
        var channel = await GetChannelAsync(token.AccessToken, cancellationToken);
        var account = await UpsertYouTubeAccountAsync(db, channel, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await UpsertChannelSnapshotAsync(db, account.Id, channel, today, cancellationToken);

        var videoIds = await GetRecentUploadVideoIdsAsync(
            token.AccessToken,
            channel.UploadsPlaylistId,
            10,
            cancellationToken);
        var videos = videoIds.Count == 0
            ? []
            : await GetVideosAsync(token.AccessToken, videoIds, cancellationToken);
        await UpsertVideoSnapshotsAsync(db, account.Id, videos, today, cancellationToken);

        token.ChannelId = channel.Id;
        token.ChannelTitle = channel.Title;
        token.ChannelThumbnailUrl = channel.ThumbnailUrl;
        token.UploadsPlaylistId = channel.UploadsPlaylistId;
        SaveToken(token);
        return 1 + videos.Count;
    }

    public async Task<YouTubeDebugSnapshotDto> GetDebugSnapshotAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var token = LoadToken() ?? throw new InvalidOperationException("Connect YouTube before fetching videos.");
        token = await EnsureAccessTokenAsync(token, cancellationToken);
        var channel = await GetChannelAsync(token.AccessToken, cancellationToken);
        var videoIds = await GetRecentUploadVideoIdsAsync(
            token.AccessToken,
            channel.UploadsPlaylistId,
            10,
            cancellationToken);
        var videos = videoIds.Count == 0
            ? []
            : await GetVideosAsync(token.AccessToken, videoIds, cancellationToken);
        var account = await UpsertYouTubeAccountAsync(db, channel, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await UpsertChannelSnapshotAsync(db, account.Id, channel, today, cancellationToken);
        await UpsertVideoSnapshotsAsync(db, account.Id, videos, today, cancellationToken);
        videos = await AttachVideoHistoryAsync(db, account.Id, videos, cancellationToken);

        token.ChannelId = channel.Id;
        token.ChannelTitle = channel.Title;
        token.ChannelThumbnailUrl = channel.ThumbnailUrl;
        token.UploadsPlaylistId = channel.UploadsPlaylistId;
        SaveToken(token);

        return new YouTubeDebugSnapshotDto(
            new YouTubeDebugChannelDto(
                channel.Id,
                channel.Title,
                channel.ThumbnailUrl,
                channel.SubscriberCount,
                channel.ViewCount,
                channel.VideoCount,
                null,
                "YouTube Data API v3 does not expose marketing link click counts here; video views are logged separately."),
            videos);
    }

    public async Task<YouTubeDebugSnapshotDto?> GetStoredSnapshotAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Platform == "youtube", cancellationToken);
        if (account is null)
        {
            return null;
        }

        var token = LoadToken();
        var channelMetric = await db.SocialMetricSnapshots
            .AsNoTracking()
            .Where(item => item.SocialAccountId == account.Id)
            .OrderByDescending(item => item.SnapshotDate)
            .FirstOrDefaultAsync(cancellationToken);

        var videoRows = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Where(item => item.SocialAccountId == account.Id)
            .OrderBy(item => item.SnapshotDate)
            .ToListAsync(cancellationToken);
        var videos = videoRows
            .GroupBy(item => item.PlatformVideoId)
            .Select(group =>
            {
                var latest = group.OrderByDescending(item => item.SnapshotDate).First();
                return new YouTubeDebugVideoDto(
                    latest.PlatformVideoId,
                    latest.Title,
                    latest.ThumbnailUrl,
                    latest.PublishedAt,
                    latest.Views,
                    latest.Likes,
                    latest.Comments,
                    group.Select(item => new YouTubeVideoMetricPointDto(
                        item.SnapshotDate,
                        item.Views,
                        item.Likes,
                        item.Comments,
                        item.Clicks)).ToArray());
            })
            .OrderByDescending(item => item.PublishedAt)
            .ToArray();

        return new YouTubeDebugSnapshotDto(
            new YouTubeDebugChannelDto(
                account.AccountHandle ?? token?.ChannelId ?? "",
                account.AccountName,
                token?.ChannelThumbnailUrl,
                channelMetric?.Followers ?? 0,
                channelMetric?.Views ?? 0,
                channelMetric?.Shares ?? videos.Length,
                null,
                "YouTube Data API v3 does not expose marketing link click counts here; video views are logged separately."),
            videos);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var token = LoadToken();
        if (token is not null)
        {
            var revokeToken = !string.IsNullOrWhiteSpace(token.RefreshToken)
                ? token.RefreshToken
                : token.AccessToken;
            if (!string.IsNullOrWhiteSpace(revokeToken))
            {
                try
                {
                    await httpClient.PostAsync(
                        "https://oauth2.googleapis.com/revoke",
                        new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["token"] = revokeToken
                        }),
                        cancellationToken);
                }
                catch (HttpRequestException)
                {
                    // Local disconnect should still clear the stored token if Google revoke is unreachable.
                }
            }
        }

        if (File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
        }
    }

    private async Task<StoredYouTubeToken> EnsureAccessTokenAsync(
        StoredYouTubeToken token,
        CancellationToken cancellationToken)
    {
        if (token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return token;
        }

        if (!IsConfigured(out var clientId, out var clientSecret) ||
            string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidOperationException("YouTube refresh token is missing.");
        }

        var response = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = token.RefreshToken,
                ["grant_type"] = "refresh_token"
            }),
            cancellationToken);
        await EnsureGoogleSuccessAsync(response, "Google token refresh failed.", cancellationToken);

        var refreshed = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("Google refresh response was empty.");
        if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
        {
            throw new InvalidOperationException("Google refresh response did not include an access token.");
        }

        token.AccessToken = refreshed.AccessToken;
        token.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresIn - 60));
        SaveToken(token);
        return token;
    }

    private async Task<YouTubeChannelInfo> GetChannelAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.googleapis.com/youtube/v3/channels?part=snippet,statistics,contentDetails&mine=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureGoogleSuccessAsync(response, "YouTube channel request failed.", cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var item = document.RootElement.GetProperty("items").EnumerateArray().FirstOrDefault();
        if (item.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("No YouTube channel was found for this Google account.");
        }

        var statistics = item.GetProperty("statistics");
        var snippet = item.GetProperty("snippet");
        var relatedPlaylists = item
            .GetProperty("contentDetails")
            .GetProperty("relatedPlaylists");
        return new YouTubeChannelInfo(
            item.GetProperty("id").GetString() ?? "",
            snippet.GetProperty("title").GetString() ?? "YouTube Channel",
            GetThumbnailUrl(snippet),
            relatedPlaylists.TryGetProperty("uploads", out var uploads)
                ? uploads.GetString() ?? ""
                : "",
            ParseLong(statistics, "viewCount"),
            ParseLong(statistics, "subscriberCount"),
            ParseLong(statistics, "videoCount"));
    }

    private async Task<IReadOnlyList<string>> GetRecentUploadVideoIdsAsync(
        string accessToken,
        string uploadsPlaylistId,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uploadsPlaylistId))
        {
            return [];
        }

        var url =
            "https://www.googleapis.com/youtube/v3/playlistItems" +
            $"?part=contentDetails&playlistId={Uri.EscapeDataString(uploadsPlaylistId)}&maxResults={maxResults}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureGoogleSuccessAsync(response, "YouTube uploads playlist request failed.", cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item
                .GetProperty("contentDetails")
                .TryGetProperty("videoId", out var videoId)
                    ? videoId.GetString() ?? ""
                    : "")
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
    }

    private async Task<IReadOnlyList<YouTubeDebugVideoDto>> GetVideosAsync(
        string accessToken,
        IReadOnlyList<string> videoIds,
        CancellationToken cancellationToken)
    {
        var url =
            "https://www.googleapis.com/youtube/v3/videos" +
            $"?part=snippet,statistics&id={Uri.EscapeDataString(string.Join(",", videoIds))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureGoogleSuccessAsync(response, "YouTube videos request failed.", cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item =>
            {
                var snippet = item.GetProperty("snippet");
                var statistics = item.GetProperty("statistics");
                return new YouTubeDebugVideoDto(
                    item.GetProperty("id").GetString() ?? "",
                    snippet.GetProperty("title").GetString() ?? "",
                    GetThumbnailUrl(snippet),
                ParseDateTimeOffset(snippet, "publishedAt"),
                ParseLong(statistics, "viewCount"),
                ParseLong(statistics, "likeCount"),
                ParseLong(statistics, "commentCount"),
                []);
            })
            .ToArray();
    }

    private static async Task<SocialAccount> UpsertYouTubeAccountAsync(
        AppDbContext db,
        YouTubeChannelInfo channel,
        CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts.FirstOrDefaultAsync(
            item => item.Platform == "youtube" && item.AccountHandle == channel.Id,
            cancellationToken);
        if (account is null)
        {
            account = new SocialAccount
            {
                Platform = "youtube",
                AccountName = channel.Title,
                AccountHandle = channel.Id,
                AccountUrl = $"https://www.youtube.com/channel/{channel.Id}",
                Color = "#d92727",
                IsActive = true
            };
            db.SocialAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            account.AccountName = channel.Title;
            account.AccountUrl = $"https://www.youtube.com/channel/{channel.Id}";
            account.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return account;
    }

    private static async Task UpsertChannelSnapshotAsync(
        AppDbContext db,
        int accountId,
        YouTubeChannelInfo channel,
        DateOnly snapshotDate,
        CancellationToken cancellationToken)
    {
        var metric = await db.SocialMetricSnapshots.FirstOrDefaultAsync(
            item => item.SocialAccountId == accountId && item.SnapshotDate == snapshotDate,
            cancellationToken);
        if (metric is null)
        {
            metric = new SocialMetricSnapshot
            {
                SocialAccountId = accountId,
                SnapshotDate = snapshotDate
            };
            db.SocialMetricSnapshots.Add(metric);
        }

        metric.Views = ClampToInt(channel.ViewCount);
        metric.Followers = ClampToInt(channel.SubscriberCount);
        metric.Shares = ClampToInt(channel.VideoCount);
        metric.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpsertVideoSnapshotsAsync(
        AppDbContext db,
        int accountId,
        IReadOnlyList<YouTubeDebugVideoDto> videos,
        DateOnly snapshotDate,
        CancellationToken cancellationToken)
    {
        foreach (var video in videos)
        {
            var metric = await db.SocialVideoMetricSnapshots.FirstOrDefaultAsync(
                item =>
                    item.SocialAccountId == accountId &&
                    item.PlatformVideoId == video.Id &&
                    item.SnapshotDate == snapshotDate,
                cancellationToken);
            if (metric is null)
            {
                metric = new SocialVideoMetricSnapshot
                {
                    SocialAccountId = accountId,
                    PlatformVideoId = video.Id,
                    SnapshotDate = snapshotDate
                };
                db.SocialVideoMetricSnapshots.Add(metric);
            }

            metric.Title = video.Title;
            metric.ThumbnailUrl = video.ThumbnailUrl;
            metric.PublishedAt = video.PublishedAt;
            metric.Views = ClampToInt(video.Views);
            metric.Likes = ClampToInt(video.Likes);
            metric.Comments = ClampToInt(video.Comments);
            metric.Clicks = 0;
            metric.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<YouTubeDebugVideoDto>> AttachVideoHistoryAsync(
        AppDbContext db,
        int accountId,
        IReadOnlyList<YouTubeDebugVideoDto> videos,
        CancellationToken cancellationToken)
    {
        var ids = videos.Select(video => video.Id).ToArray();
        var histories = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Where(item => item.SocialAccountId == accountId && ids.Contains(item.PlatformVideoId))
            .OrderBy(item => item.SnapshotDate)
            .ToListAsync(cancellationToken);
        var byVideo = histories
            .GroupBy(item => item.PlatformVideoId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => new YouTubeVideoMetricPointDto(
                    item.SnapshotDate,
                    item.Views,
                    item.Likes,
                    item.Comments,
                    item.Clicks)).ToArray() as IReadOnlyList<YouTubeVideoMetricPointDto>);

        return videos.Select(video => video with
        {
            Metrics = byVideo.TryGetValue(video.Id, out var metrics) ? metrics : []
        }).ToArray();
    }

    private bool IsConfigured(out string clientId, out string clientSecret)
    {
        clientId = configuration["YouTubeOAuth:ClientId"] ?? "";
        clientSecret = configuration["YouTubeOAuth:ClientSecret"] ?? "";
        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
        {
            return true;
        }

        return TryLoadSecretFile(out clientId, out clientSecret);
    }

    private bool TryLoadSecretFile(out string clientId, out string clientSecret)
    {
        clientId = "";
        clientSecret = "";
        var secretPath = configuration["YouTubeOAuth:ClientSecretFile"] ?? defaultSecretPath;
        if (!Path.IsPathRooted(secretPath))
        {
            secretPath = Path.GetFullPath(secretPath);
        }

        try
        {
            if (!File.Exists(secretPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(secretPath));
            var root = document.RootElement;
            if (!TryGetClientSection(root, "web", out var section) &&
                !TryGetClientSection(root, "installed", out section))
            {
                return false;
            }

            clientId = section.TryGetProperty("client_id", out var id) ? id.GetString() ?? "" : "";
            clientSecret = section.TryGetProperty("client_secret", out var secret) ? secret.GetString() ?? "" : "";
            return !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryGetClientSection(JsonElement root, string name, out JsonElement section)
    {
        if (root.TryGetProperty(name, out section) && section.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        section = default;
        return false;
    }

    private StoredYouTubeToken? LoadToken()
    {
        try
        {
            return File.Exists(tokenPath)
                ? JsonSerializer.Deserialize<StoredYouTubeToken>(File.ReadAllText(tokenPath), JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SaveToken(StoredYouTubeToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(tokenPath)!);
        File.WriteAllText(tokenPath, JsonSerializer.Serialize(token, JsonOptions));
    }

    private static long ParseLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        long.TryParse(value.GetString(), out var result)
            ? result
            : 0;

    private static int ClampToInt(long value) =>
        value > int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

    private static DateTimeOffset? ParseDateTimeOffset(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        DateTimeOffset.TryParse(value.GetString(), out var result)
            ? result
            : null;

    private static string? GetThumbnailUrl(JsonElement snippet)
    {
        if (!snippet.TryGetProperty("thumbnails", out var thumbnails))
        {
            return null;
        }

        foreach (var key in new[] { "high", "medium", "default" })
        {
            if (thumbnails.TryGetProperty(key, out var thumbnail) &&
                thumbnail.TryGetProperty("url", out var url))
            {
                return url.GetString();
            }
        }

        return null;
    }

    private static string BuildQuery(Dictionary<string, string?> values) =>
        string.Join("&", values.Select(item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? "")}"));

    private static async Task EnsureGoogleSuccessAsync(
        HttpResponseMessage response,
        string prefix,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var hint = response.StatusCode == System.Net.HttpStatusCode.Forbidden
            ? " Check that YouTube Data API v3 is enabled in Google Cloud, the OAuth consent screen includes the requested scopes, and reconnect YouTube after changing scopes."
            : "";
        throw new HttpRequestException(
            $"{prefix} HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {body}{hint}");
    }

    private static string RandomUrlSafeString(int bytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);

    private sealed record YouTubeChannelInfo(
        string Id,
        string Title,
        string? ThumbnailUrl,
        string UploadsPlaylistId,
        long ViewCount,
        long SubscriberCount,
        long VideoCount);

    private sealed class StoredYouTubeToken
    {
        public string AccessToken { get; set; } = "";
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string? ChannelId { get; set; }
        public string? ChannelTitle { get; set; }
        public string? ChannelThumbnailUrl { get; set; }
        public string? UploadsPlaylistId { get; set; }
    }
}
