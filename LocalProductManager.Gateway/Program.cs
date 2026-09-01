using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "inventory-https-gateway";
const string meterName = "LocalProductManager.Gateway";
const long maximumRequestBodySize = 250L * 1024 * 1024;

// Large spreadsheet imports pass through this gateway before reaching the API.
// Keep the gateway limit at least as large as the largest API import endpoint.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maximumRequestBodySize);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.AddOtlpExporter();
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
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
var frontendDistPath = builder.Configuration["FrontendDistPath"]
    ?? Path.GetFullPath(Path.Combine(
        builder.Environment.ContentRootPath,
        "..",
        "LocalProductManager.Web",
        "dist",
        "LocalProductManager.Web",
        "browser"));
var frontendIndexPath = Path.Combine(frontendDistPath, "index.html");
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://127.0.0.1:5077";
if (!File.Exists(frontendIndexPath))
{
    throw new FileNotFoundException(
        $"Angular production build was not found at '{frontendIndexPath}'. Run 'npm run build' first.",
        frontendIndexPath);
}

var frontendFiles = new PhysicalFileProvider(frontendDistPath);
var proxyClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("proxy");
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GatewayTraffic");
var meter = new Meter(meterName);
var requestCounter = meter.CreateCounter<long>("gateway.requests", "requests", "Requests received by the HTTPS gateway.");
var activeRequests = meter.CreateUpDownCounter<long>("gateway.active_requests", "requests", "Requests currently being processed.");
var durationHistogram = meter.CreateHistogram<double>("gateway.request.duration", "ms", "Gateway request duration.");
var requestBytesHistogram = meter.CreateHistogram<long>("gateway.request.bytes", "By", "Incoming request size.");
var responseBytesHistogram = meter.CreateHistogram<long>("gateway.response.bytes", "By", "Outgoing response size.");

app.UseResponseCompression();
app.Use(async (context, next) =>
{
    var startedAt = Stopwatch.GetTimestamp();
    var isApiRequest = IsApiRequest(context.Request.Path);
    var targetService = isApiRequest ? "inventory-api" : "angular-production";
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var requestBytes = context.Request.ContentLength ?? 0;

    activeRequests.Add(1, new KeyValuePair<string, object?>("gateway.target", targetService));
    Activity.Current?.SetTag("gateway.target", targetService);
    Activity.Current?.SetTag("client.address", clientIp);

    try
    {
        await next(context);
    }
    finally
    {
        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var responseBytes = context.Response.ContentLength ?? 0;
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

app.MapWhen(context => IsApiRequest(context.Request.Path), api =>
{
    api.Run(async context =>
    {
        var targetUri = new Uri(
            $"{apiBaseUrl}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}");
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
        request.Headers.Remove("X-Forwarded-For");
        var clientAddress = context.Connection.RemoteIpAddress;
        if (clientAddress is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientAddress.ToString());
        }

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
    });
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = frontendFiles,
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = frontendFiles,
    OnPrepareResponse = context =>
    {
        if (context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            || context.File.Name.Equals("ngsw.json", StringComparison.OrdinalIgnoreCase)
            || context.File.Name.Equals("ngsw-worker.js", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
            return;
        }

        var extension = Path.GetExtension(context.File.Name);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(context.File.Name);
        var hashSeparator = nameWithoutExtension.LastIndexOf('-');
        var hasContentHash = hashSeparator >= 0
            && nameWithoutExtension.Length - hashSeparator - 1 >= 8
            && (extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".css", StringComparison.OrdinalIgnoreCase));
        context.Context.Response.Headers[HeaderNames.CacheControl] = hasContentHash
            ? "public, max-age=31536000, immutable"
            : "public, max-age=3600";
    },
});
app.MapFallback(async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
    await context.Response.SendFileAsync(frontendIndexPath, context.RequestAborted);
});

app.Run();

static bool IsApiRequest(PathString path) =>
    path.StartsWithSegments("/api")
    || path.StartsWithSegments("/health")
    || path.StartsWithSegments("/updates");
