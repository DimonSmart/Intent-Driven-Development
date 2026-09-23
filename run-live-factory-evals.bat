@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "CONFIGURATION=Debug"
set "FRAMEWORK=net10.0"
set "TEST_DLL=%CD%\tests\Idd.Factory.LiveTests\bin\%CONFIGURATION%\%FRAMEWORK%\Idd.Factory.LiveTests.dll"
set "CLEANUP_EVAL_TEMP_ROOT=0"

if not defined IDD_FACTORY_EVAL_TIMEOUT_MINUTES set "IDD_FACTORY_EVAL_TIMEOUT_MINUTES=20"
if not defined IDD_FACTORY_EVAL_TEMP_ROOT call :CreateEvalTempRoot
if errorlevel 1 exit /b %ERRORLEVEL%
set "IDD_FACTORY_EVAL_KEEP_TEMP=1"

echo [%DATE% %TIME%] Starting IDD Factory live tests.
echo Codex timeout: %IDD_FACTORY_EVAL_TIMEOUT_MINUTES% minutes
echo Live artifacts: %CD%\artifacts\factory-evals
echo Eval temp root: %IDD_FACTORY_EVAL_TEMP_ROOT%

call :UnlockTestDll
if errorlevel 1 exit /b %ERRORLEVEL%

powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Check.ps1" -Mode Live
set "TEST_EXIT_CODE=%ERRORLEVEL%"

echo [%DATE% %TIME%] IDD Factory live tests finished with exit code %TEST_EXIT_CODE%.
echo [%DATE% %TIME%] Running idd-factory-report.

if not exist "%IDD_FACTORY_EVAL_TEMP_ROOT%\workspace\.git" goto ReportUnavailable
if not exist "%IDD_FACTORY_EVAL_TEMP_ROOT%\codex-home" goto ReportUnavailable

dotnet run --project ".\tools\idd-factory-report\Idd.Factory.Report.csproj" -- latest --repo "%IDD_FACTORY_EVAL_TEMP_ROOT%\workspace" --codex-home "%IDD_FACTORY_EVAL_TEMP_ROOT%\codex-home"
set "REPORT_EXIT_CODE=%ERRORLEVEL%"
goto Cleanup

:ReportUnavailable
echo idd-factory-report could not run because the live-eval workspace or Codex home is unavailable.
set "REPORT_EXIT_CODE=2"

:Cleanup
if "%CLEANUP_EVAL_TEMP_ROOT%"=="1" (
    rmdir /s /q "%IDD_FACTORY_EVAL_TEMP_ROOT%" 2>nul
)

if not "%TEST_EXIT_CODE%"=="0" exit /b %TEST_EXIT_CODE%
exit /b %REPORT_EXIT_CODE%

:CreateEvalTempRoot
for /f "usebackq delims=" %%I in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "[IO.Path]::Combine([IO.Path]::GetTempPath(), 'idd-factory-native-eval', [Guid]::NewGuid().ToString('N'))"`) do set "IDD_FACTORY_EVAL_TEMP_ROOT=%%I"
if not defined IDD_FACTORY_EVAL_TEMP_ROOT exit /b 1
set "CLEANUP_EVAL_TEMP_ROOT=1"
exit /b 0

:UnlockTestDll
if not exist "%TEST_DLL%" exit /b 0
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ^
  "$path = $env:TEST_DLL; try { $stream = [System.IO.File]::Open($path, 'Open', 'ReadWrite', 'None'); $stream.Dispose(); exit 0 } catch [System.IO.IOException] { $processes = Get-CimInstance Win32_Process -Filter \"Name = 'testhost.exe'\" | Where-Object { $_.CommandLine -like '*Idd.Factory.LiveTests*' }; if (-not $processes) { exit 2 }; foreach ($process in $processes) { & taskkill.exe /PID $process.ProcessId /T /F; if ($LASTEXITCODE -ne 0) { exit 4 } }; Start-Sleep -Milliseconds 500; exit 0 }"
exit /b %ERRORLEVEL%
