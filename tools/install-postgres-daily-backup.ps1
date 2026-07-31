param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$taskName = 'LocalProductManager PostgreSQL Daily Backup'
$backupScript = Join-Path $PSScriptRoot 'postgres-backup.ps1'

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed scheduled task: $taskName"
    exit
}

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument (
    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$backupScript`" -Quiet -RetentionDays 30")
$trigger = New-ScheduledTaskTrigger -Daily -At '00:00'
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2)
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal -Force | Out-Null
Write-Host "Installed scheduled task: $taskName"
Write-Host 'Schedule: daily at 00:00; missed runs start when the computer becomes available.'
