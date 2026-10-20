@echo off
setlocal

rem Codex IDD source selector. Run with "published", "local", or no arguments.
rem The PowerShell script and this BAT must remain in the same folder.

where pwsh >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 - pwsh - was not found. Install PowerShell 7 and retry.
  exit /b 1
)

if "%~1"=="" (
  pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Switch-Idd-Codex.ps1"
) else (
  pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Switch-Idd-Codex.ps1" -Source "%~1"
)

set "RESULT=%ERRORLEVEL%"
if "%~1"=="" pause
exit /b %RESULT%
