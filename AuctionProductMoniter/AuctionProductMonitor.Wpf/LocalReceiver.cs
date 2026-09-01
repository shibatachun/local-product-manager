using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AuctionProductMonitor.Wpf;

public sealed class LocalReceiver : IAsyncDisposable
{
    private WebApplication? _app;
    public event Action<SaleEvent>? SaleReceived;
    public Func<ScreenshotCapture, Task<bool>>? ScreenshotReceived { get; set; }

    public async Task StartAsync()
    {
        if (_app is not null) return;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:17891");
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.PropertyNameCaseInsensitive = true);

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*";
            context.Response.Headers.AccessControlAllowHeaders = "Content-Type";
            context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            await next();
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        app.MapPost("/api/sales", (SaleEvent sale) =>
        {
            sale.BuyerName = sale.BuyerName.Trim();
            sale.ProductNumber = sale.ProductNumber.Trim();
            if (string.IsNullOrWhiteSpace(sale.BuyerName) ||
                string.IsNullOrWhiteSpace(sale.ProductNumber))
            {
                return Results.BadRequest(new { error = "buyerName and productNumber are required" });
            }

            if (sale.DetectedAt == default) sale.DetectedAt = DateTimeOffset.Now;
            SaleReceived?.Invoke(sale);
            return Results.Ok(new { received = true });
        });

        app.MapPost("/api/screenshots", async (ScreenshotCapture capture) =>
        {
            capture.BuyerName = capture.BuyerName.Trim();
            capture.ProductNumber = capture.ProductNumber.Trim();
            if (string.IsNullOrWhiteSpace(capture.BuyerName) ||
                string.IsNullOrWhiteSpace(capture.ProductNumber) ||
                string.IsNullOrWhiteSpace(capture.ImageDataUrl))
                return Results.BadRequest(new { error = "buyerName, productNumber and imageDataUrl are required" });
            if (capture.CapturedAt == default) capture.CapturedAt = DateTimeOffset.Now;
            if (ScreenshotReceived is null || !await ScreenshotReceived(capture))
                return Results.Json(new { saved = false, error = "No matching live session sale was found." }, statusCode: StatusCodes.Status409Conflict);
            return Results.Ok(new { saved = true });
        });


        _app = app;
        await app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }
}
