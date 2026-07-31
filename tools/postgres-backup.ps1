param(
    [switch]$Quiet,
    [int]$RetentionDays = 30
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$deployment = Get-Content -LiteralPath (Join-Path $repoRoot 'config\deployment.json') -Raw | ConvertFrom-Json
$database = $deployment.database
$postgresRoot = Get-ChildItem 'C:\Program Files\PostgreSQL' -Directory -ErrorAction Stop |
    Sort-Object { [version]"$($_.Name).0" } -Descending |
    Select-Object -First 1
$pgDump = Join-Path $postgresRoot.FullName 'bin\pg_dump.exe'
$pgRestore = Join-Path $postgresRoot.FullName 'bin\pg_restore.exe'
if (-not (Test-Path -LiteralPath $pgDump) -or -not (Test-Path -LiteralPath $pgRestore)) {
    throw 'PostgreSQL backup tools were not found.'
}

$backupRoot = Join-Path $env:LOCALAPPDATA 'LocalProductManager\PostgreSQLBackups'
$logRoot = Join-Path $env:LOCALAPPDATA 'LocalProductManager\PostgreSQL'
$logPath = Join-Path $logRoot 'backup.log'
New-Item -ItemType Directory -Path $backupRoot,$logRoot -Force | Out-Null

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$finalPath = Join-Path $backupRoot "$($database.name)-$stamp.dump"
$temporaryPath = "$finalPath.tmp"
$env:PGPASSWORD = [string]$database.password

try {
    & $pgDump -h $database.host -p $database.port -U $database.username -d $database.name `
        --format=custom --compress=9 --file=$temporaryPath
    if ($LASTEXITCODE -ne 0) { throw "pg_dump exited with code $LASTEXITCODE." }

    & $pgRestore --list $temporaryPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Backup verification failed.' }

    Move-Item -LiteralPath $temporaryPath -Destination $finalPath -Force
    $hash = (Get-FileHash -LiteralPath $finalPath -Algorithm SHA256).Hash
    $size = (Get-Item -LiteralPath $finalPath).Length
    $cutoff = (Get-Date).AddDays(-[Math]::Max(1, $RetentionDays))
    Get-ChildItem -LiteralPath $backupRoot -Filter "$($database.name)-*.dump" -File |
        Where-Object LastWriteTime -lt $cutoff |
        Remove-Item -Force

    $message = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') SUCCESS database=$($database.name) bytes=$size sha256=$hash file=$finalPath"
    Add-Content -LiteralPath $logPath -Value $message
    if (-not $Quiet) { Write-Host $message -ForegroundColor Green }
}
catch {
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    $message = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') FAILED database=$($database.name) error=$($_.Exception.Message)"
    Add-Content -LiteralPath $logPath -Value $message
    if (-not $Quiet) { Write-Error $message }
    exit 1
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
