using System.Net.Http;
using System.Net.Sockets;
using System.Text;
namespace AuctionPrintRelay.Wpf;
public sealed class BarTenderClient
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMinutes(10) };
    public async Task ProbeAsync(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("BarTender endpoint is invalid.");
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(uri.Host, uri.Port, timeout.Token);
    }

    public async Task<string> SendAsync(string endpoint, PrintRequest item)
    {
        if (item.Test) throw new InvalidOperationException("Connection tests must use ProbeAsync, not a print request.");
        var rows = new List<PrintRequest>();
        var copies = Math.Clamp(item.Copies, 1, 20);
        for (var index = 0; index < copies; index++) rows.Add(item);
        return await SendRowsAsync(endpoint, rows);
    }

    public Task<string> SendBatchAsync(string endpoint, BulkPrintRequest batch)
    {
        var rows = new List<PrintRequest>();
        foreach (var item in batch.Items)
        {
            for (var index = 0; index < Math.Clamp(item.Copies, 1, 20); index++)
                rows.Add(new PrintRequest
                {
                    BuyerName = item.BuyerName,
                    ProductNumber = item.ProductNumber,
                    SessionId = batch.SessionId
                });
        }
        return SendRowsAsync(endpoint, rows);
    }

    internal static string BuildCsv(IReadOnlyCollection<PrintRequest> rows)
    {
        var csv = new StringBuilder("action,BuyerName,ProductNumber,SessionId\r\n");
        foreach (var row in rows)
            csv.Append("print,")
                .Append(Escape(row.BuyerName)).Append(',')
                .Append(Escape(row.ProductNumber)).Append(',')
                .Append(Escape(row.SessionId)).Append("\r\n");
        return csv.ToString();
    }

    private async Task<string> SendRowsAsync(string endpoint, IReadOnlyCollection<PrintRequest> rows)
    {
        using var content = new StringContent(BuildCsv(rows), Encoding.UTF8, "text/csv");
        using var response = await _client.PostAsync(endpoint, content);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"BarTender HTTP {(int)response.StatusCode}: {text}");
        return text;
    }

    private static string Escape(string value) => '"' + (value ?? "").Replace("\"", "\"\"") + '"';
}
