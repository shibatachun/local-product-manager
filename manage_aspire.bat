@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Administrator privileges are required to manage the Aspire service.
  echo Windows will now ask for administrator approval.
  mshta "vbscript:CreateObject(""Shell.Application"").ShellExecute(""%~f0"","""",""%~dp0"",""runas"",1)(window.close)"
  if errorlevel 1 goto administrator_required
  exit /b
)
for /f "tokens=1,* delims==" %%A in ('node tools\deployment-env.mjs') do set "%%A=%%B"
set "APPHOST=LocalProductManager.AppHost\LocalProductManager.AppHost.csproj"
set "LOGROOT=%LOCALAPPDATA%\LocalProductManager\Aspire"
if not exist "%LOGROOT%" mkdir "%LOGROOT%"
set "LOGFILE=%LOGROOT%\apphost-%RANDOM%-%RANDOM%.log"

goto menu

:administrator_required
echo.
echo This script must be run as administrator.
echo Right-click the BAT file and select "Run as administrator", then try again.
echo.
pause
exit /b 1

:menu
cls
echo ==============================================================
echo  Local Product Manager - Aspire
echo ==============================================================
call :findpid
if defined ASPIRE_PID (
  echo  Status    : RUNNING ^(PID !ASPIRE_PID!^)
) else (
  echo  Status    : STOPPED
)
echo  Website   : %INVENTORY_URL%
echo  Dashboard : http://127.0.0.1:%DASHBOARD_PORT%/
echo.
echo  [1] Start Aspire
echo  [2] Stop Aspire
echo  [3] Restart Aspire
echo  [4] Build and Restart
echo  [5] Open Dashboard
echo  [6] Show Log
echo  [7] Refresh
echo  [0] Exit
echo.
set /p "CHOICE=Select: "
if "%CHOICE%"=="1" call :start
if "%CHOICE%"=="2" call :stop
if "%CHOICE%"=="3" call :restart
if "%CHOICE%"=="4" call :build
if "%CHOICE%"=="5" start "" "http://127.0.0.1:%DASHBOARD_PORT%/"
if "%CHOICE%"=="6" call :log
if "%CHOICE%"=="7" goto menu
if "%CHOICE%"=="0" exit /b 0
goto menu

:findpid
set "ASPIRE_PID="
for /f "tokens=5" %%P in ('netstat -ano ^| findstr /R /C:":%DASHBOARD_PORT% .*LISTENING"') do if not defined ASPIRE_PID set "ASPIRE_PID=%%P"
exit /b

:start
call :findpid
if defined ASPIRE_PID (
  echo Aspire is already running.
  pause
  exit /b
)
echo Starting Aspire...
echo Session log: %LOGFILE%
set "DOTNET_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://127.0.0.1:%DASHBOARD_PORT%"
set "ASPIRE_ALLOW_UNSECURED_TRANSPORT=true"
set "ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true"
set "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://127.0.0.1:%DASHBOARD_OTLP_PORT%"
set "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=http://127.0.0.1:%RESOURCE_SERVICE_PORT%"
start "" /b cmd.exe /c "cd /d ""%~dp0"" && dotnet run --no-build --project ""%APPHOST%"" --configuration Release --no-launch-profile >> ""%LOGFILE%"" 2>&1"
for /l %%I in (1,1,30) do (
  timeout /t 1 /nobreak >nul
  call :findpid
  if defined ASPIRE_PID (
    echo Aspire started. PID !ASPIRE_PID!
    pause
    exit /b
  )
)
echo Aspire did not start. Check the log.
call :logtail
pause
exit /b 1

:stop
call :findpid
if not defined ASPIRE_PID (
  echo Aspire is already stopped.
) else (
  echo Stopping Aspire PID !ASPIRE_PID!...
  taskkill /PID !ASPIRE_PID! /T /F >nul 2>&1
  timeout /t 2 /nobreak >nul
  echo Aspire stopped.
)
pause
exit /b

:restart
call :findpid
if defined ASPIRE_PID taskkill /PID !ASPIRE_PID! /T /F >nul 2>&1
timeout /t 2 /nobreak >nul
call :start
exit /b

:build
echo Building Angular production files...
pushd LocalProductManager.Web
call npm.cmd run build
if errorlevel 1 (popd & echo Angular build failed. & pause & exit /b 1)
popd
echo Building Aspire and API...
dotnet build "%APPHOST%" --configuration Release
if errorlevel 1 (echo .NET build failed. & pause & exit /b 1)
call :findpid
if defined ASPIRE_PID taskkill /PID !ASPIRE_PID! /T /F >nul 2>&1
timeout /t 2 /nobreak >nul
call :start
exit /b

:log
if exist "%LOGFILE%" (
  type "%LOGFILE%"
) else (
  echo No log file exists yet.
)
pause
exit /b

:logtail
if exist "%LOGFILE%" more +0 "%LOGFILE%"
exit /b
