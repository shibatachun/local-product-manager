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

var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=local_product_manager;Username=postgres";

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(configuredConnectionString,
            postgres => postgres.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
}
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
builder.Services.AddScoped<WhatnotShowCostImportService>();
builder.Services.AddScoped<DomesticProductImportService>();
builder.Services.AddSingleton<ThirdPartyLabelPrintService>();
builder.Services.AddHttpClient();
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
builder.Services.AddSingleton<BroochFinderService>();
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
    if (!app.Environment.IsEnvironment("Testing"))
    {
        await WarehouseSchema.UpgradeAsync(db);
        await InventoryChannelSchema.UpgradeAsync(db);
        await ProductInventoryCategorySchema.UpgradeAsync(db);
        await ProductExtendedDataSchema.UpgradeAsync(db);
        await ProductListingPriceSchema.UpgradeAsync(db);
        await LiveStreamBidProductSchema.UpgradeAsync(db);
        await DomesticProductSchema.UpgradeAsync(db);
        await InventoryLabelSchema.UpgradeAsync(db);
    }
    else
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS product_category_sequences (
                "Category" varchar(16) PRIMARY KEY,
                "LastNumber" integer NOT NULL
            )
            """);
    }
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

public partial class Program;
