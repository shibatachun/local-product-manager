@echo off
setlocal
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 goto ELEVATE
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install-aspire.ps1"
pause
exit /b

:ELEVATE
echo Requesting administrator permission...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$script = Join-Path $PWD 'tools\install-aspire.ps1'; Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',('"""' + $script + '"""')"
exit /b
