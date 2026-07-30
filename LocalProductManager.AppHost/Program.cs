var builder = DistributedApplication.CreateBuilder(args);

var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".."));
var certificatePath = Path.Combine(repositoryRoot, "certificates", "local-product-manager.pfx");
var angularDirectory = Path.Combine(repositoryRoot, "LocalProductManager.Web");

var angular = builder.AddExecutable(
        "angular-dev-server",
        "npm.cmd",
        angularDirectory,
        "run",
        "start")
    .WithIconName("Code")
    .WithHttpEndpoint(port: 4200, targetPort: 4200, name: "frontend", isProxied: false)
    .WithExternalHttpEndpoints()
    .WithUrl("http://localhost:4200", "Angular website");

builder.AddProject<Projects.LocalProductManager_Api>(
        "inventory-api",
        launchProfileName: null)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:5077")
    .WithHttpEndpoint(port: 5077, targetPort: 5077, name: "api", isProxied: false)
    .WithExternalHttpEndpoints()
    .WithUrl("http://localhost:5077/api/app/version", "Inventory API");

builder.AddProject<Projects.LocalProductManager_Gateway>(
        "inventory-https-gateway",
        launchProfileName: null)
    .WithEnvironment("ASPNETCORE_URLS", "https://0.0.0.0:443;https://0.0.0.0:7265")
    .WithEnvironment("ASPNETCORE_Kestrel__Certificates__Default__Path", certificatePath)
    .WithEnvironment("ASPNETCORE_Kestrel__Certificates__Default__Password", "changeit")
    .WithHttpsEndpoint(port: 443, targetPort: 443, name: "website", isProxied: false)
    .WithHttpsEndpoint(port: 7265, targetPort: 7265, name: "website-legacy", isProxied: false)
    .WithExternalHttpEndpoints()
    .WithUrl("https://192.168.0.160", "Inventory website")
    .WaitFor(angular);

builder.Build().Run();
