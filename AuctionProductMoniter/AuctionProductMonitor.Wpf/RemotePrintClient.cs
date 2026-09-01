using System.Net.Http;
using System.Net.Http.Json;

namespace AuctionProductMonitor.Wpf;

public sealed class RemotePrintClient
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<string> SendAsync(string endpoint, string apiKey, SaleEvent? sale, bool test)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Relay address is invalid.");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("X-Api-Key", apiKey.Trim());
        request.Content = JsonContent.Create(new
        {
            buyerName = sale?.BuyerName ?? "connection_test",
            productNumber = sale?.ProductNumber ?? "0",
            sessionId = sale?.SessionId ?? "connection-test",
            copies = test ? 0 : 1,
            test
        });
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Relay HTTP {(int)response.StatusCode}: {text}");
        return text;
    }
}
