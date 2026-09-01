param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$installerDirectory = $PSScriptRoot
$projectDirectory = Split-Path $installerDirectory -Parent
$projectFile = Join-Path $projectDirectory "AuctionProductMonitor.Wpf\AuctionProductMonitor.Wpf.csproj"
$stagingDirectory = Join-Path $installerDirectory "staging\app"
$artifactsDirectory = Join-Path $installerDirectory "artifacts"
$scriptFile = Join-Path $installerDirectory "AuctionProductMonitor.iss"

if (Test-Path $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

dotnet publish $projectFile `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --source "https://api.nuget.org/v3/index.json" `
    --artifacts-path $artifactsDirectory `
    --output $stagingDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$compiler = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) {
    throw "Inno Setup 6 was not found. Install it, then run this script again."
}

& $compiler $scriptFile
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Host "Installer created in: $(Join-Path $installerDirectory 'output')"
