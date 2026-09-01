using System.Net.Http;
using System.Net.Http.Json;

namespace AuctionProductMonitor.Wpf;

public sealed class BarTenderPrintClient
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string> SendAsync(string endpoint, SaleEvent? sale, bool test, int copies = 1)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("BarTender地址无效。");

        var payload = new
        {
            action = test ? "test" : "print",
            source = "AuctionProductMonitor.Wpf",
            sessionId = sale?.SessionId ?? "connection-test",
            sessionDate = sale?.DetectedAt.ToLocalTime().ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd"),
            buyerName = sale?.BuyerName ?? "connection_test",
            productNumber = sale?.ProductNumber ?? "0",
            copies = Math.Clamp(copies, 1, 20),
            detectedAt = sale?.DetectedAt ?? DateTimeOffset.Now,
            requestedAt = DateTimeOffset.UtcNow
        };

        using var response = await _httpClient.PostAsJsonAsync(uri, payload);
        var responseText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"BarTender返回HTTP {(int)response.StatusCode}：{responseText}");

        return responseText;
    }
}
