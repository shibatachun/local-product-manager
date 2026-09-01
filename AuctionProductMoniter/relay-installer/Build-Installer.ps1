$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "AuctionPrintRelay.Wpf\AuctionPrintRelay.Wpf.csproj"
$staging = Join-Path $PSScriptRoot "staging\app"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true --source "https://api.nuget.org/v3/index.json" `
  --artifacts-path (Join-Path $PSScriptRoot "artifacts") -o $staging -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Relay publish failed." }
$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
& $iscc (Join-Path $PSScriptRoot "AuctionPrintRelay.iss")
if ($LASTEXITCODE -ne 0) { throw "Relay installer build failed." }
