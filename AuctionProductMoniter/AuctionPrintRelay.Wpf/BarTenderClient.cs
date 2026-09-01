using System.Net.Http;
using System.Net.Http.Json;
namespace AuctionPrintRelay.Wpf;
public sealed class BarTenderClient
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };
    public async Task<string> SendAsync(string endpoint, PrintRequest item)
    {
        var payload = new { action = item.Test ? "test" : "print", source = "AuctionPrintRelay.Wpf", sessionId = item.SessionId,
            sessionDate = DateTime.Now.ToString("yyyy-MM-dd"), buyerName = item.BuyerName, productNumber = item.ProductNumber,
            copies = item.Test ? 1 : Math.Clamp(item.Copies, 1, 20), detectedAt = DateTimeOffset.Now, requestedAt = DateTimeOffset.UtcNow };
        using var response = await _client.PostAsJsonAsync(endpoint, payload);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"BarTender HTTP {(int)response.StatusCode}: {text}");
        return text;
    }
}
