@echo off
setlocal EnableExtensions

rem Place this file in the repository root.
cd /d "%~dp0"

set "PROJECT=tests\Idd.Factory.LiveTests\Idd.Factory.LiveTests.csproj"
set "CONFIGURATION=Debug"
set "FRAMEWORK=net10.0"
set "TEST_DLL=%CD%\tests\Idd.Factory.LiveTests\bin\%CONFIGURATION%\%FRAMEWORK%\Idd.Factory.LiveTests.dll"

set "IDD_RUN_LIVE_FACTORY_EVALS=1"
set "IDD_CODEX_LAUNCH_PROFILE=configured-workspace-write"

call :UnlockTestDll
if errorlevel 1 exit /b %ERRORLEVEL%

dotnet test "%PROJECT%" ^
  --configuration "%CONFIGURATION%" ^
  --filter "FullyQualifiedName~TwoStepCatalogFactoryEvalTests" ^
  --logger "console;verbosity=detailed"

exit /b %ERRORLEVEL%


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
