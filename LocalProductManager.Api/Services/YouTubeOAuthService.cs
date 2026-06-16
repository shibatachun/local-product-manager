using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class YouTubeOAuthService
{
    private const string Scope = "https://www.googleapis.com/auth/youtube.readonly";
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
            token?.ChannelTitle,
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
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("Google token response was empty.");
        var storedToken = new StoredYouTubeToken
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60))
        };

        var channel = await GetChannelAsync(storedToken.AccessToken, cancellationToken);
        storedToken.ChannelId = channel.Id;
        storedToken.ChannelTitle = channel.Title;
        SaveToken(storedToken);
    }

    public async Task<int> SyncChannelMetricsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var token = LoadToken() ?? throw new InvalidOperationException("Connect YouTube before syncing.");
        token = await EnsureAccessTokenAsync(token, cancellationToken);
        var channel = await GetChannelAsync(token.AccessToken, cancellationToken);

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
            account.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var metric = await db.SocialMetricSnapshots.FirstOrDefaultAsync(
            item => item.SocialAccountId == account.Id && item.SnapshotDate == today,
            cancellationToken);
        if (metric is null)
        {
            metric = new SocialMetricSnapshot
            {
                SocialAccountId = account.Id,
                SnapshotDate = today
            };
            db.SocialMetricSnapshots.Add(metric);
        }

        metric.Views = channel.ViewCount;
        metric.Followers = channel.SubscriberCount;
        metric.Shares = channel.VideoCount;
        metric.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        token.ChannelId = channel.Id;
        token.ChannelTitle = channel.Title;
        SaveToken(token);
        return 1;
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
        response.EnsureSuccessStatusCode();

        var refreshed = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("Google refresh response was empty.");
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
            "https://www.googleapis.com/youtube/v3/channels?part=snippet,statistics&mine=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var item = document.RootElement.GetProperty("items").EnumerateArray().FirstOrDefault();
        if (item.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("No YouTube channel was found for this Google account.");
        }

        var statistics = item.GetProperty("statistics");
        return new YouTubeChannelInfo(
            item.GetProperty("id").GetString() ?? "",
            item.GetProperty("snippet").GetProperty("title").GetString() ?? "YouTube Channel",
            ParseInt(statistics, "viewCount"),
            ParseInt(statistics, "subscriberCount"),
            ParseInt(statistics, "videoCount"));
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

    private static int ParseInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        int.TryParse(value.GetString(), out var result)
            ? result
            : 0;

    private static string BuildQuery(Dictionary<string, string?> values) =>
        string.Join("&", values.Select(item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? "")}"));

    private static string RandomUrlSafeString(int bytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record GoogleTokenResponse(
        string AccessToken,
        int ExpiresIn,
        string? RefreshToken);

    private sealed record YouTubeChannelInfo(
        string Id,
        string Title,
        int ViewCount,
        int SubscriberCount,
        int VideoCount);

    private sealed class StoredYouTubeToken
    {
        public string AccessToken { get; set; } = "";
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string? ChannelId { get; set; }
        public string? ChannelTitle { get; set; }
    }
}
