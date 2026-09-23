using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Text;
namespace AuctionPrintRelay.Wpf;
public sealed class RelayServer : IAsyncDisposable
{
    private readonly RelaySettings _settings;
    private readonly BarTenderClient _barTender;
    private WebApplication? _app;
    public event Action<PrintLog>? Logged;
    public RelayServer(RelaySettings settings, BarTenderClient barTender) { _settings = settings; _barTender = barTender; }
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{RelaySettings.Port}");
        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        app.MapPost("/api/print", async (HttpContext context, PrintRequest item) =>
        {
            var supplied = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? "";
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(_settings.ApiKey)))
                return Results.Json(new { success = false, error = "invalid access key" }, statusCode: 401);
            if (string.IsNullOrWhiteSpace(item.BuyerName) || string.IsNullOrWhiteSpace(item.ProductNumber))
                return Results.BadRequest(new { success = false, error = "buyerName and productNumber are required" });
            try
            {
                if (item.Test)
                    await _barTender.ProbeAsync(_settings.BarTenderEndpoint);
                else
                    await _barTender.SendAsync(_settings.BarTenderEndpoint, item);
                Logged?.Invoke(new(DateTimeOffset.Now, item.BuyerName, item.ProductNumber, item.Test ? "Test OK" : "Printed"));
                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                Logged?.Invoke(new(DateTimeOffset.Now, item.BuyerName, item.ProductNumber, "Failed: " + ex.Message));
                return Results.Json(new { success = false, error = ex.Message }, statusCode: 502);
            }
        });
        app.MapPost("/api/print/bulk", async (HttpContext context, BulkPrintRequest batch) =>
        {
            var supplied = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? "";
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(_settings.ApiKey)))
                return Results.Json(new { success = false, error = "invalid access key" }, statusCode: 401);
            if (batch.Items.Count == 0)
                return Results.BadRequest(new { success = false, error = "items are required" });
            if (batch.Items.Any(item => string.IsNullOrWhiteSpace(item.BuyerName) || string.IsNullOrWhiteSpace(item.ProductNumber)))
                return Results.BadRequest(new { success = false, error = "every item needs buyerName and productNumber" });
            var labelCount = batch.Items.Sum(item => Math.Clamp(item.Copies, 1, 20));
            if (labelCount > 1000)
                return Results.BadRequest(new { success = false, error = "bulk print is limited to 1000 labels" });
            try
            {
                await _barTender.SendBatchAsync(_settings.BarTenderEndpoint, batch);
                Logged?.Invoke(new(DateTimeOffset.Now, $"{batch.Items.Count} orders", $"{labelCount} labels", "Bulk printed"));
                return Results.Ok(new { success = true, orders = batch.Items.Count, labels = labelCount });
            }
            catch (Exception ex)
            {
                Logged?.Invoke(new(DateTimeOffset.Now, $"{batch.Items.Count} orders", $"{labelCount} labels", "Bulk failed: " + ex.Message));
                return Results.Json(new { success = false, error = ex.Message }, statusCode: 502);
            }
        });
        _app = app; await app.StartAsync();
    }
    public async ValueTask DisposeAsync() { if (_app is null) return; await _app.StopAsync(); await _app.DisposeAsync(); }
}
