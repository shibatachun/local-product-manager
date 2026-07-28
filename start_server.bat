@echo off
setlocal EnableExtensions EnableDelayedExpansion
title LocalProductManager Server Manager

set "ROOT=%~dp0"
set "API_DIR=%ROOT%LocalProductManager.Api"
set "WEB_DIR=%ROOT%LocalProductManager.Web"
set "HTTPS_CERT=%ROOT%certificates\local-product-manager.pfx"
set "HTTPS_PORT=7265"
set "LAN_IP=192.168.0.160"

set "RUN_DIR=%TEMP%\LocalProductManagerLauncher"
set "API_RUNNER=%RUN_DIR%\run-api.cmd"
set "WEB_RUNNER=%RUN_DIR%\run-web.cmd"
set "API_LOG=%RUN_DIR%\api.log"
set "WEB_LOG=%RUN_DIR%\web.log"
set "API_PID_FILE=%RUN_DIR%\api.pid"
set "WEB_PID_FILE=%RUN_DIR%\web.pid"

if not exist "%API_DIR%" (
    echo ERROR: Cannot find API folder:
    echo %API_DIR%
    pause
    exit /b 1
)

if not exist "%WEB_DIR%" (
    echo ERROR: Cannot find Web folder:
    echo %WEB_DIR%
    pause
    exit /b 1
)

if not exist "%HTTPS_CERT%" (
    echo ERROR: Cannot find HTTPS certificate:
    echo %HTTPS_CERT%
    echo Run the mkcert setup before starting the server.
    pause
    exit /b 1
)

if not exist "%RUN_DIR%" mkdir "%RUN_DIR%"

REM Clean up service trees left behind if an earlier manager window was closed.
call :STOP_SAVED_PROCESS "%API_PID_FILE%" "%API_RUNNER%"
call :STOP_SAVED_PROCESS "%WEB_PID_FILE%" "%WEB_RUNNER%"

del /q "%API_LOG%" "%WEB_LOG%" 2>nul

REM 创建 API 启动脚本
> "%API_RUNNER%" (
    echo @echo off
    echo cd /d "%API_DIR%"
    echo set "ASPNETCORE_URLS=https://0.0.0.0:%HTTPS_PORT%"
    echo set "ASPNETCORE_Kestrel__Certificates__Default__Path=%HTTPS_CERT%"
    echo set "ASPNETCORE_Kestrel__Certificates__Default__Password=changeit"
    echo dotnet run --no-launch-profile ^> "%API_LOG%" 2^>^&1
)

REM 创建 Web 启动脚本
> "%WEB_RUNNER%" (
    echo @echo off
    echo cd /d "%WEB_DIR%"
    echo call npm run build ^> "%WEB_LOG%" 2^>^&1
    echo if errorlevel 1 exit /b 1
    echo call npm start ^>^> "%WEB_LOG%" 2^>^&1
)

cls
echo ==========================================
echo   LocalProductManager Server Manager
echo ==========================================
echo.
echo Starting API and Web servers...
echo.

REM 后台启动 API，并记录父进程 PID
for /f %%P in ('
    powershell -NoProfile -Command ^
    "$p = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', ('"""' + $env:API_RUNNER + '"""') -WindowStyle Hidden -PassThru; $p.Id"
') do set "API_PID=%%P"
> "%API_PID_FILE%" echo !API_PID!

REM 后台启动 Web，并记录父进程 PID
for /f %%P in ('
    powershell -NoProfile -Command ^
    "$p = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', ('"""' + $env:WEB_RUNNER + '"""') -WindowStyle Hidden -PassThru; $p.Id"
') do set "WEB_PID=%%P"
> "%WEB_PID_FILE%" echo !WEB_PID!

echo API PID: %API_PID%
echo Web PID: %WEB_PID%
echo HTTPS URL: https://%LAN_IP%:%HTTPS_PORT%
echo.
echo Waiting for startup...

REM 等待 build 和服务器启动
timeout /t 8 /nobreak >nul

:MENU
call :UPDATE_STATUS

cls
echo ==========================================
echo   LocalProductManager Server Manager
echo ==========================================
echo.
echo API Server : !API_STATUS!
echo Web Server : !WEB_STATUS!
echo.
echo ------------------------------------------
echo [1] Refresh status
echo [2] View API log
echo [3] View Web log
echo [4] Stop both servers and exit
echo ------------------------------------------
echo.

set /p "OPTION=Choose an option: "

if "%OPTION%"=="1" goto MENU
if "%OPTION%"=="2" goto VIEW_API_LOG
if "%OPTION%"=="3" goto VIEW_WEB_LOG
if "%OPTION%"=="4" goto STOP_SERVERS

goto MENU


:UPDATE_STATUS
set "API_STATUS=STOPPED"
set "WEB_STATUS=STOPPED"

tasklist /FI "PID eq %API_PID%" 2>nul | findstr /R /C:"[ ]%API_PID%[ ]" >nul
if not errorlevel 1 set "API_STATUS=RUNNING"

tasklist /FI "PID eq %WEB_PID%" 2>nul | findstr /R /C:"[ ]%WEB_PID%[ ]" >nul
if not errorlevel 1 set "WEB_STATUS=RUNNING"

exit /b


:VIEW_API_LOG
cls
echo ================= API LOG =================
echo.

if exist "%API_LOG%" (
    type "%API_LOG%"
) else (
    echo API log has not been created yet.
)

echo.
echo ===========================================
pause
goto MENU


:VIEW_WEB_LOG
cls
echo ================= WEB LOG =================
echo.

if exist "%WEB_LOG%" (
    type "%WEB_LOG%"
) else (
    echo Web log has not been created yet.
)

echo.
echo ===========================================
pause
goto MENU


:STOP_SERVERS
cls
echo Stopping API server...

if defined API_PID (
    taskkill /PID %API_PID% /T /F >nul 2>&1
)

echo Stopping Web server...

if defined WEB_PID (
    taskkill /PID %WEB_PID% /T /F >nul 2>&1
)

echo.
echo Both servers have been stopped.
echo.

del /q "%API_RUNNER%" "%WEB_RUNNER%" 2>nul
del /q "%API_PID_FILE%" "%WEB_PID_FILE%" 2>nul

pause
exit /b


:STOP_SAVED_PROCESS
set "SAVED_PID="
if not exist "%~1" exit /b
set /p SAVED_PID=<"%~1"
echo(!SAVED_PID!| findstr /R "^[0-9][0-9]*$" >nul || (
    del /q "%~1" 2>nul
    exit /b
)
powershell -NoProfile -Command ^
    "$p = Get-CimInstance Win32_Process -Filter ('ProcessId=' + !SAVED_PID!) -ErrorAction SilentlyContinue; if ($p -and $p.CommandLine -like '*%~2*') { Stop-Process -Id !SAVED_PID! -Force -ErrorAction SilentlyContinue }"
del /q "%~1" 2>nul
exit /b
