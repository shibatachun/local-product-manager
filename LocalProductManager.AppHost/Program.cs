using System.Text.Json;

var builder = DistributedApplication.CreateBuilder(args);

var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".."));
var deploymentPath = Path.Combine(repositoryRoot, "config", "deployment.json");
var deployment = JsonSerializer.Deserialize<DeploymentOptions>(
    File.ReadAllText(deploymentPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException($"Deployment configuration is invalid: {deploymentPath}");
var certificatePath = Path.GetFullPath(Path.Combine(
    repositoryRoot,
    deployment.Certificate.Path.Replace('/', Path.DirectorySeparatorChar)));
var frontendDistPath = Path.Combine(
    repositoryRoot,
    "LocalProductManager.Web",
    "dist",
    "LocalProductManager.Web",
    "browser");
var apiListenUrls = $"http://0.0.0.0:{deployment.Ports.Api}";
var apiLocalUrl = $"http://localhost:{deployment.Ports.Api}";
var apiProxyUrl = $"http://127.0.0.1:{deployment.Ports.Api}";
var databaseConnectionString = $"Host={deployment.Database.Host};Port={deployment.Database.Port};Database={deployment.Database.Name};Username={deployment.Database.Username};Password={deployment.Database.Password};Pooling=true;Maximum Pool Size=100";
var apiVersionUrl = $"{apiLocalUrl}/api/app/version";
var youtubeRedirectUrl = $"{apiLocalUrl}/api/youtube/oauth/callback";
var pinterestRedirectUrl = $"{apiLocalUrl}/api/pinterest/oauth/callback";
var metaRedirectUrl = $"{apiLocalUrl}/api/meta/oauth/callback";
var gatewayListenUrls =
    $"https://0.0.0.0:{deployment.Ports.Https};https://0.0.0.0:{deployment.Ports.LegacyHttps}";
var inventoryUrl = BuildHttpsUrl(deployment.LanHost, deployment.Ports.Https);

builder.AddProject<Projects.LocalProductManager_Api>(
        "inventory-api",
        launchProfileName: null)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
    .WithEnvironment("ASPNETCORE_URLS", apiListenUrls)
    .WithEnvironment("ConnectionStrings__DefaultConnection", databaseConnectionString)
    .WithEnvironment("YouTube__RedirectUri", youtubeRedirectUrl)
    .WithEnvironment("Pinterest__RedirectUri", pinterestRedirectUrl)
    .WithEnvironment("Meta__RedirectUri", metaRedirectUrl)
    .WithHttpEndpoint(port: deployment.Ports.Api, targetPort: deployment.Ports.Api, name: "api", isProxied: false)
    .WithExternalHttpEndpoints()
    .WithUrl(apiVersionUrl, "Inventory API");

builder.AddProject<Projects.LocalProductManager_Gateway>(
        "inventory-https-gateway",
        launchProfileName: null)
    .WithEnvironment("ASPNETCORE_URLS", gatewayListenUrls)
    .WithEnvironment("ASPNETCORE_Kestrel__Certificates__Default__Path", certificatePath)
    .WithEnvironment("ASPNETCORE_Kestrel__Certificates__Default__Password", deployment.Certificate.Password)
    .WithEnvironment("FrontendDistPath", frontendDistPath)
    .WithEnvironment("ApiBaseUrl", apiProxyUrl)
    .WithHttpsEndpoint(port: deployment.Ports.Https, targetPort: deployment.Ports.Https, name: "website", isProxied: false)
    .WithHttpsEndpoint(port: deployment.Ports.LegacyHttps, targetPort: deployment.Ports.LegacyHttps, name: "website-legacy", isProxied: false)
    .WithExternalHttpEndpoints()
    .WithUrl(inventoryUrl, "Inventory website");

builder.Build().Run();

static string BuildHttpsUrl(string host, int port) =>
    port == 443 ? $"https://{host}" : $"https://{host}:{port}";

internal sealed record DeploymentOptions(
    string LanHost,
    DeploymentPorts Ports,
    DeploymentCertificate Certificate,
    DeploymentDatabase Database);

internal sealed record DeploymentPorts(
    int Https,
    int LegacyHttps,
    int Api,
    int Dashboard,
    int DashboardOtlp,
    int ResourceService,
    int FrontendDevelopment);

internal sealed record DeploymentCertificate(string Path, string Password);

internal sealed record DeploymentDatabase(
    string Provider,
    string Host,
    int Port,
    string Name,
    string Username,
    string Password);
