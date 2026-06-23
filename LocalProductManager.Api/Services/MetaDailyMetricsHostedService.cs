using LocalProductManager.Api.Data;

namespace LocalProductManager.Api.Services;

public sealed class MetaDailyMetricsHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<MetaDailyMetricsHostedService> logger,
    SocialFetchLogService fetchLogs) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextRun();
            logger.LogInformation("Next Meta daily metrics fetch scheduled in {Delay}.", delay);
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
                var meta = scope.ServiceProvider.GetRequiredService<MetaOAuthService>();
                var imported = await meta.SyncAsync(db, stoppingToken);
                logger.LogInformation("Meta daily metrics fetch completed. Imported {Count} snapshots.", imported);
                await fetchLogs.RecordAsync(
                    "meta-daily",
                    "success",
                    imported,
                    "Meta Facebook/Instagram account metrics synced.",
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException ex)
            {
                logger.LogInformation(ex, "Meta daily metrics fetch skipped.");
                await fetchLogs.RecordAsync("meta-daily", "skipped", 0, ex.Message, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Meta daily metrics fetch failed.");
                await fetchLogs.RecordAsync("meta-daily", "failed", 0, ex.Message, stoppingToken);
            }
        }
    }

    private static TimeSpan DelayUntilNextRun()
    {
        var now = DateTime.Now;
        var nextRun = now.Date.AddDays(1).AddMinutes(5);
        return nextRun - now;
    }
}
