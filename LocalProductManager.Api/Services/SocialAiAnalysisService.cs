using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public sealed class SocialAiAnalysisService(
    AppDbContext db,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string reportDirectory = Path.Combine(environment.ContentRootPath, "ai-reports", "social");

    public SocialAiAnalysisStatusDto GetStatus() => new(
        IsConfigured,
        Model,
        reportDirectory,
        IsConfigured
            ? "Gemini API key is configured. Daily bilingual reports run at server time 00:00; weekly bilingual reports run every Sunday at 00:00."
            : "Set AiAnalysis:GeminiApiKey in appsettings or GEMINI_API_KEY in the server environment.");

    public async Task<SocialAiAnalysisResultDto> RunAsync(
        string period,
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var range = ResolveRange(period, date ?? DateOnly.FromDateTime(DateTime.Now));
        var prompt = await BuildPromptAsync(period, range.From, range.To, cancellationToken);
        var summary = await GenerateGeminiSummaryAsync(prompt, cancellationToken);
        var generatedAt = DateTimeOffset.Now;
        var path = await SaveReportAsync(period, range.From, range.To, summary, prompt, generatedAt, cancellationToken);

        return new SocialAiAnalysisResultDto(
            NormalizePeriod(period),
            range.From,
            range.To,
            Model,
            path,
            summary,
            generatedAt);
    }

    public async Task<SocialAiAnalysisResultDto> RunRecentWeekAsync(
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var to = date ?? DateOnly.FromDateTime(DateTime.Now);
        var from = to.AddDays(-6);
        var prompt = await BuildPromptAsync("weekly", from, to, cancellationToken);
        var summary = await GenerateGeminiSummaryAsync(prompt, cancellationToken);
        var generatedAt = DateTimeOffset.Now;
        var path = await SaveReportAsync("weekly", from, to, summary, prompt, generatedAt, cancellationToken);

        return new SocialAiAnalysisResultDto(
            "weekly",
            from,
            to,
            Model,
            path,
            summary,
            generatedAt);
    }

    public async Task<IReadOnlyList<SocialAiAnalysisReportDto>> GetRecentReportsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(reportDirectory);
        var files = Directory
            .EnumerateFiles(reportDirectory, "social-ai-*.md")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();

        var result = new List<SocialAiAnalysisReportDto>();
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            result.Add(new SocialAiAnalysisReportDto(
                Path.GetFileName(file),
                file,
                ExtractFrontMatter(text, "period") ?? "unknown",
                ParseDate(ExtractFrontMatter(text, "from")) ?? DateOnly.MinValue,
                ParseDate(ExtractFrontMatter(text, "to")) ?? DateOnly.MinValue,
                DateTimeOffset.TryParse(ExtractFrontMatter(text, "generatedAt"), out var generatedAt)
                    ? generatedAt
                    : File.GetLastWriteTime(file),
                Preview(text)));
        }

        return result;
    }

    public async Task<SocialAiAnalysisReportContentDto?> GetReportContentAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = ResolveReportPath(fileName);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        return new SocialAiAnalysisReportContentDto(
            Path.GetFileName(path),
            await File.ReadAllTextAsync(path, cancellationToken));
    }

    public string? GetReportPathForDownload(string fileName)
    {
        var path = ResolveReportPath(fileName);
        return path is not null && File.Exists(path) ? path : null;
    }

    private async Task<string> BuildPromptAsync(
        string period,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var normalizedPeriod = NormalizePeriod(period);
        var comparison = ResolveComparisonRange(normalizedPeriod, from, to);
        var ytdFrom = new DateOnly(to.Year, 1, 1);
        var accounts = await db.SocialAccounts
            .AsNoTracking()
            .Include(account => account.Metrics)
            .OrderBy(account => account.Platform)
            .ThenBy(account => account.AccountName)
            .ToListAsync(cancellationToken);
        var currentContentRows = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Include(metric => metric.SocialAccount)
            .Where(metric => metric.SnapshotDate >= from && metric.SnapshotDate <= to)
            .ToListAsync(cancellationToken);
        var comparisonContentRows = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Include(metric => metric.SocialAccount)
            .Where(metric => metric.SnapshotDate >= comparison.From && metric.SnapshotDate <= comparison.To)
            .ToListAsync(cancellationToken);
        var dailyDeltas = await db.SocialDailyMetricDeltas
            .AsNoTracking()
            .Include(metric => metric.SocialAccount)
            .Where(metric => metric.SnapshotDate <= to)
            .ToListAsync(cancellationToken);
        var currentPeriodDeltas = dailyDeltas
            .Where(metric => metric.SnapshotDate >= from && metric.SnapshotDate <= to)
            .ToArray();

        var lines = new StringBuilder();
        lines.AppendLine("You are a practical marketing analyst for a small ecommerce business.");
        lines.AppendLine("Analyze the following social media metrics and write one concise bilingual report.");
        lines.AppendLine("Write the Chinese version first, then an English version with the same findings.");
        lines.AppendLine("Use these Markdown sections exactly:");
        lines.AppendLine("## 中文报告");
        lines.AppendLine("### 总结");
        lines.AppendLine("### 平台变化");
        lines.AppendLine("### 异常波动");
        lines.AppendLine("### 下一步行动");
        lines.AppendLine("### 数据注意事项");
        lines.AppendLine("## English Report");
        lines.AppendLine("### Summary");
        lines.AppendLine("### Platform Changes");
        lines.AppendLine("### Unusual Drops or Spikes");
        lines.AppendLine("### Next Actions");
        lines.AppendLine("### Data Caveats");
        lines.AppendLine("Keep both languages practical and specific. Do not invent metrics that are not present.");
        lines.AppendLine("Do not only analyze the strongest or most eye-catching numbers.");
        lines.AppendLine("You must cover every platform/account listed in Account coverage, even when the values are flat, missing for the period, or changed by only 1.");
        lines.AppendLine("For each platform/account, summarize views, likes, comments, clicks, shares, and followers when present, including unchanged values and small changes.");
        lines.AppendLine("Use the delta rows as the required platform-by-platform review data. Highlight unusual movement only after the complete review.");
        lines.AppendLine("When Stored daily deltas are present, use them as the source of daily and weekly change totals.");
        lines.AppendLine("Daily reports compare the target day with the previous available snapshot and include year-to-date context.");
        lines.AppendLine("Weekly reports compare the selected week with the previous week.");
        lines.AppendLine($"Period: {normalizedPeriod}");
        lines.AppendLine($"Current date range: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        lines.AppendLine($"Comparison date range: {comparison.From:yyyy-MM-dd} to {comparison.To:yyyy-MM-dd}");
        lines.AppendLine($"YTD context range: {ytdFrom:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        lines.AppendLine();
        lines.AppendLine("Account coverage:");

        var anyMetrics = false;
        var accountRows = new List<(SocialAccount Account, SocialMetricSnapshot? Current)>();
        foreach (var account in accounts)
        {
            var current = LatestMetric(account, from, to) ??
                LatestMetricOnOrBefore(account, to);
            var currentInPeriod = current is not null &&
                current.SnapshotDate >= from &&
                current.SnapshotDate <= to;
            var previous = current is not null
                ? PreviousMetricBefore(account, current.SnapshotDate)
                : LatestMetric(account, comparison.From, comparison.To) ?? PreviousMetricBefore(account, from);
            var ytdFirst = FirstMetric(account, ytdFrom, to);
            accountRows.Add((account, current));
            if (current is null)
            {
                lines.AppendLine(
                    $"- {account.Platform} / {account.AccountName}: currentSnapshot=none in {from:yyyy-MM-dd} to {to:yyyy-MM-dd}; " +
                    $"previousSnapshot={(previous is null ? "none" : previous.SnapshotDate.ToString("yyyy-MM-dd"))}; " +
                    "views current=0, previous=missing; likes current=0, previous=missing; " +
                    "comments current=0, previous=missing; clicks current=0, previous=missing; " +
                    "shares current=0, previous=missing; followers current=0, previous=missing");
                continue;
            }

            anyMetrics = true;
            lines.AppendLine(
                $"- {account.Platform} / {account.AccountName}: currentDate={current.SnapshotDate:yyyy-MM-dd}, " +
                $"currentInRequestedPeriod={currentInPeriod}, " +
                MetricChange("views", current.Views, previous?.Views, ytdFirst?.Views) + "; " +
                MetricChange("likes", current.Likes, previous?.Likes, ytdFirst?.Likes) + "; " +
                MetricChange("comments", current.Comments, previous?.Comments, ytdFirst?.Comments) + "; " +
                MetricChange("clicks", current.Clicks, previous?.Clicks, ytdFirst?.Clicks) + "; " +
                MetricChange("shares", current.Shares, previous?.Shares, ytdFirst?.Shares) + "; " +
                MetricChange("followers", current.Followers, previous?.Followers, ytdFirst?.Followers));
        }

        lines.AppendLine();
        lines.AppendLine("Current period platform totals:");
        foreach (var group in accountRows.GroupBy(item => item.Account.Platform).OrderBy(group => group.Key))
        {
            lines.AppendLine(
                $"- {group.Key}: accounts={group.Count()}, accountsWithCurrentSnapshots={group.Count(item => item.Current is not null)}, " +
                $"views={group.Sum(item => item.Current?.Views ?? 0)}, likes={group.Sum(item => item.Current?.Likes ?? 0)}, " +
                $"comments={group.Sum(item => item.Current?.Comments ?? 0)}, clicks={group.Sum(item => item.Current?.Clicks ?? 0)}, " +
                $"shares={group.Sum(item => item.Current?.Shares ?? 0)}, followers={group.Sum(item => item.Current?.Followers ?? 0)}");
        }

        lines.AppendLine();
        lines.AppendLine("Stored daily deltas in the current period or latest available before the report date:");
        var currentAccountDeltas = currentPeriodDeltas
            .Where(item => item.Scope == "account")
            .GroupBy(item => item.SocialAccountId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var latestAvailableAccountDelta = dailyDeltas
            .Where(item => item.Scope == "account")
            .GroupBy(item => item.SocialAccountId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.SnapshotDate)
                    .Take(1)
                    .ToArray());
        foreach (var account in accounts)
        {
            var rows = currentAccountDeltas.GetValueOrDefault(account.Id) ?? [];
            var source = "currentPeriod";
            if (rows.Length == 0)
            {
                rows = latestAvailableAccountDelta.GetValueOrDefault(account.Id) ?? [];
                source = rows.Length > 0 ? "latestAvailableOutsidePeriod" : "none";
            }

            lines.AppendLine(
                $"- {account.Platform} / {account.AccountName}: source={source}, rows={rows.Length}, " +
                $"dates={FormatDateList(rows.Select(item => item.SnapshotDate))}, " +
                $"viewsDelta={rows.Sum(item => item.Views)}, likesDelta={rows.Sum(item => item.Likes)}, " +
                $"commentsDelta={rows.Sum(item => item.Comments)}, clicksDelta={rows.Sum(item => item.Clicks)}, " +
                $"sharesDelta={rows.Sum(item => item.Shares)}, followersDelta={rows.Sum(item => item.Followers)}");
        }

        lines.AppendLine();
        lines.AppendLine("Top content daily deltas in the current period:");
        foreach (var row in currentPeriodDeltas
            .Where(item => item.Scope == "content")
            .OrderByDescending(item => item.Views)
            .ThenByDescending(item => item.Likes)
            .Take(25))
        {
            lines.AppendLine(
                $"- {row.SocialAccount?.Platform} / {row.SocialAccount?.AccountName}: {row.Title}; " +
                $"date={row.SnapshotDate:yyyy-MM-dd}, viewsDelta={row.Views}, likesDelta={row.Likes}, " +
                $"commentsDelta={row.Comments}, clicksDelta={row.Clicks}");
        }

        lines.AppendLine();
        lines.AppendLine("Top content rows in the current period:");
        foreach (var row in currentContentRows
            .GroupBy(row => new { row.SocialAccountId, row.PlatformVideoId })
            .Select(group => group.OrderByDescending(row => row.SnapshotDate).First())
            .OrderByDescending(row => row.Views)
            .ThenByDescending(row => row.Likes)
            .Take(25))
        {
            var previous = comparisonContentRows
                .Where(item => item.SocialAccountId == row.SocialAccountId && item.PlatformVideoId == row.PlatformVideoId)
                .OrderByDescending(item => item.SnapshotDate)
                .FirstOrDefault();
            lines.AppendLine(
                $"- {row.SocialAccount?.Platform} / {row.SocialAccount?.AccountName}: {row.Title}; " +
                $"date={row.SnapshotDate:yyyy-MM-dd}, " +
                MetricChange("views", row.Views, previous?.Views, null) + "; " +
                MetricChange("likes", row.Likes, previous?.Likes, null) + "; " +
                MetricChange("comments", row.Comments, previous?.Comments, null) + "; " +
                MetricChange("clicks", row.Clicks, previous?.Clicks, null));
        }

        if (!anyMetrics)
        {
            lines.AppendLine("- No account metric snapshots found in this period.");
        }

        return lines.ToString();
    }

    private async Task<string> GenerateGeminiSummaryAsync(string prompt, CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(Model)}:generateContent?key={Uri.EscapeDataString(ApiKey!)}";
        var request = new GeminiGenerateRequest([
            new GeminiContent([
                new GeminiPart(prompt)
            ])
        ]);
        using var response = await httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini analysis failed: HTTP {(int)response.StatusCode}. {body}");
        }

        var result = JsonSerializer.Deserialize<GeminiGenerateResponse>(body, JsonOptions);
        var text = result?.Candidates?
            .SelectMany(candidate => candidate.Content?.Parts ?? [])
            .Select(part => part.Text)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(text) ? "Gemini returned an empty analysis." : text.Trim();
    }

    private async Task<string> SaveReportAsync(
        string period,
        DateOnly from,
        DateOnly to,
        string summary,
        string prompt,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(reportDirectory);
        var normalizedPeriod = NormalizePeriod(period);
        var path = Path.Combine(reportDirectory, $"social-ai-{normalizedPeriod}-{from:yyyyMMdd}-{to:yyyyMMdd}-{generatedAt:HHmmss}.md");
        var text = $"""
        ---
        period: {normalizedPeriod}
        from: {from:yyyy-MM-dd}
        to: {to:yyyy-MM-dd}
        generatedAt: {generatedAt:o}
        model: {Model}
        ---

        # Social AI Analysis ({from:yyyy-MM-dd} to {to:yyyy-MM-dd})

        {summary}

        ## Prompt data

        ```text
        {prompt}
        ```
        """;
        await File.WriteAllTextAsync(path, text, cancellationToken);
        return path;
    }

    private static (DateOnly From, DateOnly To) ResolveRange(string period, DateOnly date)
    {
        var normalized = NormalizePeriod(period);
        if (normalized == "weekly")
        {
            var diff = ((int)date.DayOfWeek + 6) % 7;
            var monday = date.AddDays(-diff);
            return (monday, monday.AddDays(6));
        }

        return (date, date);
    }

    private static (DateOnly From, DateOnly To) ResolveComparisonRange(string period, DateOnly from, DateOnly to)
    {
        if (NormalizePeriod(period) == "weekly")
        {
            return (from.AddDays(-7), to.AddDays(-7));
        }

        return (from.AddDays(-1), to.AddDays(-1));
    }

    private static string NormalizePeriod(string period) =>
        string.Equals(period, "weekly", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(period, "week", StringComparison.OrdinalIgnoreCase)
            ? "weekly"
            : "daily";

    private static SocialMetricSnapshot? LatestMetric(SocialAccount account, DateOnly from, DateOnly to) =>
        account.Metrics
            .Where(metric => metric.SnapshotDate >= from && metric.SnapshotDate <= to)
            .OrderByDescending(metric => metric.SnapshotDate)
            .FirstOrDefault();

    private static SocialMetricSnapshot? LatestMetricOnOrBefore(SocialAccount account, DateOnly date) =>
        account.Metrics
            .Where(metric => metric.SnapshotDate <= date)
            .OrderByDescending(metric => metric.SnapshotDate)
            .FirstOrDefault();

    private static SocialMetricSnapshot? FirstMetric(SocialAccount account, DateOnly from, DateOnly to) =>
        account.Metrics
            .Where(metric => metric.SnapshotDate >= from && metric.SnapshotDate <= to)
            .OrderBy(metric => metric.SnapshotDate)
            .FirstOrDefault();

    private static SocialMetricSnapshot? PreviousMetricBefore(SocialAccount account, DateOnly date) =>
        account.Metrics
            .Where(metric => metric.SnapshotDate < date)
            .OrderByDescending(metric => metric.SnapshotDate)
            .FirstOrDefault();

    private static string MetricChange(string label, int current, int? previous, int? ytdStart)
    {
        var previousText = previous.HasValue
            ? $"{previous.Value}->{current}, delta={current - previous.Value}, pct={PercentChange(previous.Value, current)}"
            : $"current={current}, previous=missing";
        var ytdText = ytdStart.HasValue
            ? $", ytdDelta={current - ytdStart.Value}, ytdPct={PercentChange(ytdStart.Value, current)}"
            : "";
        return $"{label} {previousText}{ytdText}";
    }

    private static string PercentChange(int previous, int current)
    {
        if (previous == 0)
        {
            return current == 0 ? "0%" : "new";
        }

        var percent = ((current - previous) * 100.0) / previous;
        return $"{percent:0.##}%";
    }

    private static string FormatDateList(IEnumerable<DateOnly> dates)
    {
        var values = dates
            .Distinct()
            .OrderBy(date => date)
            .Select(date => date.ToString("yyyy-MM-dd"))
            .ToArray();
        return values.Length == 0 ? "none" : string.Join(",", values);
    }

    private bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    private string? ApiKey =>
        configuration["AiAnalysis:GeminiApiKey"] ??
        Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    private string Model => configuration["AiAnalysis:GeminiModel"] ?? "gemini-1.5-flash";

    private static string? ExtractFrontMatter(string text, string key)
    {
        foreach (var line in text.Split('\n').Take(20))
        {
            if (line.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
            {
                return line[(key.Length + 1)..].Trim();
            }
        }

        return null;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var date) ? date : null;

    private static string Preview(string text)
    {
        var body = text.Contains("---", StringComparison.Ordinal)
            ? string.Join('\n', text.Split("---").Skip(2)).Trim()
            : text.Trim();
        body = body.Replace("\r", "").Trim();
        return body.Length <= 360 ? body : body[..360] + "...";
    }

    private string? ResolveReportPath(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) ||
            !safeName.StartsWith("social-ai-", StringComparison.OrdinalIgnoreCase) ||
            !safeName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Directory.CreateDirectory(reportDirectory);
        var root = Path.GetFullPath(reportDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, safeName));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private sealed record GeminiGenerateRequest(IReadOnlyList<GeminiContent> Contents);
    private sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiPart(string Text);
    private sealed record GeminiGenerateResponse(IReadOnlyList<GeminiCandidate>? Candidates);
    private sealed record GeminiCandidate(GeminiContent? Content);
}
