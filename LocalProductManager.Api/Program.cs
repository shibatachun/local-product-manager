using LocalProductManager.Api.Data;
using LocalProductManager.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.AddOtlpExporter();
});

var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
var usePostgres = databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
    || databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=Data/local-product-manager.db";
var connectionString = usePostgres
    ? configuredConnectionString
    : AddSqliteWriteSettings(ResolveSqliteConnectionString(
        configuredConnectionString,
        builder.Environment.ContentRootPath));
var dbPath = usePostgres ? null : GetSqlitePath(connectionString);
if (!usePostgres && dbPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (usePostgres)
        options.UseNpgsql(connectionString, postgres => postgres.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    else
        options.UseSqlite(connectionString, sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
});
builder.Services.AddHealthChecks();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("inventory-api"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<InventoryWriteService>();
builder.Services.AddScoped<ProductExportService>();
builder.Services.AddScoped<AmazonAttributionImportService>();
builder.Services.AddScoped<AmazonBusinessReportImportService>();
builder.Services.AddScoped<BulkAttributionImportService>();
builder.Services.AddScoped<AttributionFolderImportService>();
builder.Services.AddScoped<PinterestReportImportService>();
builder.Services.AddScoped<InstagramReportImportService>();
builder.Services.AddScoped<FacebookReportImportService>();
builder.Services.AddScoped<TikTokReportImportService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SocialDataMaintenanceService>();
builder.Services.AddScoped<LiveStreamBidProductImportService>();
builder.Services.AddScoped<ProductImageFolderService>();
builder.Services.AddHttpClient<YouTubeOAuthService>();
builder.Services.AddHttpClient<PinterestOAuthService>();
builder.Services.AddHttpClient<MetaOAuthService>();
builder.Services.AddHttpClient<SocialAiAnalysisService>();
builder.Services.AddHttpClient<ExchangeRateService>();
builder.Services.AddHostedService<YouTubeDailyMetricsHostedService>();
builder.Services.AddHostedService<MetaDailyMetricsHostedService>();
builder.Services.AddHostedService<SocialAiAnalysisHostedService>();
builder.Services.AddHttpClient<AmazonImageLookupService>();
builder.Services.AddHttpClient<RemoteImageProxyService>();
builder.Services.AddSingleton<ImageMatcherService>();
builder.Services.AddSingleton<ImageFileService>();
builder.Services.AddSingleton<ImageThumbnailService>();
builder.Services.AddSingleton<SocialFetchLogService>();
builder.Services.AddSingleton<StoreLinkService>();
builder.Services.AddSingleton<VideoTrendLinkParser>();
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LanFrontend", policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddOpenApi();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedHost |
                               ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Add(System.Net.IPAddress.Loopback);
    options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
});

var app = builder.Build();
const string appVersion = "2026-06-18-auth-permissions-v1";
var updatesRoot = Path.GetFullPath(Path.Combine(
    app.Environment.ContentRootPath,
    "..",
    "updates"));
Directory.CreateDirectory(updatesRoot);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    if (db.Database.IsSqlite())
    {
        DatabaseSchemaService.Upgrade(db);
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
        db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=10000");
    }
    await ProductInventoryCategorySchema.UpgradeAsync(db);
    await ProductExtendedDataSchema.UpgradeAsync(db);
    await LiveStreamBidProductSchema.UpgradeAsync(db);
    await scope.ServiceProvider.GetRequiredService<ProductImageFolderService>()
        .OrganizeExistingProductsAsync();
    await scope.ServiceProvider.GetRequiredService<AuthService>().SeedAdminAsync();
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("LanFrontend");
app.MapGet("/api/app/version", () =>
{
    return Results.Ok(new
    {
        version = appVersion,
        service = "inventory-api"
    });
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(updatesRoot),
    RequestPath = "/updates",
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Context.Response.Headers.Pragma = "no-cache";
        context.Context.Response.Headers.Expires = "0";
    }
});
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    AllowCachingResponses = false
});

app.Run();

static string? GetSqlitePath(string connectionString)
{
    const string prefix = "Data Source=";
    var part = connectionString
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    if (part is null)
    {
        return null;
    }

    var path = part[prefix.Length..].Trim();
    return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
}

static string ResolveSqliteConnectionString(string connectionString, string contentRootPath)
{
    const string prefix = "Data Source=";
    var parts = connectionString
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
    var dataSourceIndex = parts.FindIndex(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    if (dataSourceIndex < 0)
    {
        return connectionString;
    }

    var path = parts[dataSourceIndex][prefix.Length..].Trim();
    if (!Path.IsPathRooted(path))
    {
        path = Path.GetFullPath(Path.Combine(contentRootPath, path));
        parts[dataSourceIndex] = $"{prefix}{path}";
    }

    return string.Join(';', parts);
}

static string AddSqliteWriteSettings(string connectionString)
{
    var parts = connectionString
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    if (!parts.Any(value => value.StartsWith("Default Timeout=", StringComparison.OrdinalIgnoreCase)))
    {
        parts.Add("Default Timeout=10");
    }

    if (!parts.Any(value => value.StartsWith("Pooling=", StringComparison.OrdinalIgnoreCase)))
    {
        parts.Add("Pooling=True");
    }

    return string.Join(';', parts);
}

public partial class Program;
