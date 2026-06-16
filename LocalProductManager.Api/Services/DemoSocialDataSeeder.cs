using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LocalProductManager.Api.Services;

public static class DemoSocialDataSeeder
{
    private static readonly DemoAccount[] Accounts =
    [
        new("facebook", "Demo Facebook", "@demo-facebook", "https://www.facebook.com/demo", "#1877f2", 38, 220, 1800, 12, 18, 1240),
        new("instagram", "Demo Instagram", "@demo.instagram", "https://www.instagram.com/demo", "#c13584", 52, 410, 2600, 24, 31, 2860),
        new("pinterest", "Demo Pinterest", "@demo-pins", "https://www.pinterest.com/demo", "#bd081c", 64, 180, 4200, 8, 54, 1540),
        new("tiktok", "Demo TikTok", "@demo.tiktok", "https://www.tiktok.com/@demo", "#111827", 71, 560, 9300, 42, 67, 3780)
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        foreach (var demo in Accounts)
        {
            var account = await db.SocialAccounts
                .Include(item => item.Metrics)
                .FirstOrDefaultAsync(
                    item => item.Platform == demo.Platform && item.AccountName == demo.AccountName,
                    cancellationToken);

            if (account is null)
            {
                account = new SocialAccount
                {
                    Platform = demo.Platform,
                    AccountName = demo.AccountName,
                    AccountHandle = demo.Handle,
                    AccountUrl = demo.Url,
                    Color = demo.Color,
                    IsActive = true
                };
                db.SocialAccounts.Add(account);
            }

            for (var monthOffset = 60; monthOffset >= 1; monthOffset--)
            {
                var index = 60 - monthOffset;
                var date = today.AddMonths(-monthOffset);
                UpsertMetric(account, demo, date, index, 28, 210, 5);
            }

            for (var dayOffset = 30; dayOffset >= 0; dayOffset--)
            {
                var index = 30 - dayOffset;
                var date = today.AddDays(-dayOffset);
                UpsertMetric(account, demo, date, index + 61, 9, 260, 3);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void UpsertMetric(
        SocialAccount account,
        DemoAccount demo,
        DateOnly date,
        int index,
        int clickStep,
        int viewStep,
        int commentStep)
    {
        var metric = account.Metrics.FirstOrDefault(item => item.SnapshotDate == date);
        if (metric is null)
        {
            metric = new SocialMetricSnapshot
            {
                SocialAccount = account,
                SnapshotDate = date
            };
            account.Metrics.Add(metric);
        }

        metric.Clicks = demo.Clicks + index * clickStep + index % 2 * 5;
        metric.Likes = demo.Likes + index * (clickStep + 8) + index % 3 * 8;
        metric.Views = demo.Views + index * viewStep + index % 2 * 110;
        metric.Comments = demo.Comments + index * commentStep + index % 2;
        metric.Shares = demo.Shares + index * (commentStep + 1) + index % 3;
        metric.Followers = demo.Followers + index * 22 + index % 2 * 7;
        metric.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private sealed record DemoAccount(
        string Platform,
        string AccountName,
        string Handle,
        string Url,
        string Color,
        int Clicks,
        int Likes,
        int Views,
        int Comments,
        int Shares,
        int Followers);
}
