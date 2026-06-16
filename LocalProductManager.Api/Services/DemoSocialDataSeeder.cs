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

            for (var dayOffset = 6; dayOffset >= 0; dayOffset--)
            {
                var index = 6 - dayOffset;
                var date = today.AddDays(-dayOffset);
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

                metric.Clicks = demo.Clicks + index * 9 + index % 2 * 5;
                metric.Likes = demo.Likes + index * 17 + index % 3 * 8;
                metric.Views = demo.Views + index * 260 + index % 2 * 110;
                metric.Comments = demo.Comments + index * 3 + index % 2;
                metric.Shares = demo.Shares + index * 4 + index % 3;
                metric.Followers = demo.Followers + index * 22 + index % 2 * 7;
                metric.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
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
