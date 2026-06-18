using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace LocalProductManager.Api.Services;

public sealed class PinterestReportImportService(AppDbContext db)
{
    private static readonly Regex PinterestHandleRegex = new(
        @"pinterest\.com/(?<handle>[^/]+)/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PinterestPinRegex = new(
        @"pinterest\.com/pin/(?<id>\d+)/?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<PinterestReportImportResult> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var rows = ReadRows(stream);
        var dailyRows = ExtractDailyRows(rows);
        var boardRows = ExtractRowsAfterHeader(rows, "Top Boards", "Pinterest Link", "Impressions", "Engagement", "Pin clicks", "Outbound clicks", "Saves");
        var pinRows = ExtractRowsAfterHeader(rows, "Top Pins", "Pinterest Link", "Content Type", "Source", "Canonical", "Impressions");
        var snapshotDate = dailyRows.LastOrDefault(row => row.Impressions > 0)?.Date ??
            ExtractReportEndDate(rows) ??
            DateOnly.FromDateTime(DateTime.Now);
        var accountInfo = ExtractAccountInfo(boardRows, pinRows);
        var account = await UpsertAccountAsync(accountInfo, cancellationToken);

        foreach (var row in dailyRows)
        {
            var metric = await GetOrCreateMetricAsync(account.Id, row.Date, cancellationToken);
            metric.Views = row.Impressions;
            metric.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var boardTotals = boardRows.Aggregate(
            new PinterestBoardTotals(),
            (totals, row) => totals with
            {
                Impressions = totals.Impressions + ParseInt(row.GetValueOrDefault("Impressions")),
                Engagement = totals.Engagement + ParseInt(row.GetValueOrDefault("Engagement")),
                PinClicks = totals.PinClicks + ParseInt(row.GetValueOrDefault("Pin clicks")),
                OutboundClicks = totals.OutboundClicks + ParseInt(row.GetValueOrDefault("Outbound clicks")),
                Saves = totals.Saves + ParseInt(row.GetValueOrDefault("Saves"))
            });
        if (boardRows.Count > 0)
        {
            var metric = await GetOrCreateMetricAsync(account.Id, snapshotDate, cancellationToken);
            metric.Views = Math.Max(metric.Views, boardTotals.Impressions);
            metric.Likes = boardTotals.Engagement;
            metric.Clicks = boardTotals.PinClicks + boardTotals.OutboundClicks;
            metric.Shares = boardTotals.Saves;
            metric.UpdatedAt = DateTimeOffset.UtcNow;
        }

        foreach (var row in boardRows)
        {
            await UpsertContentSnapshotAsync(
                account.Id,
                row.GetValueOrDefault("Pinterest Link") ?? "",
                "board",
                TitleFromPinterestUrl(row.GetValueOrDefault("Pinterest Link") ?? "", "Board"),
                snapshotDate,
                ParseInt(row.GetValueOrDefault("Impressions")),
                ParseInt(row.GetValueOrDefault("Engagement")),
                ParseInt(row.GetValueOrDefault("Pin clicks")) + ParseInt(row.GetValueOrDefault("Outbound clicks")),
                cancellationToken);
        }

        foreach (var row in pinRows)
        {
            var url = row.GetValueOrDefault("Pinterest Link") ?? "";
            await UpsertContentSnapshotAsync(
                account.Id,
                url,
                "pin",
                TitleFromPinterestUrl(url, "Pin"),
                snapshotDate,
                ParseInt(row.GetValueOrDefault("Impressions")),
                likes: 0,
                clicks: 0,
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new PinterestReportImportResult(
            dailyRows.Count,
            boardRows.Count,
            pinRows.Count,
            account.AccountName,
            snapshotDate);
    }

    public async Task<PinterestContentSnapshotDto?> GetStoredSnapshotAsync(CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Platform == "pinterest" && item.AccountName != "Demo Pinterest", cancellationToken) ??
            await db.SocialAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Platform == "pinterest", cancellationToken);
        if (account is null)
        {
            return null;
        }

        var latestMetric = await db.SocialMetricSnapshots
            .AsNoTracking()
            .Where(item => item.SocialAccountId == account.Id)
            .OrderByDescending(item => item.SnapshotDate)
            .FirstOrDefaultAsync(cancellationToken);
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
                var kind = latest.Title.StartsWith("Board:", StringComparison.OrdinalIgnoreCase) ? "board" : "pin";
                return new PinterestContentItemDto(
                    latest.PlatformVideoId,
                    latest.Title,
                    latest.PlatformVideoId,
                    kind,
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
            .OrderByDescending(item => item.Views)
            .ToArray();

        return new PinterestContentSnapshotDto(
            new PinterestContentChannelDto(
                account.AccountHandle ?? "pinterest",
                account.AccountName,
                null,
                latestMetric?.Followers ?? 0,
                latestMetric?.Views ?? 0,
                content.Count(item => item.Kind == "pin"),
                latestMetric?.Clicks ?? 0,
                "Pinterest CSV maps impressions to views, pin/outbound clicks to clicks, engagement to likes, and saves to shares."),
            content);
    }

    private async Task<SocialAccount> UpsertAccountAsync(
        PinterestAccountInfo accountInfo,
        CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts.FirstOrDefaultAsync(
            item => item.Platform == "pinterest" && item.AccountHandle == accountInfo.Handle,
            cancellationToken);
        if (account is null)
        {
            account = new SocialAccount
            {
                Platform = "pinterest",
                AccountName = accountInfo.Name,
                AccountHandle = accountInfo.Handle,
                AccountUrl = accountInfo.Url,
                Color = "#bd081c",
                IsActive = true
            };
            db.SocialAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            account.AccountName = accountInfo.Name;
            account.AccountUrl = accountInfo.Url;
            account.Color ??= "#bd081c";
            account.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return account;
    }

    private async Task<SocialMetricSnapshot> GetOrCreateMetricAsync(
        int accountId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken)
    {
        var metric = await db.SocialMetricSnapshots.FirstOrDefaultAsync(
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

    private async Task UpsertContentSnapshotAsync(
        int accountId,
        string url,
        string kind,
        string title,
        DateOnly snapshotDate,
        int views,
        int likes,
        int clicks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var metric = await db.SocialVideoMetricSnapshots.FirstOrDefaultAsync(
            item => item.SocialAccountId == accountId &&
                item.PlatformVideoId == url &&
                item.SnapshotDate == snapshotDate,
            cancellationToken);
        if (metric is null)
        {
            metric = new SocialVideoMetricSnapshot
            {
                SocialAccountId = accountId,
                PlatformVideoId = url,
                SnapshotDate = snapshotDate
            };
            db.SocialVideoMetricSnapshots.Add(metric);
        }

        metric.Title = kind == "board" && !title.StartsWith("Board:", StringComparison.OrdinalIgnoreCase)
            ? $"Board: {title}"
            : title;
        metric.ThumbnailUrl = null;
        metric.Views = views;
        metric.Likes = likes;
        metric.Clicks = clicks;
        metric.Comments = 0;
        metric.UpdatedAt = DateTimeOffset.UtcNow;
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

    private static List<PinterestDailyRow> ExtractDailyRows(IReadOnlyList<List<string>> rows)
    {
        var result = new List<PinterestDailyRow>();
        var headerIndex = FindExactHeader(rows, "Date", "Impressions");
        if (headerIndex < 0) return result;

        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.Count < 2 || !DateOnly.TryParse(row[0], CultureInfo.InvariantCulture, out var date))
            {
                break;
            }

            result.Add(new PinterestDailyRow(date, ParseInt(row.ElementAtOrDefault(1))));
        }

        return result;
    }

    private static List<Dictionary<string, string>> ExtractRowsAfterHeader(
        IReadOnlyList<List<string>> rows,
        string sectionTitle,
        params string[] headers)
    {
        var titleIndex = rows.ToList().FindIndex(row => row.Count > 0 && row[0].StartsWith(sectionTitle, StringComparison.OrdinalIgnoreCase));
        if (titleIndex < 0) return [];
        var headerIndex = -1;
        for (var index = titleIndex + 1; index < rows.Count; index++)
        {
            if (IsHeader(rows[index], headers))
            {
                headerIndex = index;
                break;
            }
        }
        if (headerIndex < 0) return [];

        var result = new List<Dictionary<string, string>>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0]) || !row[0].StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var item = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                item[headers[index]] = row.ElementAtOrDefault(index) ?? "";
            }
            result.Add(item);
        }

        return result;
    }

    private static int FindExactHeader(IReadOnlyList<List<string>> rows, params string[] headers)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (IsHeader(rows[index], headers)) return index;
        }

        return -1;
    }

    private static bool IsHeader(IReadOnlyList<string> row, IReadOnlyList<string> headers) =>
        row.Count >= headers.Count &&
        headers.Select((header, index) => string.Equals(row[index], header, StringComparison.OrdinalIgnoreCase)).All(BooleanIsTrue);

    private static bool BooleanIsTrue(bool value) => value;

    private static DateOnly? ExtractReportEndDate(IReadOnlyList<List<string>> rows)
    {
        var range = rows.SelectMany(row => row)
            .FirstOrDefault(value => Regex.IsMatch(value, @"\d{4}-\d{2}-\d{2}\s*-\s*\d{4}-\d{2}-\d{2}"));
        if (range is null) return null;
        var parts = range.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length >= 6 &&
            DateOnly.TryParse($"{parts[3]}-{parts[4]}-{parts[5]}", CultureInfo.InvariantCulture, out var endDate))
        {
            return endDate;
        }

        return null;
    }

    private static PinterestAccountInfo ExtractAccountInfo(
        IReadOnlyList<Dictionary<string, string>> boardRows,
        IReadOnlyList<Dictionary<string, string>> pinRows)
    {
        var url = boardRows.Select(row => row.GetValueOrDefault("Pinterest Link"))
            .Concat(pinRows.Select(row => row.GetValueOrDefault("Pinterest Link")))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "https://www.pinterest.com/";
        var match = PinterestHandleRegex.Match(url);
        var handle = match.Success && !string.Equals(match.Groups["handle"].Value, "pin", StringComparison.OrdinalIgnoreCase)
            ? match.Groups["handle"].Value
            : "imported-pinterest";
        return new PinterestAccountInfo(
            $"Pinterest {handle}",
            $"@{handle}",
            $"https://www.pinterest.com/{handle}/");
    }

    private static string TitleFromPinterestUrl(string url, string fallback)
    {
        var pin = PinterestPinRegex.Match(url);
        if (pin.Success) return $"Pin {pin.Groups["id"].Value}";

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var last = parts.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(last))
            {
                return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(last.Replace('-', ' ').Replace('_', ' '));
            }
        }

        return fallback;
    }

    private static int ParseInt(string? value) =>
        int.TryParse((value ?? "").Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? Math.Max(0, result)
            : 0;

    private sealed record PinterestDailyRow(DateOnly Date, int Impressions);

    private sealed record PinterestBoardTotals(
        int Impressions = 0,
        int Engagement = 0,
        int PinClicks = 0,
        int OutboundClicks = 0,
        int Saves = 0);

    private sealed record PinterestAccountInfo(string Name, string Handle, string Url);
}
