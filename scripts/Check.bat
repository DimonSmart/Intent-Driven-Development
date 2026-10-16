@echo off
setlocal

pushd "%~dp0\.." || exit /b 1

pwsh -NoProfile -File ".\scripts\Check.ps1" %*
set "exitCode=%ERRORLEVEL%"

popd
exit /b %exitCode%
