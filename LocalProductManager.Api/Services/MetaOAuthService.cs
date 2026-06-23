using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class MetaOAuthService
{
    private const string DefaultScope = "pages_show_list,pages_read_engagement,pages_read_user_content,read_insights,instagram_basic,instagram_manage_insights";
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

    public MetaOAuthService(
        HttpClient httpClient,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        this.httpClient = httpClient;
        this.configuration = configuration;
        tokenPath = Path.Combine(environment.ContentRootPath, "Data", "meta-oauth-token.json");
        defaultSecretPath = Path.Combine(environment.ContentRootPath, "Data", "meta-client-secret.json");
    }

    public MetaAuthStatusDto GetStatus(bool isAdminHost)
    {
        var configured = IsConfigured(out _, out _);
        var token = LoadToken();
        return new MetaAuthStatusDto(
            configured,
            token is not null,
            token?.UserId,
            token?.UserName,
            token?.Pages.Count ?? 0,
            token?.Pages.Count(page => page.InstagramAccount is not null) ?? 0,
            configured
                ? null
                : "Put the Meta OAuth secret file at Data/meta-client-secret.json or set MetaOAuth:ClientId and MetaOAuth:ClientSecret.",
            RedirectUri(),
            isAdminHost);
    }

    public string RedirectUri() =>
        configuration["MetaOAuth:RedirectUri"] ??
        "http://localhost:5077/api/meta/oauth/callback";

    public string BuildAuthorizationUrl(string redirectUri)
    {
        if (!IsConfigured(out var clientId, out _))
        {
            throw new InvalidOperationException("Meta OAuth is not configured.");
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
            ["scope"] = Scope(),
            ["state"] = state
        };

        return $"https://www.facebook.com/{GraphVersion}/dialog/oauth?" + BuildQuery(query);
    }

    public async Task CompleteAuthorizationAsync(
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured(out var clientId, out var clientSecret))
        {
            throw new InvalidOperationException("Meta OAuth is not configured.");
        }

        lock (StateSync)
        {
            if (!string.Equals(pendingState, state, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("OAuth state did not match.");
            }

            pendingState = null;
        }

        var tokenUrl = $"https://graph.facebook.com/{GraphVersion}/oauth/access_token?" + BuildQuery(new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["code"] = code
        });
        using var tokenResponse = await httpClient.GetAsync(tokenUrl, cancellationToken);
        await EnsureMetaSuccessAsync(tokenResponse, "Meta token exchange failed.", cancellationToken);
        var token = await tokenResponse.Content.ReadFromJsonAsync<MetaTokenResponse>(
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("Meta token response was empty.");
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Meta token response did not include an access token.");
        }

        var longLivedToken = await ExchangeLongLivedTokenAsync(
            token.AccessToken,
            clientId,
            clientSecret,
            cancellationToken);
        var profile = await GetUserProfileAsync(longLivedToken.AccessToken, cancellationToken);
        var pages = await GetPagesAsync(longLivedToken.AccessToken, cancellationToken);

        SaveToken(new StoredMetaToken
        {
            AccessToken = longLivedToken.AccessToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, longLivedToken.ExpiresIn - 60)),
            UserId = profile.Id,
            UserName = profile.Name,
            Pages = pages.ToList()
        });
    }

    public async Task<int> SyncAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var token = LoadToken() ?? throw new InvalidOperationException("Connect Meta before syncing.");
        if (token.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            throw new InvalidOperationException("Meta token expired. Reconnect Meta from the host computer.");
        }

        var freshPages = await GetPagesAsync(token.AccessToken, cancellationToken);
        token.Pages = freshPages.ToList();
        SaveToken(token);

        var imported = 0;
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var page in freshPages)
        {
            var pageDetails = await GetPageDetailsAsync(page.Id, page.AccessToken, cancellationToken);
            var facebookAccount = await UpsertSocialAccountAsync(
                db,
                "facebook",
                pageDetails.Name,
                pageDetails.Id,
                pageDetails.Link,
                "#1877f2",
                cancellationToken);
            await UpsertSnapshotAsync(
                db,
                facebookAccount.Id,
                today,
                views: pageDetails.FanCount,
                likes: pageDetails.FanCount,
                comments: 0,
                clicks: 0,
                shares: 0,
                followers: pageDetails.FollowersCount,
                cancellationToken);
            imported++;

            if (page.InstagramAccount is null)
            {
                continue;
            }

            var instagram = await GetInstagramAccountAsync(
                page.InstagramAccount.Id,
                page.AccessToken,
                cancellationToken);
            var instagramAccount = await UpsertSocialAccountAsync(
                db,
                "instagram",
                instagram.Name ?? instagram.Username ?? "Instagram Account",
                instagram.Username ?? instagram.Id,
                instagram.Permalink,
                "#c13584",
                cancellationToken);
            await UpsertSnapshotAsync(
                db,
                instagramAccount.Id,
                today,
                views: instagram.MediaCount,
                likes: 0,
                comments: 0,
                clicks: 0,
                shares: 0,
                followers: instagram.FollowersCount,
                cancellationToken);
            imported++;
        }

        return imported;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.WaitAsync(cancellationToken);
        if (File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
        }
    }

    private async Task<MetaTokenResponse> ExchangeLongLivedTokenAsync(
        string shortLivedAccessToken,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var url = $"https://graph.facebook.com/{GraphVersion}/oauth/access_token?" + BuildQuery(new Dictionary<string, string?>
        {
            ["grant_type"] = "fb_exchange_token",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["fb_exchange_token"] = shortLivedAccessToken
        });
        using var response = await httpClient.GetAsync(url, cancellationToken);
        await EnsureMetaSuccessAsync(response, "Meta long-lived token exchange failed.", cancellationToken);
        return await response.Content.ReadFromJsonAsync<MetaTokenResponse>(JsonOptions, cancellationToken) ??
            throw new InvalidOperationException("Meta long-lived token response was empty.");
    }

    private async Task<MetaUserProfile> GetUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"https://graph.facebook.com/{GraphVersion}/me?fields=id,name&access_token={Uri.EscapeDataString(accessToken)}",
            cancellationToken);
        await EnsureMetaSuccessAsync(response, "Meta profile request failed.", cancellationToken);
        return await response.Content.ReadFromJsonAsync<MetaUserProfile>(JsonOptions, cancellationToken) ??
            throw new InvalidOperationException("Meta profile response was empty.");
    }

    private async Task<IReadOnlyList<MetaPageToken>> GetPagesAsync(
        string userAccessToken,
        CancellationToken cancellationToken)
    {
        var url = $"https://graph.facebook.com/{GraphVersion}/me/accounts?fields=id,name,access_token,link,instagram_business_account{{id,username,name}}&limit=100&access_token={Uri.EscapeDataString(userAccessToken)}";
        var pages = new List<MetaPageToken>();
        while (!string.IsNullOrWhiteSpace(url))
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            await EnsureMetaSuccessAsync(response, "Meta pages request failed.", cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<MetaPageListResponse>(JsonOptions, cancellationToken) ??
                throw new InvalidOperationException("Meta pages response was empty.");
            pages.AddRange(result.Data.Where(page => !string.IsNullOrWhiteSpace(page.AccessToken)));
            url = result.Paging?.Next;
        }

        return pages;
    }

    private async Task<MetaPageDetails> GetPageDetailsAsync(
        string pageId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var fields = "id,name,link,fan_count,followers_count";
        using var response = await httpClient.GetAsync(
            $"https://graph.facebook.com/{GraphVersion}/{Uri.EscapeDataString(pageId)}?fields={Uri.EscapeDataString(fields)}&access_token={Uri.EscapeDataString(pageAccessToken)}",
            cancellationToken);
        await EnsureMetaSuccessAsync(response, "Meta page details request failed.", cancellationToken);
        return await response.Content.ReadFromJsonAsync<MetaPageDetails>(JsonOptions, cancellationToken) ??
            throw new InvalidOperationException("Meta page details response was empty.");
    }

    private async Task<MetaInstagramAccount> GetInstagramAccountAsync(
        string instagramUserId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var fields = "id,username,name,profile_picture_url,followers_count,media_count";
        using var response = await httpClient.GetAsync(
            $"https://graph.facebook.com/{GraphVersion}/{Uri.EscapeDataString(instagramUserId)}?fields={Uri.EscapeDataString(fields)}&access_token={Uri.EscapeDataString(pageAccessToken)}",
            cancellationToken);
        await EnsureMetaSuccessAsync(response, "Meta Instagram account request failed.", cancellationToken);
        var account = await response.Content.ReadFromJsonAsync<MetaInstagramAccount>(JsonOptions, cancellationToken) ??
            throw new InvalidOperationException("Meta Instagram account response was empty.");
        return account with
        {
            Permalink = string.IsNullOrWhiteSpace(account.Username)
                ? null
                : $"https://www.instagram.com/{account.Username}/"
        };
    }

    private static async Task<SocialAccount> UpsertSocialAccountAsync(
        AppDbContext db,
        string platform,
        string accountName,
        string accountHandle,
        string? accountUrl,
        string color,
        CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts.FirstOrDefaultAsync(
            item => item.Platform == platform && item.AccountHandle == accountHandle,
            cancellationToken);
        if (account is null)
        {
            account = new SocialAccount
            {
                Platform = platform,
                AccountName = accountName,
                AccountHandle = accountHandle,
                AccountUrl = accountUrl,
                Color = color,
                IsActive = true
            };
            db.SocialAccounts.Add(account);
        }
        else
        {
            account.AccountName = accountName;
            account.AccountUrl = accountUrl;
            account.Color ??= color;
            account.IsActive = true;
            account.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    private static async Task UpsertSnapshotAsync(
        AppDbContext db,
        int accountId,
        DateOnly snapshotDate,
        int views,
        int likes,
        int comments,
        int clicks,
        int shares,
        int followers,
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

        metric.Views = Math.Max(0, views);
        metric.Likes = Math.Max(0, likes);
        metric.Comments = Math.Max(0, comments);
        metric.Clicks = Math.Max(0, clicks);
        metric.Shares = Math.Max(0, shares);
        metric.Followers = Math.Max(0, followers);
        metric.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private bool IsConfigured(out string clientId, out string clientSecret)
    {
        clientId = configuration["MetaOAuth:ClientId"] ?? "";
        clientSecret = configuration["MetaOAuth:ClientSecret"] ?? "";
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
        var secretPath = configuration["MetaOAuth:ClientSecretFile"] ?? defaultSecretPath;
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
            var section = root;
            if (root.ValueKind == JsonValueKind.Object &&
                (TryGetClientSection(root, "web", out var webSection) ||
                 TryGetClientSection(root, "installed", out webSection) ||
                 TryGetClientSection(root, "meta", out webSection)))
            {
                section = webSection;
            }

            clientId = ReadString(section, "client_id") ?? ReadString(section, "clientId") ?? "";
            clientSecret = ReadString(section, "client_secret") ?? ReadString(section, "clientSecret") ?? "";
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

    private string Scope() => configuration["MetaOAuth:Scopes"] ?? DefaultScope;
    private string GraphVersion => configuration["MetaOAuth:GraphVersion"] ?? "v20.0";

    private StoredMetaToken? LoadToken()
    {
        try
        {
            return File.Exists(tokenPath)
                ? JsonSerializer.Deserialize<StoredMetaToken>(File.ReadAllText(tokenPath), JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SaveToken(StoredMetaToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(tokenPath)!);
        File.WriteAllText(tokenPath, JsonSerializer.Serialize(token, JsonOptions));
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

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static string BuildQuery(Dictionary<string, string?> values) =>
        string.Join("&", values.Select(item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? "")}"));

    private static async Task EnsureMetaSuccessAsync(
        HttpResponseMessage response,
        string prefix,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"{prefix} HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {body} Check Meta App Review permissions and reconnect after scope changes.");
    }

    private static string RandomUrlSafeString(int bytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record MetaTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType);

    private sealed record MetaUserProfile(string Id, string Name);

    private sealed record MetaPageListResponse(
        IReadOnlyList<MetaPageToken> Data,
        MetaPaging? Paging);

    private sealed record MetaPaging(string? Next);

    private sealed record MetaPageToken(
        string Id,
        string Name,
        [property: JsonPropertyName("access_token")] string AccessToken,
        string? Link,
        [property: JsonPropertyName("instagram_business_account")] MetaInstagramRef? InstagramAccount);

    private sealed record MetaInstagramRef(string Id, string? Username, string? Name);

    private sealed record MetaPageDetails(
        string Id,
        string Name,
        string? Link,
        [property: JsonPropertyName("fan_count")] int FanCount,
        [property: JsonPropertyName("followers_count")] int FollowersCount);

    private sealed record MetaInstagramAccount(
        string Id,
        string? Username,
        string? Name,
        [property: JsonPropertyName("profile_picture_url")] string? ProfilePictureUrl,
        [property: JsonPropertyName("followers_count")] int FollowersCount,
        [property: JsonPropertyName("media_count")] int MediaCount,
        string? Permalink);

    private sealed class StoredMetaToken
    {
        public string AccessToken { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public List<MetaPageToken> Pages { get; set; } = [];
    }
}
