[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    $arguments = @(
        '-NoProfile'
        '-ExecutionPolicy'
        'Bypass'
        '-File'
        "`"$PSCommandPath`""
    )
    if ($Force) {
        $arguments += '-Force'
    }

    $elevated = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $elevated.ExitCode
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$wacsPath = Join-Path $repositoryRoot '_tools\win-acme\wacs.exe'
$challengePath = Join-Path $repositoryRoot '_tools\win-acme\dns-challenge.json'
$certificatePath = Join-Path $repositoryRoot 'certificates\inventory-jjgqaz-com.pfx'
$backupDirectory = Join-Path $repositoryRoot '_backups\certificates'
$friendlyName = 'LocalProductManager-inventory-jjgqaz-com'
$pfxPassword = 'changeit'

if (-not (Test-Path -LiteralPath $wacsPath)) {
    throw "win-acme was not found: $wacsPath"
}
if (-not (Test-Path -LiteralPath $certificatePath)) {
    throw "Current certificate was not found: $certificatePath"
}

$oldCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $certificatePath,
    $pfxPassword
)
$remainingDays = [Math]::Floor(($oldCertificate.NotAfter - (Get-Date)).TotalDays)

Write-Host ''
Write-Host 'Inventory HTTPS certificate renewal' -ForegroundColor Cyan
Write-Host "Current expiry : $($oldCertificate.NotAfter)"
Write-Host "Days remaining : $remainingDays"
Write-Host ''

if (-not $Force -and $remainingDays -gt 35) {
    $recommendedDate = $oldCertificate.NotAfter.AddDays(-35).ToString('yyyy-MM-dd')
    Write-Host 'The certificate is not close to expiry, so no renewal was attempted.' -ForegroundColor Yellow
    Write-Host "Run renew_certificate.bat normally on or after $recommendedDate."
    Write-Host 'For a deliberate test, run: renew_certificate.bat -Force'
    exit 0
}

New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = Join-Path $backupDirectory "inventory-jjgqaz-com-$timestamp.pfx"
Copy-Item -LiteralPath $certificatePath -Destination $backupPath

if (Test-Path -LiteralPath $challengePath) {
    Remove-Item -LiteralPath $challengePath -Force
}

$logPath = Join-Path $env:TEMP "inventory-certificate-renewal-$timestamp.log"
$errorLogPath = Join-Path $env:TEMP "inventory-certificate-renewal-$timestamp-error.log"
$wacsArguments = @(
    '--renew'
    '--friendlyname'
    $friendlyName
    '--verbose'
)
if ($Force) {
    $wacsArguments += '--force'
}

Write-Host 'Requesting a certificate from Let''s Encrypt...'
$process = Start-Process `
    -FilePath $wacsPath `
    -ArgumentList $wacsArguments `
    -RedirectStandardOutput $logPath `
    -RedirectStandardError $errorLogPath `
    -PassThru

$challengeDisplayed = $false
while (-not $process.HasExited) {
    if (-not $challengeDisplayed -and (Test-Path -LiteralPath $challengePath)) {
        try {
            $challenge = Get-Content -LiteralPath $challengePath -Raw | ConvertFrom-Json
            if ($challenge.token) {
                $challengeDisplayed = $true
                try {
                    Set-Clipboard -Value $challenge.token
                }
                catch {
                    # Clipboard support is convenient but not required.
                }

                Write-Host ''
                Write-Host 'ACTION REQUIRED IN WHOIS DNS' -ForegroundColor Yellow
                Write-Host 'Type  : TXT'
                Write-Host 'Name  : _acme-challenge.inventory'
                Write-Host "Value : $($challenge.token)" -ForegroundColor Green
                Write-Host ''
                Write-Host 'The TXT value has also been copied to the clipboard.'
                Write-Host 'Replace the old TXT value in Whois, then leave this window open.'
                Write-Host 'The script checks public DNS every 10 seconds for up to 30 minutes.'
                Write-Host ''
            }
        }
        catch {
            # The hook may still be writing the JSON file; retry on the next pass.
        }
    }

    Start-Sleep -Seconds 2
    $process.Refresh()
}

if ($process.ExitCode -ne 0) {
    Write-Host 'Certificate renewal failed. The existing certificate was kept.' -ForegroundColor Red
    Write-Host "Log: $logPath"
    if ((Test-Path $errorLogPath) -and (Get-Item $errorLogPath).Length -gt 0) {
        Get-Content -LiteralPath $errorLogPath | Select-Object -Last 30
    }
    exit $process.ExitCode
}

$newCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $certificatePath,
    $pfxPassword
)

if ($newCertificate.Thumbprint -eq $oldCertificate.Thumbprint) {
    Write-Host 'win-acme completed, but the certificate was not replaced.' -ForegroundColor Yellow
    Write-Host 'It may not have been due yet. Use -Force only when intentionally testing.'
    exit 0
}

Write-Host ''
Write-Host 'Certificate renewed successfully.' -ForegroundColor Green
Write-Host "New expiry : $($newCertificate.NotAfter)"
Write-Host "Backup     : $backupPath"
Write-Host ''
Write-Host 'Restart LocalProductManager Server Manager so Kestrel loads the new certificate.' -ForegroundColor Yellow
