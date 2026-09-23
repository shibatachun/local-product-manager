using System.Net;
using System.Net.Http.Json;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LocalProductManager.Api.Tests;

public sealed class LiveStreamBidShowSessionTests
{
    [Fact]
    public async Task DeleteBatch_RemovesImportedFileReservation()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        const string batchNumber = "20260914_111401_BarTender";
        var sessionId = Guid.NewGuid().ToString("N");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AppUserSessions.Add(new AppUserSession
            {
                Id = sessionId,
                UserName = "import-test",
                Role = "admin",
                AppUser = new AppUser
                {
                    UserName = "import-test",
                    PasswordHash = "test",
                    Role = "admin",
                    CanImportData = true
                }
            });
            db.LiveStreamBidProducts.Add(new LiveStreamBidProduct
            {
                BatchNumber = batchNumber,
                BatchProductNumber = 1,
                Uuid = Guid.NewGuid().ToString("D"),
                Title = "Imported product",
                Quantity = 1,
                Inventory = 1,
                UnitPrice = 1
            });
            db.ImportedBidBatches.Add(new ImportedBidBatch
            {
                BatchNumber = batchNumber,
                FileName = $"{batchNumber}.csv",
                Sha256 = new string('a', 64)
            });
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/live-stream-bid-products/batches/{batchNumber}");
        request.Headers.Add("Cookie", $"lpm_session={sessionId}");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var checkScope = factory.Services.CreateAsyncScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(checkDb.LiveStreamBidProducts.Any(item => item.BatchNumber == batchNumber));
        Assert.False(checkDb.ImportedBidBatches.Any(item => item.BatchNumber == batchNumber));
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("BID|{0}")]
    [InlineData("BID:{0}")]
    [InlineData("BID-{0}")]
    [InlineData("BID {0}")]
    public async Task Lookup_FindsStoredBidUuidRegardlessOfLabelPrefix(string storedFormat)
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var uuid = Guid.NewGuid().ToString("D");
        var storedUuid = string.Format(storedFormat, uuid);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LiveStreamBidProducts.Add(new LiveStreamBidProduct
            {
                Uuid = storedUuid,
                Title = "Lookup test product",
                Quantity = 1,
                Inventory = 1,
                ActualInventory = 1,
                UnitPrice = 1
            });
            await db.SaveChangesAsync();
        }

        var scanned = Uri.EscapeDataString($"BID|{uuid.ToUpperInvariant()}");
        var result = await client.GetAsync($"/api/live-stream-bid-products/lookup?uuid={scanned}");
        result.EnsureSuccessStatusCode();
        var product = await result.Content.ReadFromJsonAsync<LookupProduct>();
        Assert.Equal(uuid, product?.Uuid, ignoreCase: true);
    }

    [Fact]
    public async Task ShowSessions_PersistGroupSalesAndIncrementWithinTheDay()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();

        var firstResponse = await client.PostAsJsonAsync("/api/live-stream-bid-products/show-sessions/start", new { });
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<ShowSummary>();
        Assert.NotNull(first);
        Assert.Equal(1, first.ShowNumber);
        Assert.True(first.IsActive);

        var active = await client.GetFromJsonAsync<ShowSummary>("/api/live-stream-bid-products/show-sessions/active");
        Assert.NotNull(active);
        Assert.Equal(first.Id, active.Id);

        var duplicateStart = await client.PostAsJsonAsync("/api/live-stream-bid-products/show-sessions/start", new { });
        Assert.Equal(HttpStatusCode.Conflict, duplicateStart.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = new LiveStreamBidProduct
            {
                Uuid = Guid.NewGuid().ToString(),
                Title = "Show session test product",
                Quantity = 2,
                Inventory = 1,
                ActualInventory = 1,
                UnitPrice = 10,
                TotalCost = 25
            };
            db.LiveStreamBidProducts.Add(product);
            await db.SaveChangesAsync();
            db.LiveStreamBidSales.Add(new LiveStreamBidSale
            {
                ShowSessionId = first.Id,
                BidProductId = product.Id,
                ProductUuid = product.Uuid,
                ProductTitle = product.Title,
                Quantity = product.Quantity,
                Cost = product.TotalCost,
                SaleDate = first.Date,
                SoldAt = DateTimeOffset.UtcNow,
                SoldBy = "test"
            });
            await db.SaveChangesAsync();
        }

        var settledResponse = await client.PostAsJsonAsync($"/api/live-stream-bid-products/show-sessions/{first.Id}/stop", new { });
        settledResponse.EnsureSuccessStatusCode();
        var settled = await settledResponse.Content.ReadFromJsonAsync<ShowSummary>();
        Assert.NotNull(settled);
        Assert.False(settled.IsActive);
        Assert.Equal(1, settled.SoldCount);
        Assert.Equal(1, settled.InventorySold);
        Assert.Equal(25, settled.TotalCostRmb);
        Assert.Single(settled.Sales);

        var secondResponse = await client.PostAsJsonAsync("/api/live-stream-bid-products/show-sessions/start", new { });
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<ShowSummary>();
        Assert.NotNull(second);
        Assert.Equal(first.Date, second.Date);
        Assert.Equal(2, second.ShowNumber);

        var day = await client.GetFromJsonAsync<DailySummary>($"/api/live-stream-bid-products/sales/by-date?date={first.Date:yyyy-MM-dd}");
        Assert.NotNull(day);
        Assert.Equal(1, day.InventorySold);
        Assert.Equal(2, day.Shows.Count);
        Assert.Equal(25, day.TotalCostRmb);
        Assert.Equal([1, 2], day.Shows.Select(item => item.ShowNumber).ToArray());
    }

    private sealed record SaleSummary(int Id);

    private sealed record LookupProduct(string Uuid);

    private sealed record ShowSummary(
        int Id,
        DateOnly Date,
        int ShowNumber,
        bool IsActive,
        int SoldCount,
        int InventorySold,
        decimal TotalCostRmb,
        IReadOnlyList<SaleSummary> Sales);

    private sealed record DailySummary(
        DateOnly Date,
        int InventorySold,
        decimal TotalCostRmb,
        IReadOnlyList<ShowSummary> Shows);
}
