@echo off
setlocal
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
        "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

certutil.exe -addstore -f Root "%~dp0certificates\rootCA.pem"
if errorlevel 1 (
    echo.
    echo Failed to install the LocalProductManager internal certificate.
    pause
    exit /b 1
)

echo.
echo Internal certificate installed successfully.
echo Open: https://192.168.1.154
pause
