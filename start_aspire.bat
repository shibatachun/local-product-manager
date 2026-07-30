@echo off
setlocal
cd /d "%~dp0"
title LocalProductManager Aspire AppHost
dotnet run --project "%~dp0LocalProductManager.AppHost\LocalProductManager.AppHost.csproj" --launch-profile http
pause
