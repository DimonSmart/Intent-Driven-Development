@echo off
setlocal EnableExtensions

rem Place this file in the repository root.
cd /d "%~dp0"

set "PROJECT=tests\Idd.Factory.LiveTests\Idd.Factory.LiveTests.csproj"
set "CONFIGURATION=Debug"
set "FRAMEWORK=net10.0"
set "TEST_DLL=%CD%\tests\Idd.Factory.LiveTests\bin\%CONFIGURATION%\%FRAMEWORK%\Idd.Factory.LiveTests.dll"

set "IDD_RUN_LIVE_FACTORY_EVALS=1"
set "IDD_CODEX_LAUNCH_PROFILE=unrestricted-runtime-launch"
if not defined IDD_FACTORY_EVAL_TIMEOUT_MINUTES set "IDD_FACTORY_EVAL_TIMEOUT_MINUTES=20"

echo [%DATE% %TIME%] Starting IDD Factory live eval.
echo Launch profile: %IDD_CODEX_LAUNCH_PROFILE%
echo Codex timeout: %IDD_FACTORY_EVAL_TIMEOUT_MINUTES% minutes
echo Live artifacts: %CD%\artifacts\factory-evals
echo Current phase is recorded in the newest artifacts\factory-evals\*\progress.log.

call :UnlockTestDll
if errorlevel 1 exit /b %ERRORLEVEL%

dotnet test "%PROJECT%" ^
  --configuration "%CONFIGURATION%" ^
  --filter "FullyQualifiedName~TwoStepCatalogFactoryEvalTests" ^
  --nologo ^
  --verbosity minimal ^
  --logger "console;verbosity=detailed"

set "TEST_EXIT_CODE=%ERRORLEVEL%"
echo [%DATE% %TIME%] IDD Factory live eval finished with exit code %TEST_EXIT_CODE%.
exit /b %TEST_EXIT_CODE%


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
  "  $processes = Get-CimInstance Win32_Process -Filter \"Name = 'testhost.exe'\" | Where-Object { $_.CommandLine -like '*Idd.Factory.LiveTests*' }; " ^
  "  if (-not $processes) { " ^
  "    Write-Error 'The DLL is locked, but no testhost process was found.'; " ^
  "    exit 2; " ^
  "  }; " ^
  "  foreach ($process in $processes) { " ^
  "    Write-Host ('Stopping stale live-eval process tree, PID ' + $process.ProcessId); " ^
  "    & taskkill.exe /PID $process.ProcessId /T /F; " ^
  "    if ($LASTEXITCODE -ne 0) { exit 4; } " ^
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
