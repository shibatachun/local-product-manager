$repoRoot = Split-Path -Parent $PSScriptRoot
$manager = Join-Path $repoRoot 'manage_aspire.bat'
Start-Process -FilePath 'cmd.exe' -WorkingDirectory $repoRoot -ArgumentList '/c', "`"$manager`""
