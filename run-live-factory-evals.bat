@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Place this file in the repository root.
cd /d "%~dp0"

set "PROJECT=tests\Idd.Factory.LiveTests\Idd.Factory.LiveTests.csproj"
set "CONFIGURATION=Debug"
set "FRAMEWORK=net10.0"
set "TEST_DLL=%CD%\tests\Idd.Factory.LiveTests\bin\%CONFIGURATION%\%FRAMEWORK%\Idd.Factory.LiveTests.dll"

set "IDD_RUN_LIVE_FACTORY_EVALS=1"
set "IDD_CODEX_LAUNCH_PROFILE="
set "WRITE_PROBE_RESULT=NOT RUN"
set "TELEMETRY_RESULT=NOT RUN"
set "FACTORY_EVALUATION_RESULT=NOT RUN"
for /f "usebackq delims=" %%I in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "'{0:yyyyMMdd-HHmmss}-{1}' -f (Get-Date).ToUniversalTime(), ([Guid]::NewGuid().ToString('N').Substring(0,8))"`) do set "IDD_CODEX_LAUNCH_DISCOVERY_ID=%%I"
if not defined IDD_CODEX_LAUNCH_DISCOVERY_ID set "IDD_CODEX_LAUNCH_DISCOVERY_ID=manual-%RANDOM%-%RANDOM%"

call :UnlockTestDll
if errorlevel 1 exit /b %ERRORLEVEL%

echo Codex launch profile discovery
echo.

for %%P in (isolated-workspace-write configured-workspace-write windows-unelevated-workspace-write windows-elevated-workspace-write) do (
  call :TryLaunchProfile "%%P"
  if not errorlevel 1 goto LaunchProfileSelected
)

echo No Codex launch profile passed the workspace write probe.
echo Factory evaluation will not run.
set "WRITE_PROBE_RESULT=FAIL"
call :WriteTrackedSummary
exit /b 1

:LaunchProfileSelected
echo.
echo Selected profile: %IDD_CODEX_LAUNCH_PROFILE%
echo Workspace write probe: PASS
echo.
set "WRITE_PROBE_RESULT=PASS"

dotnet test "%PROJECT%" ^
  --configuration "%CONFIGURATION%" ^
  --filter "FullyQualifiedName~CodexSubagentTelemetryLiveTests" ^
  --logger "console;verbosity=detailed"

if errorlevel 1 (
  set "TELEMETRY_RESULT=FAIL"
  call :WriteTrackedSummary
  exit /b !ERRORLEVEL!
)

echo Subagent telemetry probe: PASS
set "TELEMETRY_RESULT=PASS"

dotnet test "%PROJECT%" ^
  --configuration "%CONFIGURATION%" ^
  --filter "FullyQualifiedName~TwoStepCatalogFactoryEvalTests" ^
  --logger "console;verbosity=detailed"

set "FACTORY_EVALUATION_EXIT_CODE=!ERRORLEVEL!"
if "!FACTORY_EVALUATION_EXIT_CODE!"=="0" (
  set "FACTORY_EVALUATION_RESULT=PASS"
) else (
  set "FACTORY_EVALUATION_RESULT=FAIL"
)
call :WriteTrackedSummary
exit /b !FACTORY_EVALUATION_EXIT_CODE!


:TryLaunchProfile
set "IDD_CODEX_LAUNCH_PROFILE=%~1"
echo Trying Codex launch profile: %IDD_CODEX_LAUNCH_PROFILE%

dotnet test "%PROJECT%" ^
  --configuration "%CONFIGURATION%" ^
  --filter "FullyQualifiedName~CodexWorkspaceWriteProbeLiveTests" ^
  --logger "console;verbosity=detailed"

set "PROBE_EXIT_CODE=%ERRORLEVEL%"
if "%PROBE_EXIT_CODE%"=="0" (
  echo %IDD_CODEX_LAUNCH_PROFILE%: PASS
  exit /b 0
)

echo %IDD_CODEX_LAUNCH_PROFILE%: FAIL
echo.
exit /b %PROBE_EXIT_CODE%


:WriteTrackedSummary
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0scripts\Update-LiveFactoryEvalSummary.ps1" ^
  -RepositoryRoot "%CD%" ^
  -DiscoveryId "%IDD_CODEX_LAUNCH_DISCOVERY_ID%" ^
  -SelectedProfile "%IDD_CODEX_LAUNCH_PROFILE%" ^
  -WriteProbeResult "%WRITE_PROBE_RESULT%" ^
  -TelemetryResult "%TELEMETRY_RESULT%" ^
  -FactoryEvaluationResult "%FACTORY_EVALUATION_RESULT%"

if errorlevel 1 (
  echo Failed to update the tracked live evaluation summary.
  exit /b !ERRORLEVEL!
)

exit /b 0


:UnlockTestDll
if not exist "%TEST_DLL%" exit /b 0

powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ^
  "$path = $env:TEST_DLL; " ^
  "try { " ^
  "  $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None); " ^
  "  $stream.Dispose(); " ^
  "  exit 0; " ^
  "} catch [System.IO.IOException] { " ^
  "  Write-Host ('Test DLL is locked: ' + $path); " ^
  "  $processes = Get-Process -Name testhost -ErrorAction SilentlyContinue; " ^
  "  if (-not $processes) { " ^
  "    Write-Error 'The DLL is locked, but no testhost process was found.'; " ^
  "    exit 2; " ^
  "  }; " ^
  "  foreach ($process in $processes) { " ^
  "    Write-Host ('Stopping stale testhost process, PID ' + $process.Id); " ^
  "    Stop-Process -Id $process.Id -Force; " ^
  "  }; " ^
  "  Start-Sleep -Milliseconds 500; " ^
  "  try { " ^
  "    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None); " ^
  "    $stream.Dispose(); " ^
  "    Write-Host 'The test DLL is unlocked.'; " ^
  "    exit 0; " ^
  "  } catch { " ^
  "    Write-Error ('The test DLL is still locked: ' + $path); " ^
  "    exit 3; " ^
  "  } " ^
  "}"

set "UNLOCK_EXIT_CODE=%ERRORLEVEL%"
if not "%UNLOCK_EXIT_CODE%"=="0" (
  echo Failed to unlock the test assembly.
  exit /b %UNLOCK_EXIT_CODE%
)

exit /b 0
