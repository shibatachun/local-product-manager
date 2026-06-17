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

public sealed class PinterestOAuthService
{
    private const string DefaultScope = "user_accounts:read,pins:read,boards:read";
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

    public PinterestOAuthService(
        HttpClient httpClient,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        this.httpClient = httpClient;
        this.configuration = configuration;
        tokenPath = Path.Combine(environment.ContentRootPath, "Data", "pinterest-oauth-token.json");
        defaultSecretPath = Path.Combine(environment.ContentRootPath, "Data", "pinterest-client-secret.json");
    }

    public PinterestAuthStatusDto GetStatus(bool isAdminHost)
    {
        var configured = IsConfigured(out _, out _);
        var token = LoadToken();
        return new PinterestAuthStatusDto(
            configured,
            token is not null,
            token?.AccountId,
            token?.AccountName,
            token?.AccountThumbnailUrl,
            configured
                ? null
                : "Put the Pinterest OAuth client secret file at Data/pinterest-client-secret.json or set PinterestOAuth:ClientId and PinterestOAuth:ClientSecret.",
            RedirectUri(),
            isAdminHost);
    }

    public string RedirectUri() =>
        configuration["PinterestOAuth:RedirectUri"] ??
        "http://localhost:5077/api/pinterest/oauth/callback";

    public string BuildAuthorizationUrl(string redirectUri)
    {
        if (!IsConfigured(out var clientId, out _))
        {
            throw new InvalidOperationException("Pinterest OAuth is not configured.");
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

        return "https://www.pinterest.com/oauth/?" + BuildQuery(query);
    }

    public async Task CompleteAuthorizationAsync(
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured(out var clientId, out var clientSecret))
        {
            throw new InvalidOperationException("Pinterest OAuth is not configured.");
        }

        lock (StateSync)
        {
            if (!string.Equals(pendingState, state, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("OAuth state did not match.");
            }
            pendingState = null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.pinterest.com/v5/oauth/token");
        request.Headers.Authorization = BuildBasicAuth(clientId, clientSecret);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsurePinterestSuccessAsync(response, "Pinterest token exchange failed.", cancellationToken);

        var token = await response.Content.ReadFromJsonAsync<PinterestTokenResponse>(
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("Pinterest token response was empty.");
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Pinterest token response did not include an access token.");
        }

        var storedToken = new StoredPinterestToken
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60))
        };

        var account = await GetAccountAsync(storedToken.AccessToken, cancellationToken);
        storedToken.AccountId = account.Id;
        storedToken.AccountName = account.Name;
        storedToken.AccountThumbnailUrl = account.ThumbnailUrl;
        storedToken.Username = account.Username;
        SaveToken(storedToken);
    }

    public async Task<int> SyncAccountMetricsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var token = LoadToken() ?? throw new InvalidOperationException("Connect Pinterest before syncing.");
        token = await EnsureAccessTokenAsync(token, cancellationToken);
        var pinterestAccount = await GetAccountAsync(token.AccessToken, cancellationToken);

        var account = await UpsertPinterestAccountAsync(db, pinterestAccount, cancellationToken);
        await UpsertAccountSnapshotAsync(
            db,
            account.Id,
            pinterestAccount,
            DateOnly.FromDateTime(DateTime.Now),
            cancellationToken);

        token.AccountId = pinterestAccount.Id;
        token.AccountName = pinterestAccount.Name;
        token.AccountThumbnailUrl = pinterestAccount.ThumbnailUrl;
        token.Username = pinterestAccount.Username;
        SaveToken(token);
        return 1;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask.WaitAsync(cancellationToken);
        if (File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
        }
    }

    private async Task<StoredPinterestToken> EnsureAccessTokenAsync(
        StoredPinterestToken token,
        CancellationToken cancellationToken)
    {
        if (token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return token;
        }

        if (!IsConfigured(out var clientId, out var clientSecret) ||
            string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidOperationException("Pinterest refresh token is missing.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.pinterest.com/v5/oauth/token");
        request.Headers.Authorization = BuildBasicAuth(clientId, clientSecret);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refresh_token"] = token.RefreshToken,
            ["grant_type"] = "refresh_token"
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsurePinterestSuccessAsync(response, "Pinterest token refresh failed.", cancellationToken);

        var refreshed = await response.Content.ReadFromJsonAsync<PinterestTokenResponse>(
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("Pinterest refresh response was empty.");
        if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
        {
            throw new InvalidOperationException("Pinterest refresh response did not include an access token.");
        }

        token.AccessToken = refreshed.AccessToken;
        if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
        {
            token.RefreshToken = refreshed.RefreshToken;
        }
        token.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresIn - 60));
        SaveToken(token);
        return token;
    }

    private async Task<PinterestAccountInfo> GetAccountAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.pinterest.com/v5/user_account");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsurePinterestSuccessAsync(response, "Pinterest account request failed.", cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var username = ReadString(root, "username");
        var id = ReadString(root, "id") ?? username ?? "pinterest-account";
        var name = ReadString(root, "business_name") ??
            ReadString(root, "name") ??
            username ??
            "Pinterest Account";
        var thumbnailUrl = ReadString(root, "profile_image") ??
            ReadString(root, "profile_image_url") ??
            ReadString(root, "image");
        var followers = ReadLong(root, "follower_count") ??
            ReadLong(root, "followers_count") ??
            ReadLong(root, "followers") ??
            0;
        var views = ReadLong(root, "monthly_views") ??
            ReadLong(root, "monthly_viewer_count") ??
            ReadLong(root, "profile_views") ??
            0;
        var url = !string.IsNullOrWhiteSpace(username)
            ? $"https://www.pinterest.com/{username}/"
            : null;

        return new PinterestAccountInfo(id, name, username, thumbnailUrl, url, followers, views);
    }

    private static async Task<SocialAccount> UpsertPinterestAccountAsync(
        AppDbContext db,
        PinterestAccountInfo pinterestAccount,
        CancellationToken cancellationToken)
    {
        var handle = pinterestAccount.Username ?? pinterestAccount.Id;
        var account = await db.SocialAccounts.FirstOrDefaultAsync(
            item => item.Platform == "pinterest" && item.AccountHandle == handle,
            cancellationToken);
        if (account is null)
        {
            account = new SocialAccount
            {
                Platform = "pinterest",
                AccountName = pinterestAccount.Name,
                AccountHandle = handle,
                AccountUrl = pinterestAccount.Url,
                Color = "#bd081c",
                IsActive = true
            };
            db.SocialAccounts.Add(account);
        }
        else
        {
            account.AccountName = pinterestAccount.Name;
            account.AccountUrl = pinterestAccount.Url;
            account.Color ??= "#bd081c";
            account.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    private static async Task UpsertAccountSnapshotAsync(
        AppDbContext db,
        int accountId,
        PinterestAccountInfo pinterestAccount,
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

        metric.Followers = ClampToInt(pinterestAccount.FollowerCount);
        metric.Views = ClampToInt(pinterestAccount.MonthlyViews);
        metric.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private bool IsConfigured(out string clientId, out string clientSecret)
    {
        clientId = configuration["PinterestOAuth:ClientId"] ?? "";
        clientSecret = configuration["PinterestOAuth:ClientSecret"] ?? "";
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
        var secretPath = configuration["PinterestOAuth:ClientSecretFile"] ?? defaultSecretPath;
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
                 TryGetClientSection(root, "pinterest", out webSection)))
            {
                section = webSection;
            }

            clientId = section.TryGetProperty("client_id", out var id)
                ? id.GetString() ?? ""
                : section.TryGetProperty("clientId", out id)
                    ? id.GetString() ?? ""
                    : "";
            clientSecret = section.TryGetProperty("client_secret", out var secret)
                ? secret.GetString() ?? ""
                : section.TryGetProperty("clientSecret", out secret)
                    ? secret.GetString() ?? ""
                    : "";
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

    private string Scope() => configuration["PinterestOAuth:Scopes"] ?? DefaultScope;

    private StoredPinterestToken? LoadToken()
    {
        try
        {
            return File.Exists(tokenPath)
                ? JsonSerializer.Deserialize<StoredPinterestToken>(File.ReadAllText(tokenPath), JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void SaveToken(StoredPinterestToken token)
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

    private static AuthenticationHeaderValue BuildBasicAuth(string clientId, string clientSecret)
    {
        var raw = $"{clientId}:{clientSecret}";
        return new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
        {
            return numeric;
        }

        return long.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static int ClampToInt(long value) =>
        value > int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

    private static string BuildQuery(Dictionary<string, string?> values) =>
        string.Join("&", values.Select(item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? "")}"));

    private static async Task EnsurePinterestSuccessAsync(
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
            $"{prefix} HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {body} Check that Pinterest API access is enabled and the OAuth app includes the requested scopes.");
    }

    private static string RandomUrlSafeString(int bytes)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record PinterestTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);

    private sealed record PinterestAccountInfo(
        string Id,
        string Name,
        string? Username,
        string? ThumbnailUrl,
        string? Url,
        long FollowerCount,
        long MonthlyViews);

    private sealed class StoredPinterestToken
    {
        public string AccessToken { get; set; } = "";
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string? AccountId { get; set; }
        public string? AccountName { get; set; }
        public string? AccountThumbnailUrl { get; set; }
        public string? Username { get; set; }
    }
}
