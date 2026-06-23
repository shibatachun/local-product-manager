using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace LocalProductManager.Api.Services;

public sealed class FacebookReportImportService(AppDbContext db)
{
    private static readonly string[] FacebookIdKeys =
    [
        "Video asset ID", "Universal video ID", "Post ID", "PostId", "ID"
    ];

    private static readonly string[] FacebookUrlKeys =
    [
        "Permalink", "Post permalink", "URL", "Post URL", "Link"
    ];

    private static readonly string[] FacebookViewKeys =
    [
        "3-second video views", "3-Second viewers", "Views", "Video views",
        "Impressions", "Lifetime Post Total Impressions", "Lifetime Post total impressions",
        "Reach", "Lifetime Post Total Reach"
    ];

    private static readonly string[] FacebookLikeKeys =
    [
        "Reactions", "Likes", "Lifetime Post Reactions", "Lifetime Post Stories by action type - like"
    ];

    private static readonly string[] FacebookCommentKeys =
    [
        "Comments", "Lifetime Post Comments", "Comment count"
    ];

    private static readonly string[] FacebookShareKeys =
    [
        "Shares", "Lifetime Post Shares", "Share count"
    ];

    private static readonly string[] FacebookClickKeys =
    [
        "Clicks", "Post clicks", "Lifetime Post Consumers", "Link clicks", "Other clicks"
    ];

    private static readonly string[] FacebookFollowerKeys =
    [
        "Follows", "Followers", "New followers", "Page follows"
    ];

    private static readonly string[] FacebookPublishedAtKeys =
    [
        "Posted", "Publish time", "Created", "Date", "Post date"
    ];

    private static readonly Regex FacebookPostRegex = new(
        @"facebook\.com/(?:[^/]+/)?(?:posts|videos|reel|photo|permalink\.php|watch)/(?<id>[^/?#]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<FacebookReportImportResult> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var rows = ReadRows(stream);
        if (rows.Count < 2)
        {
            throw new InvalidOperationException("Facebook report did not contain any post rows.");
        }

        var header = rows[0];
        var records = rows.Skip(1)
            .Select(row => ToDictionary(header, row))
            .Where(IsPostRow)
            .ToList();
        if (records.Count == 0)
        {
            throw new InvalidOperationException("Facebook report did not contain recognized post rows.");
        }

        var accountInfo = ExtractAccountInfo(file.FileName, records);
        var account = await UpsertAccountAsync(accountInfo, cancellationToken);
        var snapshotDate = ExtractSnapshotDate(file.FileName, records);
        var totals = records.Aggregate(
            new FacebookTotals(),
            (current, row) => current with
            {
                Views = current.Views + ReadMetric(row, FacebookViewKeys),
                Likes = current.Likes + ReadMetric(row, FacebookLikeKeys),
                Comments = current.Comments + ReadMetric(row, FacebookCommentKeys),
                Shares = current.Shares + ReadMetric(row, FacebookShareKeys),
                Clicks = current.Clicks + ReadMetric(row, FacebookClickKeys),
                Followers = current.Followers + ReadMetric(row, FacebookFollowerKeys)
            });

        var previousMetric = await PreviousAccountMetricAsync(account.Id, snapshotDate, cancellationToken);
        var metric = await GetOrCreateMetricAsync(account.Id, snapshotDate, cancellationToken);
        metric.Views = (previousMetric?.Views ?? 0) + totals.Views;
        metric.Likes = (previousMetric?.Likes ?? 0) + totals.Likes;
        metric.Comments = (previousMetric?.Comments ?? 0) + totals.Comments;
        metric.Shares = (previousMetric?.Shares ?? 0) + totals.Shares;
        metric.Clicks = (previousMetric?.Clicks ?? 0) + totals.Clicks;
        metric.Followers = (previousMetric?.Followers ?? 0) + totals.Followers;
        metric.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var row in records)
        {
            await UpsertPostSnapshotAsync(account.Id, row, snapshotDate, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new FacebookReportImportResult(
            account.Id,
            records.Count,
            totals.Views,
            totals.Likes,
            totals.Comments,
            totals.Shares,
            totals.Clicks,
            account.AccountName,
            snapshotDate);
    }

    public async Task<FacebookContentSnapshotDto?> GetStoredSnapshotAsync(
        int? accountId,
        CancellationToken cancellationToken)
    {
        var query = db.SocialAccounts
            .AsNoTracking()
            .Where(item => item.Platform == "facebook");
        SocialAccount? account = null;
        if (accountId.HasValue)
        {
            account = await query.FirstOrDefaultAsync(item => item.Id == accountId.Value, cancellationToken);
        }

        account ??= await query
            .OrderByDescending(item => db.SocialVideoMetricSnapshots.Any(row => row.SocialAccountId == item.Id))
            .ThenByDescending(item => db.SocialMetricSnapshots.Any(row => row.SocialAccountId == item.Id))
            .ThenByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.AccountName)
            .FirstOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return null;
        }

        var accountMetrics = await db.SocialMetricSnapshots
            .AsNoTracking()
            .Where(item => item.SocialAccountId == account.Id)
            .OrderByDescending(item => item.SnapshotDate)
            .ToListAsync(cancellationToken);
        var latestMetric = accountMetrics.FirstOrDefault();
        var contentRows = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Where(item => item.SocialAccountId == account.Id)
            .OrderBy(item => item.SnapshotDate)
            .ToListAsync(cancellationToken);
        var content = contentRows
            .GroupBy(item => item.PlatformVideoId)
            .Select(group =>
            {
                var latest = group.OrderByDescending(item => item.SnapshotDate).First();
                return new FacebookContentItemDto(
                    latest.PlatformVideoId,
                    latest.Title,
                    latest.PlatformVideoId,
                    TitleKind(latest.Title),
                    latest.PublishedAt,
                    latest.Views,
                    latest.Likes,
                    latest.Clicks,
                    latest.Comments,
                    group.Select(item => new PinterestMetricPointDto(
                        item.SnapshotDate,
                        item.Views,
                        item.Likes,
                        item.Comments,
                        item.Clicks)).ToArray());
            })
            .OrderByDescending(item => item.PublishedAt)
            .ThenByDescending(item => item.Views)
            .ToArray();

        return new FacebookContentSnapshotDto(
            new FacebookContentChannelDto(
                account.AccountHandle ?? "facebook",
                account.AccountName,
                null,
                latestMetric?.Followers ?? 0,
                latestMetric?.Views ?? 0,
                content.Length,
                latestMetric?.Clicks ?? 0,
                "Facebook report maps impressions/views/reach to views and post/link clicks to clicks."),
            content);
    }

    private async Task<SocialAccount> UpsertAccountAsync(
        FacebookAccountInfo accountInfo,
        CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts.FirstOrDefaultAsync(
            item => item.Platform == "facebook" &&
                ((accountInfo.Handle != null && item.AccountHandle == accountInfo.Handle) ||
                 item.AccountName == accountInfo.Name),
            cancellationToken);
        if (account is null)
        {
            account = new SocialAccount
            {
                Platform = "facebook",
                AccountName = accountInfo.Name,
                AccountHandle = accountInfo.Handle,
                AccountUrl = accountInfo.Url,
                Color = "#1877f2",
                IsActive = true
            };
            db.SocialAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            account.AccountName = accountInfo.Name;
            account.AccountHandle = accountInfo.Handle ?? account.AccountHandle;
            account.AccountUrl = accountInfo.Url ?? account.AccountUrl;
            account.Color ??= "#1877f2";
            account.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return account;
    }

    private async Task<SocialMetricSnapshot> GetOrCreateMetricAsync(
        int accountId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken)
    {
        var metric = db.ChangeTracker
            .Entries<SocialMetricSnapshot>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(item => item.SocialAccountId == accountId && item.SnapshotDate == snapshotDate);
        if (metric is not null)
        {
            return metric;
        }

        metric = await db.SocialMetricSnapshots.FirstOrDefaultAsync(
            item => item.SocialAccountId == accountId && item.SnapshotDate == snapshotDate,
            cancellationToken);
        if (metric is not null)
        {
            return metric;
        }

        metric = new SocialMetricSnapshot
        {
            SocialAccountId = accountId,
            SnapshotDate = snapshotDate
        };
        db.SocialMetricSnapshots.Add(metric);
        return metric;
    }

    private async Task UpsertPostSnapshotAsync(
        int accountId,
        IReadOnlyDictionary<string, string> row,
        DateOnly snapshotDate,
        CancellationToken cancellationToken)
    {
        var id = NormalizeFacebookId(
            GetAny(row, FacebookUrlKeys),
            GetAny(row, FacebookIdKeys));
        var metric = db.ChangeTracker
            .Entries<SocialVideoMetricSnapshot>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(item =>
                item.SocialAccountId == accountId &&
                item.PlatformVideoId == id &&
                item.SnapshotDate == snapshotDate);

        metric ??= await db.SocialVideoMetricSnapshots.FirstOrDefaultAsync(
            item => item.SocialAccountId == accountId &&
                item.PlatformVideoId == id &&
                item.SnapshotDate == snapshotDate,
            cancellationToken);
        if (metric is null)
        {
            metric = new SocialVideoMetricSnapshot
            {
                SocialAccountId = accountId,
                PlatformVideoId = id,
                SnapshotDate = snapshotDate
            };
            db.SocialVideoMetricSnapshots.Add(metric);
        }

        metric.Title = BuildTitle(row, id);
        metric.ThumbnailUrl = null;
        metric.PublishedAt = ParseDateTime(GetAny(row, FacebookPublishedAtKeys));
        var previous = await PreviousContentMetricAsync(accountId, id, snapshotDate, cancellationToken);
        metric.Views = (previous?.Views ?? 0) + ReadMetric(row, FacebookViewKeys);
        metric.Likes = (previous?.Likes ?? 0) + ReadMetric(row, FacebookLikeKeys);
        metric.Comments = (previous?.Comments ?? 0) + ReadMetric(row, FacebookCommentKeys);
        metric.Clicks = (previous?.Clicks ?? 0) + ReadMetric(row, FacebookClickKeys);
        metric.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<SocialMetricSnapshot?> PreviousAccountMetricAsync(
        int accountId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken) =>
        await db.SocialMetricSnapshots
            .AsNoTracking()
            .Where(item => item.SocialAccountId == accountId && item.SnapshotDate < snapshotDate)
            .OrderByDescending(item => item.SnapshotDate)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<SocialVideoMetricSnapshot?> PreviousContentMetricAsync(
        int accountId,
        string id,
        DateOnly snapshotDate,
        CancellationToken cancellationToken) =>
        await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Where(item => item.SocialAccountId == accountId &&
                item.PlatformVideoId == id &&
                item.SnapshotDate < snapshotDate)
            .OrderByDescending(item => item.SnapshotDate)
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsPostRow(IReadOnlyDictionary<string, string> row) =>
        !string.IsNullOrWhiteSpace(GetAny(row, FacebookIdKeys)) ||
        !string.IsNullOrWhiteSpace(GetAny(row, FacebookUrlKeys));

    private static FacebookAccountInfo ExtractAccountInfo(
        string fileName,
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        var first = rows[0];
        var name = GetAny(first, ["Page Name", "Page name", "Account name", "Account", "Facebook Page", "Page"]);
        var handle = NormalizeHandle(GetAny(first, ["Page username", "Username", "Handle", "Page ID", "Account ID"]));
        var url = GetAny(first, ["Page URL", "Facebook Page URL", "Account URL"]);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = !string.IsNullOrWhiteSpace(handle)
                ? handle.TrimStart('@')
                : Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' ');
        }

        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(handle) && !handle.All(char.IsDigit))
        {
            url = $"https://www.facebook.com/{handle.TrimStart('@')}/";
        }

        return new FacebookAccountInfo(
            string.IsNullOrWhiteSpace(name) ? "Facebook import" : name,
            string.IsNullOrWhiteSpace(handle) ? null : handle,
            string.IsNullOrWhiteSpace(url) ? null : url);
    }

    private static DateOnly ExtractSnapshotDate(
        string fileName,
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        var fileDates = Regex.Matches(fileName, @"\d{4}[-_]\d{2}[-_]\d{2}")
            .Select(match => match.Value.Replace('_', '-'))
            .Select(value => DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date) ? date : (DateOnly?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .Concat(Regex.Matches(fileName, @"[A-Za-z]+-\d{1,2}-\d{4}")
                .Select(match => ParseFileNameDate(match.Value))
                .Where(date => date.HasValue)
                .Select(date => date!.Value))
            .ToArray();
        if (fileDates.Length > 0)
        {
            return fileDates.Max();
        }

        var rowDates = rows
            .Select(row => ParseDateTime(GetAny(row, FacebookPublishedAtKeys)))
            .Where(date => date.HasValue)
            .Select(date => DateOnly.FromDateTime(date!.Value.DateTime))
            .ToArray();
        return rowDates.Length > 0 ? rowDates.Max() : DateOnly.FromDateTime(DateTime.Now);
    }

    private static DateOnly? ParseFileNameDate(string value)
    {
        var formats = new[] { "MMM-d-yyyy", "MMMM-d-yyyy" };
        return DateTime.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? DateOnly.FromDateTime(date)
            : null;
    }

    private static List<List<string>> ReadRows(Stream stream)
    {
        using var parser = new TextFieldParser(stream, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var rows = new List<List<string>>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            rows.Add(fields.Select(field => field.Trim()).ToList());
        }

        return rows;
    }

    private static Dictionary<string, string> ToDictionary(IReadOnlyList<string> header, IReadOnlyList<string> row)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < header.Count; index++)
        {
            result[header[index]] = row.ElementAtOrDefault(index) ?? "";
        }

        return result;
    }

    private static int ReadMetric(IReadOnlyDictionary<string, string> row, IReadOnlyList<string> keys) =>
        keys.Select(key => ParseInt(GetAny(row, [key])))
            .FirstOrDefault(value => value > 0);

    private static string GetAny(IReadOnlyDictionary<string, string> row, IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        var normalized = row
            .GroupBy(item => NormalizeHeader(item.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
                StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (normalized.TryGetValue(NormalizeHeader(key), out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string NormalizeHeader(string value) =>
        Regex.Replace(value, @"[^a-z0-9]+", "", RegexOptions.IgnoreCase).ToLowerInvariant();

    private static string NormalizeFacebookId(string url, string fallback)
    {
        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            var query = string.IsNullOrWhiteSpace(uri.Query) ? "" : uri.Query;
            var normalized = $"{uri.Scheme}://{uri.Host}{path}{query}";
            return normalized.Length <= 128 ? normalized : normalized[..128];
        }

        return string.IsNullOrWhiteSpace(fallback) ? Guid.NewGuid().ToString("N") : fallback[..Math.Min(fallback.Length, 128)];
    }

    private static string BuildTitle(IReadOnlyDictionary<string, string> row, string id)
    {
        var text = GetAny(row, ["Post Message", "Message", "Description", "Caption", "Text", "Title"]);
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            return firstLine.Length <= 180 ? firstLine : firstLine[..180];
        }

        var kind = GetAny(row, ["Post Type", "Type", "Content Type"]);
        if (string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(GetAny(row, ["Video asset ID", "Universal video ID"])))
        {
            kind = "Facebook video";
        }
        var match = FacebookPostRegex.Match(id);
        var postId = match.Success ? match.Groups["id"].Value : id.Split('/').LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return $"{(string.IsNullOrWhiteSpace(kind) ? "Facebook post" : kind)} {postId}".Trim();
    }

    private static string TitleKind(string title)
    {
        if (title.Contains("reel", StringComparison.OrdinalIgnoreCase)) return "reel";
        if (title.Contains("video", StringComparison.OrdinalIgnoreCase)) return "video";
        return "post";
    }

    private static string? NormalizeHandle(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        return trimmed.StartsWith('@') || trimmed.All(char.IsDigit) ? trimmed : $"@{trimmed}";
    }

    private static DateTimeOffset? ParseDateTime(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int ParseInt(string? value) =>
        int.TryParse((value ?? "").Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? Math.Max(0, result)
            : 0;

    private sealed record FacebookTotals(
        int Views = 0,
        int Likes = 0,
        int Comments = 0,
        int Shares = 0,
        int Clicks = 0,
        int Followers = 0);

    private sealed record FacebookAccountInfo(string Name, string? Handle, string? Url);
}
