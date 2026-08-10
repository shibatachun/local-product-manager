@echo off
setlocal
cd /d "%~dp0"

if "%~1"=="" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0tools\convert-1688-order.ps1"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0tools\convert-1688-order.ps1" -InputPath "%~f1"
)

if errorlevel 1 (
    echo.
    echo Conversion failed. Review the error above.
    pause
)

endlocal
