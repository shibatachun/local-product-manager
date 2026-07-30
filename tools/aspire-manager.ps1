$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$runner = Join-Path $PSScriptRoot 'run-apphost.ps1'
$logRoot = Join-Path $env:LOCALAPPDATA 'LocalProductManager\Aspire'
$logPath = Join-Path $logRoot 'apphost.log'
$pidPath = Join-Path $logRoot 'apphost.pid'
$dashboardUrl = 'http://127.0.0.1:18889/'

$createdNew = $false
$mutex = [Threading.Mutex]::new($true, 'LocalProductManager.AspireManager', [ref]$createdNew)
if (-not $createdNew) {
    [Windows.Forms.MessageBox]::Show(
        'Aspire Manager is already open.',
        'Local Product Manager',
        'OK',
        'Information') | Out-Null
    exit
}

function Get-AppHostProcess {
    if (-not (Test-Path -LiteralPath $pidPath)) {
        return $null
    }

    $savedPid = 0
    if (-not [int]::TryParse((Get-Content -LiteralPath $pidPath -Raw).Trim(), [ref]$savedPid)) {
        Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
        return $null
    }

    $process = Get-Process -Id $savedPid -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
    }
    return $process
}

function Start-AppHost {
    if ($null -ne (Get-AppHostProcess)) {
        return
    }

    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $launcher = Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden `
        -WorkingDirectory $repoRoot `
        -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$runner`"" `
        -PassThru

    Start-Sleep -Milliseconds 800
    if ($launcher.HasExited -and $null -eq (Get-AppHostProcess)) {
        $details = if (Test-Path -LiteralPath $logPath) {
            (Get-Content -LiteralPath $logPath -Tail 12) -join [Environment]::NewLine
        }
        else {
            'The background launcher exited before creating a log.'
        }
        throw "Aspire could not be started.`n`n$details"
    }
}

function Stop-AppHost {
    $process = Get-AppHostProcess
    if ($null -eq $process) {
        return
    }

    & taskkill.exe /PID $process.Id /T /F | Out-Null
    Remove-Item -LiteralPath $pidPath -Force -ErrorAction SilentlyContinue
}

$form = [Windows.Forms.Form]::new()
$form.Text = 'Local Product Manager - Aspire'
$form.ClientSize = [Drawing.Size]::new(520, 225)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false

$title = [Windows.Forms.Label]::new()
$title.Text = 'Aspire service'
$title.Font = [Drawing.Font]::new('Segoe UI', 16, [Drawing.FontStyle]::Bold)
$title.AutoSize = $true
$title.Location = [Drawing.Point]::new(24, 20)
$form.Controls.Add($title)

$status = [Windows.Forms.Label]::new()
$status.Font = [Drawing.Font]::new('Segoe UI', 11)
$status.AutoSize = $true
$status.Location = [Drawing.Point]::new(27, 65)
$form.Controls.Add($status)

$hint = [Windows.Forms.Label]::new()
$hint.Text = 'The service does not start automatically. Use the controls below.'
$hint.Font = [Drawing.Font]::new('Segoe UI', 9)
$hint.AutoSize = $true
$hint.ForeColor = [Drawing.Color]::DimGray
$hint.Location = [Drawing.Point]::new(28, 94)
$form.Controls.Add($hint)

function Add-Button([string]$text, [int]$x, [int]$width) {
    $button = [Windows.Forms.Button]::new()
    $button.Text = $text
    $button.Size = [Drawing.Size]::new($width, 34)
    $button.Location = [Drawing.Point]::new($x, 132)
    $form.Controls.Add($button)
    return $button
}

$startButton = Add-Button 'Start Aspire' 28 105
$stopButton = Add-Button 'Stop Aspire' 142 105
$restartButton = Add-Button 'Restart' 256 84
$dashboardButton = Add-Button 'Dashboard' 349 105
$logButton = Add-Button 'Log' 463 48

function Update-Status {
    $process = Get-AppHostProcess
    $running = $null -ne $process
    if ($running) {
        $status.Text = "Status: Running (PID $($process.Id))"
        $status.ForeColor = [Drawing.Color]::ForestGreen
    }
    else {
        $status.Text = 'Status: Stopped'
        $status.ForeColor = [Drawing.Color]::Firebrick
    }
    $startButton.Enabled = -not $running
    $stopButton.Enabled = $running
    $restartButton.Enabled = $running
    $dashboardButton.Enabled = $running
}

$startButton.Add_Click({
    try {
        Start-AppHost
    }
    catch {
        [Windows.Forms.MessageBox]::Show(
            $_.Exception.Message,
            'Aspire start failed',
            'OK',
            'Error') | Out-Null
    }
    Update-Status
})
$stopButton.Add_Click({
    try {
        Stop-AppHost
    }
    catch {
        [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Aspire stop failed', 'OK', 'Error') | Out-Null
    }
    Update-Status
})
$restartButton.Add_Click({
    try {
        Stop-AppHost
        Start-Sleep -Milliseconds 500
        Start-AppHost
    }
    catch {
        [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Aspire restart failed', 'OK', 'Error') | Out-Null
    }
    Update-Status
})
$dashboardButton.Add_Click({
    Start-Process $dashboardUrl
})
$logButton.Add_Click({
    if (Test-Path -LiteralPath $logPath) {
        Start-Process notepad.exe -ArgumentList "`"$logPath`""
    }
    else {
        [Windows.Forms.MessageBox]::Show('No Aspire log has been created yet.', 'Aspire Log') | Out-Null
    }
})

$timer = [Windows.Forms.Timer]::new()
$timer.Interval = 1500
$timer.Add_Tick({ Update-Status })
$timer.Start()

$form.Add_FormClosed({
    $timer.Stop()
    $mutex.ReleaseMutex()
    $mutex.Dispose()
})

Update-Status
[void]$form.ShowDialog()
