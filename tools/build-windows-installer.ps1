[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]$Bump = 'Patch'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot 'LocalProductManager.Web'
$tauriConfigPath = Join-Path $webRoot 'src-tauri\tauri.conf.json'
$cargoTomlPath = Join-Path $webRoot 'src-tauri\Cargo.toml'
$cargoLockPath = Join-Path $webRoot 'src-tauri\Cargo.lock'
$packageJsonPath = Join-Path $webRoot 'package.json'
$packageLockPath = Join-Path $webRoot 'package-lock.json'
$bundleDirectory = Join-Path $webRoot 'src-tauri\target\release\bundle\nsis'
$signingKeyPath = Join-Path $webRoot '_tools\tauri-updater\local-product-manager.key'
$updatesDirectory = Join-Path $repositoryRoot 'updates'

$requiredFiles = @(
    $tauriConfigPath,
    $cargoTomlPath,
    $cargoLockPath,
    $packageJsonPath,
    $packageLockPath
)
foreach ($path in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file was not found: $path"
    }
}

if (-not (Get-Command npm.cmd -ErrorAction SilentlyContinue)) {
    throw 'npm.cmd is not available in PATH.'
}
$cargoBinPath = Join-Path $env:USERPROFILE '.cargo\bin'
$cargoPath = Join-Path $cargoBinPath 'cargo.exe'
if (-not (Get-Command cargo.exe -ErrorAction SilentlyContinue) -and
    -not (Test-Path -LiteralPath $cargoPath)) {
    throw "cargo.exe is not available in PATH or at: $cargoPath"
}
if (-not (Test-Path -LiteralPath $signingKeyPath)) {
    throw @"
The Tauri updater signing key was not found:
$signingKeyPath

Restore this private key from the company backup before building an update.
Do not generate a replacement key because installed clients only trust the original key.
"@
}

$utf8NoBom = [Text.UTF8Encoding]::new($false)
$original = @{}
foreach ($path in $requiredFiles) {
    $original[$path] = [IO.File]::ReadAllText($path)
}

$tauriConfig = $original[$tauriConfigPath] | ConvertFrom-Json
$currentVersionText = [string]$tauriConfig.version
if ($currentVersionText -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
    throw "Current Tauri version is not valid semantic versioning: $currentVersionText"
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$patch = [int]$Matches[3]

if (-not $Version) {
    switch ($Bump) {
        'Major' {
            $major++
            $minor = 0
            $patch = 0
        }
        'Minor' {
            $minor++
            $patch = 0
        }
        'Patch' {
            $patch++
        }
    }
    $Version = "$major.$minor.$patch"
}

Write-Host ''
Write-Host 'LocalProductManager Windows installer' -ForegroundColor Cyan
Write-Host "Current version : $currentVersionText"
Write-Host "Build version   : $Version" -ForegroundColor Green
Write-Host ''

$tauriVersionPattern = [regex]::new('("version"\s*:\s*")\d+\.\d+\.\d+(")')
$cargoVersionPattern = [regex]::new('(?m)^(version\s*=\s*")\d+\.\d+\.\d+(")')
$npmVersionPattern = [regex]::new('("version"\s*:\s*")\d+\.\d+\.\d+(")')

$updatedTauriConfig = $tauriVersionPattern.Replace(
    $original[$tauriConfigPath],
    "`${1}$Version`${2}",
    1
)
$updatedCargoToml = $cargoVersionPattern.Replace(
    $original[$cargoTomlPath],
    "`${1}$Version`${2}",
    1
)
$updatedPackageJson = $npmVersionPattern.Replace(
    $original[$packageJsonPath],
    "`${1}$Version`${2}",
    1
)
$updatedPackageLock = $npmVersionPattern.Replace(
    $original[$packageLockPath],
    "`${1}$Version`${2}",
    2
)

try {
    [IO.File]::WriteAllText($tauriConfigPath, $updatedTauriConfig, $utf8NoBom)
    [IO.File]::WriteAllText($cargoTomlPath, $updatedCargoToml, $utf8NoBom)
    [IO.File]::WriteAllText($packageJsonPath, $updatedPackageJson, $utf8NoBom)
    [IO.File]::WriteAllText($packageLockPath, $updatedPackageLock, $utf8NoBom)

    Push-Location $webRoot
    try {
        $previousSigningKey = $env:TAURI_SIGNING_PRIVATE_KEY
        $previousCi = $env:CI
        $previousPath = $env:PATH
        if (-not (Get-Command cargo.exe -ErrorAction SilentlyContinue)) {
            $env:PATH = "$cargoBinPath;$env:PATH"
        }
        $env:TAURI_SIGNING_PRIVATE_KEY = $signingKeyPath
        $env:CI = 'true'
        & npm.cmd run desktop:build
        if ($LASTEXITCODE -ne 0) {
            throw "Tauri build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:TAURI_SIGNING_PRIVATE_KEY = $previousSigningKey
        $env:CI = $previousCi
        $env:PATH = $previousPath
        Pop-Location
    }
}
catch {
    foreach ($path in $requiredFiles) {
        [IO.File]::WriteAllText($path, $original[$path], $utf8NoBom)
    }
    Write-Host ''
    Write-Host 'Build failed. Version files were restored.' -ForegroundColor Red
    throw
}

$installer = Get-ChildItem -LiteralPath $bundleDirectory -Filter '*.exe' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $installer) {
    throw "Build completed, but no NSIS installer was found in: $bundleDirectory"
}

$signaturePath = "$($installer.FullName).sig"
if (-not (Test-Path -LiteralPath $signaturePath)) {
    throw "The signed updater artifact was not found: $signaturePath"
}

New-Item -ItemType Directory -Path $updatesDirectory -Force | Out-Null
$publishedInstallerPath = Join-Path $updatesDirectory 'LocalProductManager-setup.exe'
$publishedSignaturePath = "$publishedInstallerPath.sig"
$temporaryInstallerPath = "$publishedInstallerPath.tmp"
$temporarySignaturePath = "$publishedSignaturePath.tmp"
Copy-Item -LiteralPath $installer.FullName -Destination $temporaryInstallerPath -Force
Copy-Item -LiteralPath $signaturePath -Destination $temporarySignaturePath -Force
Move-Item -LiteralPath $temporaryInstallerPath -Destination $publishedInstallerPath -Force
Move-Item -LiteralPath $temporarySignaturePath -Destination $publishedSignaturePath -Force

$signature = [IO.File]::ReadAllText($signaturePath).Trim()
$manifest = [ordered]@{
    version = $Version
    notes = "LocalProductManager $Version"
    pub_date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    platforms = [ordered]@{
        'windows-x86_64' = [ordered]@{
            signature = $signature
            url = 'https://192.168.0.160/updates/LocalProductManager-setup.exe'
        }
    }
}
$manifestJson = $manifest | ConvertTo-Json -Depth 5
$manifestPath = Join-Path $updatesDirectory 'latest.json'
$temporaryManifestPath = "$manifestPath.tmp"
[IO.File]::WriteAllText(
    $temporaryManifestPath,
    $manifestJson,
    $utf8NoBom
)
Move-Item -LiteralPath $temporaryManifestPath -Destination $manifestPath -Force

Write-Host ''
Write-Host 'Installer created successfully.' -ForegroundColor Green
Write-Host "Version   : $Version"
Write-Host "Installer : $($installer.FullName)"
Write-Host "Published : $publishedInstallerPath"
Write-Host "Manifest  : $manifestPath"
Write-Host ''
Write-Host 'The running API publishes this update immediately. Commit the version-file changes.'
