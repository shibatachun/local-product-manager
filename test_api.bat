@echo off
setlocal
cd /d "%~dp0"

echo Running API smoke and inventory concurrency tests...
dotnet test LocalProductManager.Api.Tests\LocalProductManager.Api.Tests.csproj --configuration Debug

if errorlevel 1 (
    echo.
    echo ============================================================
    echo API TEST RESULT: FAILED
    echo Review the errors above for the failed test name and reason.
    echo ============================================================
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo API TEST RESULT: PASSED
echo All tests passed. The production database was not used.
echo ============================================================
echo.
pause
exit /b 0
