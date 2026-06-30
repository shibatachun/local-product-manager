using System.Globalization;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class TikTokReportImportService(AppDbContext db)
{
    public async Task<TikTokReportImportResult> ImportAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var rows = SimpleXlsxReader.ReadFirstSheet(stream)
            .Where(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();
        if (rows.Count < 2)
        {
            throw new InvalidOperationException("TikTok overview did not contain any daily metric rows.");
        }

        var header = rows[0];
        ValidateHeader(header);
        var records = ResolveDates(rows.Skip(1)
                .Select(row => ToDictionary(header, row))
                .Where(row => !string.IsNullOrWhiteSpace(Get(row, "Date")))
                .ToArray())
            .ToArray();
        if (records.Length == 0)
        {
            throw new InvalidOperationException("TikTok overview did not contain recognized daily rows.");
        }

        var account = await UpsertAccountAsync(cancellationToken);
        var totals = new TikTokTotals();
        foreach (var record in records)
        {
            var views = ParseInt(Get(record.Row, "Video Views"));
            var profileViews = ParseInt(Get(record.Row, "Profile Views"));
            var likes = ParseInt(Get(record.Row, "Likes"));
            var comments = ParseInt(Get(record.Row, "Comments"));
            var shares = ParseInt(Get(record.Row, "Shares"));

            totals = totals with
            {
                Views = totals.Views + views,
                ProfileViews = totals.ProfileViews + profileViews,
                Likes = totals.Likes + likes,
                Comments = totals.Comments + comments,
                Shares = totals.Shares + shares
            };

            var metric = await GetOrCreateMetricAsync(account.Id, record.Date, cancellationToken);
            metric.Views = views;
            metric.Clicks = profileViews;
            metric.Likes = likes;
            metric.Comments = comments;
            metric.Shares = shares;
            metric.Followers = 0;
            metric.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertDailyDeltaAsync(
                account.Id,
                record.Date,
                views,
                profileViews,
                likes,
                comments,
                shares,
                cancellationToken);
        }

        account.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new TikTokReportImportResult(
            account.Id,
            records.Length,
            totals.Views,
            totals.ProfileViews,
            totals.Likes,
            totals.Comments,
            totals.Shares,
            account.AccountName,
            records.Min(row => row.Date),
            records.Max(row => row.Date));
    }

    private async Task<SocialAccount> UpsertAccountAsync(CancellationToken cancellationToken)
    {
        var account = await db.SocialAccounts
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(item => item.Platform == "tiktok", cancellationToken);
        if (account is not null)
        {
            account.Color ??= "#111827";
            return account;
        }

        account = new SocialAccount
        {
            Platform = "tiktok",
            AccountName = "TikTok Overview",
            AccountHandle = "@tiktok",
            AccountUrl = null,
            Color = "#111827",
            IsActive = true
        };
        db.SocialAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
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
        if (metric is not null) return metric;

        metric = await db.SocialMetricSnapshots.FirstOrDefaultAsync(
            item => item.SocialAccountId == accountId && item.SnapshotDate == snapshotDate,
            cancellationToken);
        if (metric is not null) return metric;

        metric = new SocialMetricSnapshot
        {
            SocialAccountId = accountId,
            SnapshotDate = snapshotDate
        };
        db.SocialMetricSnapshots.Add(metric);
        return metric;
    }

    private async Task UpsertDailyDeltaAsync(
        int accountId,
        DateOnly snapshotDate,
        int views,
        int clicks,
        int likes,
        int comments,
        int shares,
        CancellationToken cancellationToken)
    {
        var delta = await db.SocialDailyMetricDeltas.FirstOrDefaultAsync(
            item => item.SocialAccountId == accountId &&
                item.Scope == "account" &&
                item.MetricKey == "__account__" &&
                item.SnapshotDate == snapshotDate,
            cancellationToken);
        if (delta is null)
        {
            delta = new SocialDailyMetricDelta
            {
                SocialAccountId = accountId,
                Scope = "account",
                MetricKey = "__account__",
                SnapshotDate = snapshotDate
            };
            db.SocialDailyMetricDeltas.Add(delta);
        }

        delta.Title = "TikTok overview daily row";
        delta.Views = views;
        delta.Clicks = clicks;
        delta.Likes = likes;
        delta.Comments = comments;
        delta.Shares = shares;
        delta.Followers = 0;
        delta.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static IEnumerable<TikTokRecord> ResolveDates(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var parsed = rows
            .Select(row => new { Row = row, MonthDay = ParseMonthDay(Get(row, "Date")) })
            .Where(item => item.MonthDay.HasValue)
            .Select(item => new { item.Row, Month = item.MonthDay!.Value.Month, Day = item.MonthDay.Value.Day })
            .ToArray();
        if (parsed.Length == 0) yield break;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var startYear = today.Year;
        var first = parsed[0];
        var last = parsed[^1];
        if (first.Month > last.Month || (first.Month == last.Month && first.Day > last.Day))
        {
            startYear--;
        }

        var currentYear = startYear;
        var previousMonth = parsed[0].Month;
        foreach (var item in parsed)
        {
            if (item.Month < previousMonth)
            {
                currentYear++;
            }
            previousMonth = item.Month;
            yield return new TikTokRecord(item.Row, new DateOnly(currentYear, item.Month, item.Day));
        }
    }

    private static void ValidateHeader(IReadOnlyList<string> header)
    {
        foreach (var required in new[] { "Date", "Video Views", "Profile Views", "Likes", "Comments", "Shares" })
        {
            if (!header.Any(value => string.Equals(value, required, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"TikTok overview is missing required column: {required}");
            }
        }
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

    private static DateOnly? ParseMonthDay(string value)
    {
        foreach (var format in new[] { "MMMM d", "MMM d", "M/d", "M/d/yyyy", "yyyy-MM-dd" })
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return DateOnly.FromDateTime(parsed);
            }
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallback)
            ? DateOnly.FromDateTime(fallback)
            : null;
    }

    private static int ParseInt(string? value) =>
        int.TryParse((value ?? "").Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? Math.Max(0, result)
            : 0;

    private sealed record TikTokRecord(Dictionary<string, string> Row, DateOnly Date);
    private sealed record TikTokTotals(
        int Views = 0,
        int ProfileViews = 0,
        int Likes = 0,
        int Comments = 0,
        int Shares = 0);
}
