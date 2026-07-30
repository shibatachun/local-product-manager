$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$taskName = 'LocalProductManager Aspire AppHost'
$oldTaskName = 'LocalProductManager Monitor'
$iisData = 'C:\inetpub\LocalProductManager\LocalProductManager.Api\Data'
$sourceData = Join-Path $repoRoot 'LocalProductManager.Api\Data'
$sourceDatabase = Join-Path $sourceData 'local-product-manager.db'
$backupRoot = Join-Path $repoRoot '_backups'
$backupData = Join-Path $backupRoot ("pre-aspire-data-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$manager = Join-Path $PSScriptRoot 'aspire-manager.ps1'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run install_aspire.bat and approve the administrator prompt.'
}

Start-Transcript -LiteralPath (Join-Path $repoRoot 'aspire-install.log') -Append | Out-Null

Write-Host 'Stopping old services...' -ForegroundColor Cyan
Get-ScheduledTask -TaskName $oldTaskName -ErrorAction SilentlyContinue |
    Stop-ScheduledTask -ErrorAction SilentlyContinue
Get-ScheduledTask -TaskName $oldTaskName -ErrorAction SilentlyContinue |
    Unregister-ScheduledTask -Confirm:$false
Get-Process 'LocalProductManager.Monitor' -ErrorAction SilentlyContinue |
    Stop-Process -Force

Import-Module WebAdministration
if ((Test-Path 'IIS:\Sites\LocalProductManager') -and
    (Get-WebsiteState -Name 'LocalProductManager').Value -eq 'Started') {
    Stop-Website -Name 'LocalProductManager' -ErrorAction SilentlyContinue
}
if ((Test-Path 'IIS:\AppPools\LocalProductManager') -and
    (Get-WebAppPoolState -Name 'LocalProductManager').Value -eq 'Started') {
    Stop-WebAppPool -Name 'LocalProductManager' -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 2

Write-Host 'Backing up and migrating the latest IIS data...' -ForegroundColor Cyan
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
if (Test-Path $sourceData) {
    Copy-Item -LiteralPath $sourceData -Destination $backupData -Recurse
}
if ((Test-Path $iisData) -and -not (Test-Path -LiteralPath $sourceDatabase)) {
    New-Item -ItemType Directory -Path $sourceData -Force | Out-Null
    & robocopy.exe $iisData $sourceData /E /R:2 /W:1 | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "IIS data migration failed with robocopy exit code $LASTEXITCODE."
    }
}
elseif (Test-Path $iisData) {
    Write-Host 'Keeping the active Aspire database; legacy IIS data will not overwrite it.' -ForegroundColor Yellow
}

Write-Host 'Removing the LocalProductManager IIS site...' -ForegroundColor Cyan
if (Test-Path 'IIS:\Sites\LocalProductManager') {
    Remove-Website -Name 'LocalProductManager'
}
if (Test-Path 'IIS:\AppPools\LocalProductManager') {
    Remove-WebAppPool -Name 'LocalProductManager'
}

$oldMonitorInstall = Join-Path $env:ProgramData 'LocalProductManager\Monitor'
if (Test-Path $oldMonitorInstall) {
    Remove-Item -LiteralPath $oldMonitorInstall -Recurse -Force
}

Write-Host 'Registering Aspire Manager at user logon...' -ForegroundColor Cyan
Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue |
    Stop-ScheduledTask -ErrorAction SilentlyContinue
Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue |
    Unregister-ScheduledTask -Confirm:$false

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$manager`"" `
    -WorkingDirectory $repoRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $identity.Name `
    -LogonType Interactive -RunLevel Highest

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Settings $settings -Principal $taskPrincipal -Force | Out-Null

Write-Host 'Allowing LocalProductManager HTTPS on port 443...' -ForegroundColor Cyan
$httpsRuleName = 'LocalProductManager HTTPS 443'
if (Get-NetFirewallRule -DisplayName $httpsRuleName -ErrorAction SilentlyContinue) {
    Set-NetFirewallRule -DisplayName $httpsRuleName -Enabled True `
        -Direction Inbound -Action Allow -Profile Private
}
else {
    New-NetFirewallRule -DisplayName $httpsRuleName -Direction Inbound `
        -Action Allow -Protocol TCP -LocalPort 443 -Profile Private | Out-Null
}

Start-ScheduledTask -TaskName $taskName

Write-Host ''
Write-Host 'Aspire Manager installation completed.' -ForegroundColor Green
Write-Host 'Aspire is stopped by default. Use the manager window to start it.'
Write-Host "Data backup: $backupData"
Write-Host 'Dashboard: http://127.0.0.1:18889/'
Write-Host 'Inventory: https://192.168.0.160/'
