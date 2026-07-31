@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install-postgres-daily-backup.ps1"
echo.
pause
