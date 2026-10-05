@echo off
setlocal EnableExtensions

cd /d "%~dp0"
call :ParseArguments %*
if errorlevel 1 exit /b %ERRORLEVEL%
set "CONFIGURATION=Debug"
set "FRAMEWORK=net10.0"
set "TEST_DLL=%CD%\tests\Idd.Factory.LiveTests\bin\%CONFIGURATION%\%FRAMEWORK%\Idd.Factory.LiveTests.dll"
set "CLEANUP_EVAL_TEMP_ROOT=0"

if not defined IDD_FACTORY_EVAL_TIMEOUT_MINUTES set "IDD_FACTORY_EVAL_TIMEOUT_MINUTES=20"
if not defined IDD_FACTORY_EVAL_TEMP_ROOT call :CreateEvalTempRoot
if errorlevel 1 exit /b %ERRORLEVEL%
if not defined IDD_FACTORY_EVAL_ARTIFACT_DIR call :CreateArtifactRoot
if errorlevel 1 exit /b %ERRORLEVEL%
set "IDD_FACTORY_EVAL_KEEP_TEMP=1"

echo [%DATE% %TIME%] Starting IDD Factory live tests.
echo Codex timeout: %IDD_FACTORY_EVAL_TIMEOUT_MINUTES% minutes
if defined IDD_FACTORY_EVAL_MODEL (echo Model override: %IDD_FACTORY_EVAL_MODEL%) else (echo Model override: repository config)
if defined IDD_FACTORY_EVAL_REASONING_EFFORT (echo Reasoning override: %IDD_FACTORY_EVAL_REASONING_EFFORT%) else (echo Reasoning override: repository config)
echo Live artifacts: %IDD_FACTORY_EVAL_ARTIFACT_DIR%
echo Eval temp root: %IDD_FACTORY_EVAL_TEMP_ROOT%

call :UnlockTestDll
if errorlevel 1 exit /b %ERRORLEVEL%

powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Check.ps1" -Mode Live
set "TEST_EXIT_CODE=%ERRORLEVEL%"

echo [%DATE% %TIME%] IDD Factory live tests finished with exit code %TEST_EXIT_CODE%.
echo [%DATE% %TIME%] Running idd-factory-report.

if not exist "%IDD_FACTORY_EVAL_TEMP_ROOT%\workspace\.git" goto ReportUnavailable
if not exist "%IDD_FACTORY_EVAL_TEMP_ROOT%\codex-home" goto ReportUnavailable

dotnet run --project ".\tools\idd-factory-report\Idd.Factory.Report.csproj" -- latest --repo "%IDD_FACTORY_EVAL_TEMP_ROOT%\workspace" --codex-home "%IDD_FACTORY_EVAL_TEMP_ROOT%\codex-home" --json "%IDD_FACTORY_EVAL_ARTIFACT_DIR%\factory-report.json" --markdown "%IDD_FACTORY_EVAL_ARTIFACT_DIR%\factory-report.md"
set "REPORT_EXIT_CODE=%ERRORLEVEL%"
if not "%REPORT_EXIT_CODE%"=="0" goto Cleanup
echo.
echo ===== Human-readable Factory report =====
type "%IDD_FACTORY_EVAL_ARTIFACT_DIR%\factory-report.md"
echo ===== End Factory report =====
echo Markdown report: %IDD_FACTORY_EVAL_ARTIFACT_DIR%\factory-report.md
echo TRX results:     %IDD_FACTORY_EVAL_ARTIFACT_DIR%\live-tests.trx
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

:ParseArguments
if "%~1"=="" exit /b 0
if /I "%~1"=="--model" goto ParseModel
if /I "%~1"=="--reasoning" goto ParseReasoning
if /I "%~1"=="--help" goto PrintUsage
if /I "%~1"=="-h" goto PrintUsage
echo Unknown argument: %~1 >&2
goto PrintUsageError

:ParseModel
if "%~2"=="" (
  echo --model requires a value. >&2
  goto PrintUsageError
)
set "IDD_FACTORY_EVAL_MODEL=%~2"
set "IDD_FACTORY_EVAL_MODEL_SOURCE=argument"
shift
shift
goto ParseArguments

:ParseReasoning
if "%~2"=="" (
  echo --reasoning requires a value. >&2
  goto PrintUsageError
)
set "IDD_FACTORY_EVAL_REASONING_EFFORT=%~2"
set "IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE=argument"
shift
shift
goto ParseArguments

:PrintUsage
echo Usage: %~nx0 [--model ^<model^>] [--reasoning ^<effort^>]
exit /b 0

:PrintUsageError
echo Usage: %~nx0 [--model ^<model^>] [--reasoning ^<effort^>] >&2
exit /b 2

:CreateArtifactRoot
for /f "usebackq delims=" %%I in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$id = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff') + '-' + [Guid]::NewGuid().ToString('N'); [IO.Path]::Combine($pwd.Path, 'artifacts', 'factory-evals', $id)"`) do set "IDD_FACTORY_EVAL_ARTIFACT_DIR=%%I"
if not defined IDD_FACTORY_EVAL_ARTIFACT_DIR exit /b 1
exit /b 0

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
