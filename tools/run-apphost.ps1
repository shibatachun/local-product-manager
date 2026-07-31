$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'LocalProductManager.AppHost\LocalProductManager.AppHost.csproj'
$deployment = Get-Content -LiteralPath (Join-Path $repoRoot 'config\deployment.json') -Raw | ConvertFrom-Json
$logRoot = Join-Path $env:LOCALAPPDATA 'LocalProductManager\Aspire'
$logPath = Join-Path $logRoot 'apphost.log'
$pidPath = Join-Path $logRoot 'apphost.pid'

New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Set-Location $repoRoot

Set-Content -LiteralPath $pidPath -Value $PID -Encoding Ascii
try {
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$($deployment.ports.dashboard)"
    $env:ASPIRE_ALLOW_UNSECURED_TRANSPORT = 'true'
    $env:ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS = 'true'
    $env:ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL = "http://127.0.0.1:$($deployment.ports.dashboardOtlp)"
    $env:ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL = "http://127.0.0.1:$($deployment.ports.resourceService)"
    dotnet run --no-build --project $project --configuration Release --no-launch-profile *>> $logPath
}
finally {
    Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
}
