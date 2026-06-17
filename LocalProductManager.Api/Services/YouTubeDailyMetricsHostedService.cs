using LocalProductManager.Api.Data;

namespace LocalProductManager.Api.Services;

public sealed class YouTubeDailyMetricsHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<YouTubeDailyMetricsHostedService> logger,
    SocialFetchLogService fetchLogs) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextMidnight();
            logger.LogInformation("Next YouTube metrics fetch scheduled in {Delay}.", delay);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var youtube = scope.ServiceProvider.GetRequiredService<YouTubeOAuthService>();
                var imported = await youtube.RecordDailySnapshotAsync(db, stoppingToken);
                logger.LogInformation("YouTube daily metrics fetch completed. Imported {Count} snapshots.", imported);
                await fetchLogs.RecordAsync(
                    "youtube-daily",
                    "success",
                    imported,
                    $"Daily YouTube metrics fetch completed. Imported {imported} snapshots.",
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "YouTube daily metrics fetch failed.");
                await fetchLogs.RecordAsync(
                    "youtube-daily",
                    "failed",
                    0,
                    ex.Message,
                    stoppingToken);
            }
        }
    }

    private static TimeSpan DelayUntilNextMidnight()
    {
        var now = DateTime.Now;
        var nextMidnight = now.Date.AddDays(1);
        return nextMidnight - now;
    }
}
