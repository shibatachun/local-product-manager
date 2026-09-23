using System.Net.Http;
using System.Net.Http.Json;

namespace AuctionProductMonitor.Wpf;

public sealed class RemotePrintClient
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<string> SendAsync(string endpoint, string apiKey, SaleEvent? sale, bool test, int copies = 1)
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
            copies = test ? 0 : Math.Clamp(copies, 1, 20),
            test
        });
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Relay HTTP {(int)response.StatusCode}: {text}");
        return text;
    }

    public async Task<string> SendBatchAsync(string endpoint, string apiKey, IReadOnlyCollection<SaleEvent> sales)
    {
        if (sales.Count == 0) throw new ArgumentException("At least one sale is required.", nameof(sales));
        var uri = ParseEndpoint(endpoint);
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/bulk" };
        using var request = new HttpRequestMessage(HttpMethod.Post, builder.Uri);
        request.Headers.Add("X-Api-Key", apiKey.Trim());
        request.Content = JsonContent.Create(new
        {
            sessionId = sales.First().SessionId,
            items = sales.Select(sale => new
            {
                buyerName = sale.BuyerName,
                productNumber = sale.ProductNumber,
                copies = Math.Clamp(sale.Quantity, 1, 20)
            })
        });
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Relay HTTP {(int)response.StatusCode}: {text}");
        return text;
    }

    private static Uri ParseEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Relay address is invalid.");
        return uri;
    }
}
