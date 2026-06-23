using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace LocalProductManager.Api.Services;

public sealed class InstagramReportImportService(AppDbContext db)
{
    private static readonly Regex InstagramPostRegex = new(
        @"instagram\.com/(?:p|reel|tv)/(?<id>[^/?#]+)/?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FileDateRangeRegex = new(
        @"_(?<end>[A-Za-z]{3}-\d{1,2}-\d{4})_",
        RegexOptions.Compiled);

    public async Task<InstagramReportImportResult> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var rows = ReadRows(stream);
        if (rows.Count < 2)
        {
            throw new InvalidOperationException("Instagram report did not contain any post rows.");
        }

        var header = rows[0];
        var records = rows.Skip(1)
            .Select(row => ToDictionary(header, row))
            .Where(row => !string.IsNullOrWhiteSpace(Get(row, "Post ID")) || !string.IsNullOrWhiteSpace(Get(row, "Permalink")))
            .ToList();
        if (records.Count == 0)
        {
            throw new InvalidOperationException("Instagram report did not contain recognized post rows.");
        }

        var accountInfo = ExtractAccountInfo(records);
        var account = await UpsertAccountAsync(accountInfo, cancellationToken);
        var snapshotDate = ExtractSnapshotDate(file.FileName, records);
        var totals = records.Aggregate(
            new InstagramTotals(),
            (current, row) => current with
            {
                Views = current.Views + ParseInt(Get(row, "Views")),
                Likes = current.Likes + ParseInt(Get(row, "Likes")),
                Comments = current.Comments + ParseInt(Get(row, "Comments")),
                Shares = current.Shares + ParseInt(Get(row, "Shares")) + ParseInt(Get(row, "Saves")),
                Followers = current.Followers + ParseInt(Get(row, "Follows"))
            });

        var previousMetric = await PreviousAccountMetricAsync(account.Id, snapshotDate, cancellationToken);
        var metric = await GetOrCreateMetricAsync(account.Id, snapshotDate, cancellationToken);
        metric.Views = (previousMetric?.Views ?? 0) + totals.Views;
        metric.Likes = (previousMetric?.Likes ?? 0) + totals.Likes;
        metric.Comments = (previousMetric?.Comments ?? 0) + totals.Comments;
        metric.Shares = (previousMetric?.Shares ?? 0) + totals.Shares;
        metric.Followers = (previousMetric?.Followers ?? 0) + totals.Followers;
        metric.Clicks = previousMetric?.Clicks ?? 0;
        metric.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var row in records)
        {
            await UpsertPostSnapshotAsync(account.Id, row, snapshotDate, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new InstagramReportImportResult(
            account.Id,
            records.Count,
            totals.Views,
            totals.Likes,
            totals.Comments,
            totals.Shares,
            totals.Followers,
            account.AccountName,
            snapshotDate);
    }

    public async Task<InstagramContentSnapshotDto?> GetStoredSnapshotAsync(
        int? accountId,
        CancellationToken cancellationToken)
    {
        var query = db.SocialAccounts
            .AsNoTracking()
            .Where(item => item.Platform == "instagram");
        SocialAccount? account = null;
        if (accountId.HasValue)
        {
            account = await query
                .FirstOrDefaultAsync(item => item.Id == accountId.Value, cancellationToken);
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
                return new InstagramContentItemDto(
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

        return new InstagramContentSnapshotDto(
            new InstagramContentChannelDto(
                account.AccountHandle ?? "instagram",
                account.AccountName,
                null,
                latestMetric?.Followers ?? 0,
                latestMetric?.Views ?? 0,
                content.Length,
                latestMetric?.Shares ?? 0,
                "Instagram CSV does not include link clicks. Saves are shown per post, and shares plus saves feed the Shares chart."),
            content);
    }

    private async Task<SocialAccount> UpsertAccountAsync(
        InstagramAccountInfo accountInfo,
        CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts.FirstOrDefaultAsync(
            item => item.Platform == "instagram" && item.AccountHandle == accountInfo.Handle,
            cancellationToken);
        if (account is null)
        {
            account = new SocialAccount
            {
                Platform = "instagram",
                AccountName = accountInfo.Name,
                AccountHandle = accountInfo.Handle,
                AccountUrl = accountInfo.Url,
                Color = "#c13584",
                IsActive = true
            };
            db.SocialAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            account.AccountName = accountInfo.Name;
            account.AccountUrl = accountInfo.Url;
            account.Color ??= "#c13584";
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
        var id = NormalizeInstagramId(Get(row, "Permalink"), Get(row, "Post ID"));
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

        metric.Title = BuildTitle(Get(row, "Description"), Get(row, "Post type"), id);
        metric.ThumbnailUrl = null;
        metric.PublishedAt = ParseDateTime(Get(row, "Publish time"));
        var previous = await PreviousContentMetricAsync(accountId, id, snapshotDate, cancellationToken);
        metric.Views = (previous?.Views ?? 0) + ParseInt(Get(row, "Views"));
        metric.Likes = (previous?.Likes ?? 0) + ParseInt(Get(row, "Likes"));
        metric.Comments = (previous?.Comments ?? 0) + ParseInt(Get(row, "Comments"));
        metric.Clicks = (previous?.Clicks ?? 0) + ParseInt(Get(row, "Saves"));
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

    private static InstagramAccountInfo ExtractAccountInfo(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var first = rows[0];
        var username = Get(first, "Account username");
        var name = Get(first, "Account name");
        if (string.IsNullOrWhiteSpace(username))
        {
            username = Get(first, "Account ID");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            name = username;
        }

        var handle = username.StartsWith('@') ? username : $"@{username}";
        return new InstagramAccountInfo(
            string.IsNullOrWhiteSpace(name) ? $"Instagram {handle}" : name,
            handle,
            string.IsNullOrWhiteSpace(username) ? null : $"https://www.instagram.com/{username.TrimStart('@')}/");
    }

    private static DateOnly ExtractSnapshotDate(string fileName, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var match = FileDateRangeRegex.Match(fileName);
        if (match.Success &&
            DateOnly.TryParseExact(match.Groups["end"].Value, "MMM-dd-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
        {
            return endDate;
        }

        var dates = rows.Select(row => ParseDateTime(Get(row, "Publish time")))
            .Where(date => date.HasValue)
            .Select(date => DateOnly.FromDateTime(date!.Value.DateTime))
            .ToArray();
        return dates.Length > 0 ? dates.Max() : DateOnly.FromDateTime(DateTime.Now);
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

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value.Trim() : "";

    private static string NormalizeInstagramId(string url, string fallback)
    {
        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            var normalized = $"{uri.Scheme}://{uri.Host}{path}/";
            return normalized.Length <= 128 ? normalized : normalized[..128];
        }

        return string.IsNullOrWhiteSpace(fallback) ? Guid.NewGuid().ToString("N") : fallback[..Math.Min(fallback.Length, 128)];
    }

    private static string BuildTitle(string description, string postType, string id)
    {
        var firstLine = description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            return firstLine.Length <= 180 ? firstLine : firstLine[..180];
        }

        var match = InstagramPostRegex.Match(id);
        var shortCode = match.Success ? match.Groups["id"].Value : id.Split('/').LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return $"{(string.IsNullOrWhiteSpace(postType) ? "Instagram post" : postType)} {shortCode}".Trim();
    }

    private static string TitleKind(string title)
    {
        return title.Contains("reel", StringComparison.OrdinalIgnoreCase) ? "reel" : "post";
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

    private sealed record InstagramTotals(
        int Views = 0,
        int Likes = 0,
        int Comments = 0,
        int Shares = 0,
        int Followers = 0);

    private sealed record InstagramAccountInfo(string Name, string Handle, string? Url);
}
