using System.Text.Json;

namespace LocalProductManager.Api.Services;

public sealed record SocialFetchLogEntry(
    DateTimeOffset Timestamp,
    string Source,
    string Status,
    int MetricsImported,
    string Message);

public sealed class SocialFetchLogService(IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string logDirectory = Path.Combine(environment.ContentRootPath, "logging");

    public async Task RecordAsync(
        string source,
        string status,
        int metricsImported,
        string message,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(logDirectory);
        var entry = new SocialFetchLogEntry(
            DateTimeOffset.Now,
            source,
            status,
            metricsImported,
            message);
        var path = LogPath(entry.Timestamp);
        await File.AppendAllTextAsync(
            path,
            JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SocialFetchLogEntry>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(logDirectory);
        var files = Directory
            .EnumerateFiles(logDirectory, "social-fetch-*.jsonl")
            .OrderByDescending(path => path)
            .Take(6)
            .ToArray();

        var entries = new List<SocialFetchLogEntry>();
        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, cancellationToken);
            foreach (var line in lines.Reverse())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<SocialFetchLogEntry>(line);
                    if (entry is not null) entries.Add(entry);
                }
                catch (JsonException)
                {
                    // Ignore a partially written or manually edited line.
                }
            }
        }

        return entries
            .OrderByDescending(entry => entry.Timestamp)
            .Take(limit)
            .ToArray();
    }

    public async Task<string> ArchiveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(logDirectory);
        var entries = await GetRecentAsync(500, cancellationToken);
        var archivePath = Path.Combine(
            logDirectory,
            $"social-fetch-archive-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(
            archivePath,
            JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        return archivePath;
    }

    private string LogPath(DateTimeOffset timestamp) =>
        Path.Combine(logDirectory, $"social-fetch-{timestamp:yyyy-MM}.jsonl");
}
