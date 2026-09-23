using System.Net;
using LocalProductManager.Api.Data;
using LocalProductManager.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LocalProductManager.Api.Tests;

public sealed class LiveStreamBidFolderTests
{
    [Fact]
    public async Task OpenFolder_RejectsAdministratorWhenRequestIsNotFromHost()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var sessionId = Guid.NewGuid().ToString("N");
        int productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AppUserSessions.Add(new AppUserSession
            {
                Id = sessionId,
                UserName = "folder-test-admin",
                Role = "admin",
                AppUser = new AppUser
                {
                    UserName = "folder-test-admin",
                    PasswordHash = "test",
                    Role = "admin",
                    IsEnabled = true
                }
            });
            var product = new LiveStreamBidProduct
            {
                Uuid = Guid.NewGuid().ToString("D"),
                Title = "Folder test product",
                Quantity = 1,
                Inventory = 1,
                UnitPrice = 1
            };
            db.LiveStreamBidProducts.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/live-stream-bid-products/{productId}/image/open-folder");
        request.Headers.Add("Cookie", $"lpm_session={sessionId}");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
