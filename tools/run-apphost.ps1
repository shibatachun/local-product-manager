$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'LocalProductManager.AppHost\LocalProductManager.AppHost.csproj'
$logRoot = Join-Path $env:LOCALAPPDATA 'LocalProductManager\Aspire'
$logPath = Join-Path $logRoot 'apphost.log'
$pidPath = Join-Path $logRoot 'apphost.pid'

New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Set-Location $repoRoot

Set-Content -LiteralPath $pidPath -Value $PID -Encoding Ascii
try {
    dotnet run --project $project --configuration Release --launch-profile http *>> $logPath
}
finally {
    Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
}
