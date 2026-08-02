@echo off
setlocal EnableExtensions
cd /d "%~dp0"
dotnet run --project tools\CodexLaunchExperiment\CodexLaunchExperiment.csproj --configuration Release
exit /b %ERRORLEVEL%
