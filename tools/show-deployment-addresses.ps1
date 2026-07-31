$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$deployment = Get-Content -LiteralPath (Join-Path $repositoryRoot 'config\deployment.json') -Raw | ConvertFrom-Json
$httpsPort = [int]$deployment.ports.https
$inventoryUrl = if ($httpsPort -eq 443) {
    "https://$($deployment.lanHost)"
}
else {
    "https://$($deployment.lanHost):$httpsPort"
}

Write-Host "Open: $inventoryUrl"
Write-Host "Dashboard: http://127.0.0.1:$($deployment.ports.dashboard)/"
