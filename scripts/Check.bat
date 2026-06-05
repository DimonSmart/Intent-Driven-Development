@echo off
setlocal

pushd "%~dp0\.." || exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Check.ps1"
set "exitCode=%ERRORLEVEL%"

popd
exit /b %exitCode%
