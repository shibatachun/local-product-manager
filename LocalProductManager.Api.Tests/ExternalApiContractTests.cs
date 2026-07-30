using System.Net;

namespace LocalProductManager.Api.Tests;

public sealed class ExternalApiContractTests
{
    [Fact]
    public async Task ExternalIntegrationReadEndpoints_RespondWithoutServerErrors()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        await IsolatedApiFactory.LoginAsAdminAsync(client);

        var paths = new[]
        {
            "/api/youtube/auth/status",
            "/api/youtube/videos",
            "/api/youtube/videos/debug",
            "/api/pinterest/auth/status",
            "/api/pinterest/pins",
            "/api/meta/auth/status",
            "/api/instagram/posts",
            "/api/facebook/posts",
            "/api/amazon-marketing/products",
            "/api/social-ai-analysis/status",
            "/api/social-ai-analysis/reports?limit=10",
            "/api/social-ai-analysis/reports/missing.md",
            "/api/social-ai-analysis/reports/missing.md/download",
            "/api/social-fetch-logs",
            "/api/images/roots",
            "/api/images/host/browse?path=missing",
            "/api/images/preview?path=missing&download=false",
            "/api/images/remote-preview?url=invalid",
            "/api/products/999999/amazon-attributions"
        };

        foreach (var path in paths)
        {
            var response = await client.GetAsync(path);
            Assert.True(
                (int)response.StatusCode < 500,
                $"{path} returned unexpected server error {(int)response.StatusCode}.");
        }
    }

    [Fact]
    public async Task OAuthAndImportEndpoints_RejectMissingOrInvalidInputWithoutExternalTraffic()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        await IsolatedApiFactory.LoginAsAdminAsync(client);

        foreach (var platform in new[] { "youtube", "pinterest", "meta" })
        {
            var start = await client.GetAsync($"/api/{platform}/auth/start");
            Assert.True((int)start.StatusCode < 500, $"{platform} auth/start returned a server error.");

            var callback = await client.GetAsync($"/api/{platform}/oauth/callback?error=isolated-test");
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);

            var disconnect = await client.PostAsync($"/api/{platform}/auth/disconnect", null);
            Assert.True(
                disconnect.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
                $"{platform} disconnect returned {disconnect.StatusCode}.");
        }

        var emptyPostPaths = new[]
        {
            "/api/imports/amazon-business-report",
            "/api/imports/amazon-attributions",
            "/api/imports/amazon-attribution-files",
            "/api/pinterest/report/import",
            "/api/instagram/report/import",
            "/api/facebook/report/import",
            "/api/tiktok/report/import",
            "/api/products/999999/amazon-attributions/import",
            "/api/products/999999/image/upload"
        };

        foreach (var path in emptyPostPaths)
        {
            var response = await client.PostAsync(path, null);
            Assert.True(
                (int)response.StatusCode is >= 400 and < 500,
                $"{path} should reject a missing upload with a 4xx response but returned {(int)response.StatusCode}.");
        }
    }
}
