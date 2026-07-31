$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$runner = Join-Path $PSScriptRoot 'run-apphost.ps1'
$logRoot = Join-Path $env:LOCALAPPDATA 'LocalProductManager\Aspire'
$logPath = Join-Path $logRoot 'apphost.log'
$buildLogPath = Join-Path $logRoot 'build.log'
$pidPath = Join-Path $logRoot 'apphost.pid'
$dashboardUrl = 'http://127.0.0.1:18889/'
$frontendProject = Join-Path $repoRoot 'LocalProductManager.Web'
$frontendDistRoot = Join-Path $frontendProject 'dist\LocalProductManager.Web'
$frontendDist = Join-Path $frontendDistRoot 'browser'
$appHostProject = Join-Path $repoRoot 'LocalProductManager.AppHost\LocalProductManager.AppHost.csproj'
$building = $false

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

function Build-Production {
    $stagingRoot = Join-Path $env:TEMP "LocalProductManager-build-$([Guid]::NewGuid().ToString('N'))"
    $stagedFrontendRoot = Join-Path $stagingRoot 'frontend'
    $stagedFrontend = Join-Path $stagedFrontendRoot 'browser'
    $stagedBackend = Join-Path $stagingRoot 'backend'
    $frontendBackup = Join-Path $stagingRoot 'frontend-previous'
    $wasRunning = $null -ne (Get-AppHostProcess)
    $frontendPromoted = $false

    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    Set-Content -LiteralPath $buildLogPath `
        -Value "Production build started at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" `
        -Encoding UTF8

    try {
        Push-Location $frontendProject
        try {
            & npm.cmd run build -- --output-path $stagedFrontendRoot *>> $buildLogPath
            if ($LASTEXITCODE -ne 0) {
                throw "Angular production build failed. The running service was not changed.`n`nSee: $buildLogPath"
            }
        }
        finally {
            Pop-Location
        }

        if (-not (Test-Path -LiteralPath (Join-Path $stagedFrontend 'index.html'))) {
            throw "Angular build completed without creating browser\index.html.`n`nSee: $buildLogPath"
        }

        & dotnet.exe build $appHostProject --configuration Release --no-restore `
            --output $stagedBackend *>> $buildLogPath
        if ($LASTEXITCODE -ne 0) {
            throw "Backend validation build failed. The running service was not changed.`n`nSee: $buildLogPath"
        }

        if ($wasRunning) {
            Stop-AppHost
        }

        New-Item -ItemType Directory -Path $frontendDistRoot -Force | Out-Null
        if (Test-Path -LiteralPath $frontendDist) {
            Move-Item -LiteralPath $frontendDist -Destination $frontendBackup
        }
        Move-Item -LiteralPath $stagedFrontend -Destination $frontendDist
        $frontendPromoted = $true

        & dotnet.exe build $appHostProject --configuration Release --no-restore *>> $buildLogPath
        if ($LASTEXITCODE -ne 0) {
            throw "Final backend build failed. The previous frontend will be restored.`n`nSee: $buildLogPath"
        }

        Start-AppHost
        Add-Content -LiteralPath $buildLogPath `
            -Value "Production build completed at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    }
    catch {
        if ($frontendPromoted -and (Test-Path -LiteralPath $frontendBackup)) {
            if (Test-Path -LiteralPath $frontendDist) {
                Remove-Item -LiteralPath $frontendDist -Recurse -Force
            }
            Move-Item -LiteralPath $frontendBackup -Destination $frontendDist
        }
        if ($wasRunning -and $null -eq (Get-AppHostProcess)) {
            Start-AppHost
        }
        throw
    }
    finally {
        $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
        $resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
        if ($resolvedStaging.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$form = [Windows.Forms.Form]::new()
$form.Text = 'Local Product Manager - Aspire'
$form.ClientSize = [Drawing.Size]::new(660, 245)
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
$buildButton = Add-Button 'Build && Restart' 349 125
$dashboardButton = Add-Button 'Dashboard' 483 105
$logButton = Add-Button 'Log' 597 48

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
    $buildButton.Enabled = -not $building
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
$buildButton.Add_Click({
    $building = $true
    $status.Text = 'Status: Building production version...'
    $status.ForeColor = [Drawing.Color]::DarkOrange
    $form.Refresh()
    try {
        Build-Production
        [Windows.Forms.MessageBox]::Show(
            "Production build succeeded and Aspire was restarted.`n`nBuild log: $buildLogPath",
            'Build complete',
            'OK',
            'Information') | Out-Null
    }
    catch {
        [Windows.Forms.MessageBox]::Show(
            $_.Exception.Message,
            'Build failed',
            'OK',
            'Error') | Out-Null
    }
    finally {
        $building = $false
        Update-Status
    }
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
