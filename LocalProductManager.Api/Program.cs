using LocalProductManager.Api.Data;
using LocalProductManager.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=Data/local-product-manager.db";
var connectionString = ResolveSqliteConnectionString(
    configuredConnectionString,
    builder.Environment.ContentRootPath);
var dbPath = GetSqlitePath(connectionString);
if (dbPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString, sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<ProductExportService>();
builder.Services.AddScoped<AmazonAttributionImportService>();
builder.Services.AddScoped<AmazonBusinessReportImportService>();
builder.Services.AddScoped<BulkAttributionImportService>();
builder.Services.AddScoped<AttributionFolderImportService>();
builder.Services.AddScoped<PinterestReportImportService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<YouTubeOAuthService>();
builder.Services.AddHttpClient<PinterestOAuthService>();
builder.Services.AddHostedService<YouTubeDailyMetricsHostedService>();
builder.Services.AddHttpClient<AmazonImageLookupService>();
builder.Services.AddHttpClient<RemoteImageProxyService>();
builder.Services.AddSingleton<ImageMatcherService>();
builder.Services.AddSingleton<ImageFileService>();
builder.Services.AddSingleton<SocialFetchLogService>();
builder.Services.AddSingleton<StoreLinkService>();
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

var app = builder.Build();
const string appVersion = "2026-06-18-auth-permissions-v1";
var angularRoot = Path.GetFullPath(Path.Combine(
    app.Environment.ContentRootPath,
    "..",
    "LocalProductManager.Web",
    "dist",
    "LocalProductManager.Web",
    "browser"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    DatabaseSchemaService.Upgrade(db);
    await scope.ServiceProvider.GetRequiredService<AuthService>().SeedAdminAsync();
    await DemoSocialDataSeeder.SeedAsync(db);
}

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
    var indexPath = Path.Combine(angularRoot, "index.html");
    var mainFiles = Directory.Exists(angularRoot)
        ? Directory.GetFiles(angularRoot, "main-*.js")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new
            {
                file.Name,
                lastWriteTime = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
            })
            .ToList()
        : [];

    return Results.Ok(new
    {
        version = appVersion,
        contentRoot = app.Environment.ContentRootPath,
        angularRoot,
        angularRootExists = Directory.Exists(angularRoot),
        indexExists = File.Exists(indexPath),
        indexLastWriteTime = File.Exists(indexPath)
            ? File.GetLastWriteTime(indexPath).ToString("yyyy-MM-dd HH:mm:ss")
            : null,
        mainFiles
    });
});
if (Directory.Exists(angularRoot))
{
    var angularFiles = new PhysicalFileProvider(angularRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = angularFiles });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = angularFiles,
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Context.Response.Headers.Pragma = "no-cache";
            context.Context.Response.Headers.Expires = "0";
        }
    });
}
app.MapControllers();
if (Directory.Exists(angularRoot))
{
    var indexPath = Path.Combine(angularRoot, "index.html");
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
        await context.Response.SendFileAsync(indexPath);
    });
}

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
