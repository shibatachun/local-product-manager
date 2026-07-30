using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "inventory-https-gateway";
const string meterName = "LocalProductManager.Gateway";

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.AddOtlpExporter();
});
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithMetrics(metrics => metrics
        .AddMeter(meterName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
builder.Services.AddHttpClient("proxy", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();
var proxyClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("proxy");
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GatewayTraffic");
var meter = new Meter(meterName);
var requestCounter = meter.CreateCounter<long>("gateway.requests", "requests", "Requests received by the HTTPS gateway.");
var activeRequests = meter.CreateUpDownCounter<long>("gateway.active_requests", "requests", "Requests currently being processed.");
var durationHistogram = meter.CreateHistogram<double>("gateway.request.duration", "ms", "Gateway request duration.");
var requestBytesHistogram = meter.CreateHistogram<long>("gateway.request.bytes", "By", "Incoming request size.");
var responseBytesHistogram = meter.CreateHistogram<long>("gateway.response.bytes", "By", "Outgoing response size.");

app.Run(async context =>
{
    var startedAt = Stopwatch.GetTimestamp();
    var targetService = context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.StartsWithSegments("/health")
        || context.Request.Path.StartsWithSegments("/updates")
        ? "inventory-api"
        : "angular-dev-server";
    var upstreamBase = targetService == "inventory-api"
        ? "http://127.0.0.1:5077"
        : "http://127.0.0.1:4200";
    var targetUri = new Uri($"{upstreamBase}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}");
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var requestBytes = context.Request.ContentLength ?? 0;
    long responseBytes = 0;
    activeRequests.Add(1, new KeyValuePair<string, object?>("gateway.target", targetService));
    Activity.Current?.SetTag("gateway.target", targetService);
    Activity.Current?.SetTag("client.address", clientIp);

    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
    if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.Content = new StreamContent(context.Request.Body);
    }

    foreach (var header in context.Request.Headers)
    {
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }
        if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
        {
            request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    request.Headers.Host = targetUri.Authority;
    request.Headers.Remove("X-Forwarded-Proto");
    request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
    request.Headers.Remove("X-Forwarded-Host");
    request.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);

    try
    {
        using var response = await proxyClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");
        await using var responseStream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var bytesRead = await responseStream.ReadAsync(buffer, context.RequestAborted);
                if (bytesRead == 0) break;
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), context.RequestAborted);
                responseBytes += bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    catch (HttpRequestException)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync("The requested service is starting or unavailable.");
    }
    finally
    {
        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var statusGroup = $"{context.Response.StatusCode / 100}xx";
        var tags = new TagList
        {
            { "gateway.target", targetService },
            { "http.request.method", context.Request.Method },
            { "http.response.status_group", statusGroup },
        };
        requestCounter.Add(1, tags);
        activeRequests.Add(-1, new KeyValuePair<string, object?>("gateway.target", targetService));
        durationHistogram.Record(elapsedMs, tags);
        requestBytesHistogram.Record(requestBytes, tags);
        responseBytesHistogram.Record(responseBytes, tags);
        logger.LogInformation(
            "Gateway {Method} {Path} -> {TargetService} returned {StatusCode} in {ElapsedMs:F1} ms; client {ClientIp}; request {RequestBytes} B; response {ResponseBytes} B",
            context.Request.Method,
            context.Request.Path.Value,
            targetService,
            context.Response.StatusCode,
            elapsedMs,
            clientIp,
            requestBytes,
            responseBytes);
    }
});

app.Run();
