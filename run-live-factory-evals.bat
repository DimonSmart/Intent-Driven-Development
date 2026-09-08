@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "PROJECT=tests\Idd.Factory.LiveTests\Idd.Factory.LiveTests.csproj"
set "CONFIGURATION=Debug"
set "FRAMEWORK=net10.0"
set "TEST_DLL=%CD%\tests\Idd.Factory.LiveTests\bin\%CONFIGURATION%\%FRAMEWORK%\Idd.Factory.LiveTests.dll"

set "IDD_RUN_LIVE_FACTORY_EVALS=1"
if not defined IDD_FACTORY_EVAL_TIMEOUT_MINUTES set "IDD_FACTORY_EVAL_TIMEOUT_MINUTES=20"

echo [%DATE% %TIME%] Starting IDD Factory live end-to-end test.
echo Codex timeout: %IDD_FACTORY_EVAL_TIMEOUT_MINUTES% minutes
echo Live artifacts: %CD%\artifacts\factory-evals

call :UnlockTestDll
if errorlevel 1 exit /b %ERRORLEVEL%

dotnet test "%PROJECT%" ^
  --configuration "%CONFIGURATION%" ^
  --filter "FullyQualifiedName~FactoryEndToEndLiveTests" ^
  --nologo ^
  --verbosity minimal ^
  --logger "console;verbosity=detailed"

set "TEST_EXIT_CODE=%ERRORLEVEL%"
echo [%DATE% %TIME%] IDD Factory live test finished with exit code %TEST_EXIT_CODE%.
exit /b %TEST_EXIT_CODE%

:UnlockTestDll
if not exist "%TEST_DLL%" exit /b 0
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ^
  "$path = $env:TEST_DLL; try { $stream = [System.IO.File]::Open($path, 'Open', 'ReadWrite', 'None'); $stream.Dispose(); exit 0 } catch [System.IO.IOException] { $processes = Get-CimInstance Win32_Process -Filter \"Name = 'testhost.exe'\" | Where-Object { $_.CommandLine -like '*Idd.Factory.LiveTests*' }; if (-not $processes) { exit 2 }; foreach ($process in $processes) { & taskkill.exe /PID $process.ProcessId /T /F; if ($LASTEXITCODE -ne 0) { exit 4 } }; Start-Sleep -Milliseconds 500; exit 0 }"
exit /b %ERRORLEVEL%
