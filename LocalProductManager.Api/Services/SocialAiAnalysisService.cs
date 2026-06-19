using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Dtos;
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
            ? "Gemini API key is configured."
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

    private async Task<string> BuildPromptAsync(
        string period,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var accounts = await db.SocialAccounts
            .AsNoTracking()
            .Include(account => account.Metrics)
            .OrderBy(account => account.Platform)
            .ThenBy(account => account.AccountName)
            .ToListAsync(cancellationToken);
        var videoRows = await db.SocialVideoMetricSnapshots
            .AsNoTracking()
            .Include(metric => metric.SocialAccount)
            .Where(metric => metric.SnapshotDate >= from && metric.SnapshotDate <= to)
            .ToListAsync(cancellationToken);

        var lines = new StringBuilder();
        lines.AppendLine("You are a practical marketing analyst for a small ecommerce business.");
        lines.AppendLine("Analyze the following social media metrics and write a concise Chinese report.");
        lines.AppendLine("The report should include: 1) executive summary, 2) platform changes, 3) unusual drops/spikes, 4) action items for tomorrow/next week, 5) data caveats.");
        lines.AppendLine($"Period: {NormalizePeriod(period)}");
        lines.AppendLine($"Date range: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
        lines.AppendLine();
        lines.AppendLine("Account snapshots:");

        foreach (var account in accounts)
        {
            var metrics = account.Metrics
                .Where(metric => metric.SnapshotDate >= from && metric.SnapshotDate <= to)
                .OrderBy(metric => metric.SnapshotDate)
                .ToArray();
            if (metrics.Length == 0) continue;

            var first = metrics.First();
            var last = metrics.Last();
            lines.AppendLine(
                $"- {account.Platform} / {account.AccountName}: rows={metrics.Length}, " +
                $"views {first.Views}->{last.Views}, likes {first.Likes}->{last.Likes}, comments {first.Comments}->{last.Comments}, " +
                $"clicks {first.Clicks}->{last.Clicks}, shares {first.Shares}->{last.Shares}, followers {first.Followers}->{last.Followers}");
        }

        lines.AppendLine();
        lines.AppendLine("Top content rows in the period:");
        foreach (var row in videoRows
            .OrderByDescending(row => row.Views)
            .ThenByDescending(row => row.Likes)
            .Take(25))
        {
            lines.AppendLine(
                $"- {row.SocialAccount?.Platform} / {row.SocialAccount?.AccountName}: {row.Title}; " +
                $"date={row.SnapshotDate:yyyy-MM-dd}, views={row.Views}, likes={row.Likes}, comments={row.Comments}, clicks={row.Clicks}");
        }

        if (!accounts.Any(account => account.Metrics.Any(metric => metric.SnapshotDate >= from && metric.SnapshotDate <= to)))
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

    private static string NormalizePeriod(string period) =>
        string.Equals(period, "weekly", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(period, "week", StringComparison.OrdinalIgnoreCase)
            ? "weekly"
            : "daily";

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

    private sealed record GeminiGenerateRequest(IReadOnlyList<GeminiContent> Contents);
    private sealed record GeminiContent(IReadOnlyList<GeminiPart> Parts);
    private sealed record GeminiPart(string Text);
    private sealed record GeminiGenerateResponse(IReadOnlyList<GeminiCandidate>? Candidates);
    private sealed record GeminiCandidate(GeminiContent? Content);
}
