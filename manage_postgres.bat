@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Administrator privileges are required to manage PostgreSQL.
  echo Windows will now ask for administrator approval.
  mshta "vbscript:CreateObject(""Shell.Application"").ShellExecute(""%~f0"","""",""%~dp0"",""runas"",1)(window.close)"
  if errorlevel 1 goto administrator_required
  exit /b
)
for /f "tokens=1,* delims==" %%A in ('node tools\deployment-env.mjs') do set "%%A=%%B"
for /f "delims=" %%D in ('dir /b /ad "C:\Program Files\PostgreSQL" 2^>nul') do set "PGVERSION=%%D"
set "PGBIN=C:\Program Files\PostgreSQL\%PGVERSION%\bin"
set "PG_SERVICE_NAME=postgresql-x64-%PGVERSION%"
set "BACKUPROOT=%LOCALAPPDATA%\LocalProductManager\PostgreSQLBackups"
if not exist "%BACKUPROOT%" mkdir "%BACKUPROOT%"

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
echo  Local Product Manager - PostgreSQL
echo ==============================================================
set "PGSTATE=NOT FOUND"
for /f "tokens=4" %%S in ('sc query "%PG_SERVICE_NAME%" ^| findstr STATE') do set "PGSTATE=%%S"
echo  Service   : %PG_SERVICE_NAME%
echo  Status    : !PGSTATE!
echo  Server    : %DB_HOST%:%DB_PORT%
echo  Database  : %DB_NAME%
echo  Username  : %DB_USERNAME%
echo  Backups   : %BACKUPROOT%
echo.
echo  [1] Start PostgreSQL
echo  [2] Stop PostgreSQL
echo  [3] Restart PostgreSQL
echo  [4] Backup Now
echo  [5] List All Databases
echo  [6] Open Backup Folder
echo  [7] Refresh
echo  [0] Exit
echo.
set /p "CHOICE=Select: "
if "%CHOICE%"=="1" call :start
if "%CHOICE%"=="2" call :stop
if "%CHOICE%"=="3" call :restart
if "%CHOICE%"=="4" call :backup
if "%CHOICE%"=="5" call :databases
if "%CHOICE%"=="6" start "" explorer.exe "%BACKUPROOT%"
if "%CHOICE%"=="7" goto menu
if "%CHOICE%"=="0" exit /b 0
goto menu

:start
net start "%PG_SERVICE_NAME%"
pause
exit /b

:stop
echo WARNING: The inventory website cannot access its database while PostgreSQL is stopped.
choice /C YN /M "Stop PostgreSQL"
if errorlevel 2 exit /b
net stop "%PG_SERVICE_NAME%"
pause
exit /b

:restart
net stop "%PG_SERVICE_NAME%"
net start "%PG_SERVICE_NAME%"
pause
exit /b

:backup
set "PGPASSWORD=%DB_PASSWORD%"
for /f %%T in ('node tools\timestamp.mjs') do set "STAMP=%%T"
set "BACKUP=%BACKUPROOT%\%DB_NAME%-!STAMP!.dump"
echo Backing up %DB_NAME%...
"%PGBIN%\pg_dump.exe" -h "%DB_HOST%" -p "%DB_PORT%" -U "%DB_USERNAME%" -d "%DB_NAME%" -Fc -Z 9 -f "!BACKUP!.tmp"
if errorlevel 1 (del /q "!BACKUP!.tmp" 2>nul & echo Backup failed. & set "PGPASSWORD=" & pause & exit /b 1)
"%PGBIN%\pg_restore.exe" --list "!BACKUP!.tmp" >nul
if errorlevel 1 (del /q "!BACKUP!.tmp" 2>nul & echo Verification failed. & set "PGPASSWORD=" & pause & exit /b 1)
move /y "!BACKUP!.tmp" "!BACKUP!" >nul
forfiles /p "%BACKUPROOT%" /m "%DB_NAME%-*.dump" /d -30 /c "cmd /c del /q @path" 2>nul
set "PGPASSWORD="
echo Backup completed and verified:
echo !BACKUP!
pause
exit /b

:databases
set "PGPASSWORD=%DB_PASSWORD%"
echo.
echo Database                         Size
echo -------------------------------- ----------------
"%PGBIN%\psql.exe" -h "%DB_HOST%" -p "%DB_PORT%" -U "%DB_USERNAME%" -d postgres -P border=0 -P tuples_only=on -c "SELECT rpad(datname,32), pg_size_pretty(pg_database_size(datname)) FROM pg_database WHERE datistemplate=false ORDER BY datname;"
set "PGPASSWORD="
pause
exit /b
