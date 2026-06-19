namespace LocalProductManager.Api.Services;

public sealed class SocialAiAnalysisHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SocialAiAnalysisHostedService> logger,
    SocialFetchLogService fetchLogs) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextAnalysisTime();
            logger.LogInformation("Next social AI analysis scheduled in {Delay}.", delay);
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
                var analysis = scope.ServiceProvider.GetRequiredService<SocialAiAnalysisService>();
                var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));
                var result = await analysis.RunAsync("daily", yesterday, stoppingToken);
                await fetchLogs.RecordAsync(
                    "ai-analysis-daily",
                    "success",
                    1,
                    $"Daily social AI analysis generated: {result.Path}",
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Daily social AI analysis failed.");
                await fetchLogs.RecordAsync(
                    "ai-analysis-daily",
                    "failed",
                    0,
                    ex.Message,
                    stoppingToken);
            }
        }
    }

    private static TimeSpan DelayUntilNextAnalysisTime()
    {
        var now = DateTime.Now;
        var nextRun = now.Date.AddDays(1).AddMinutes(5);
        return nextRun - now;
    }
}
