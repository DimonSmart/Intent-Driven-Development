@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "IDD_RUN_LIVE_INTENT_EVALS=1"
if not defined IDD_INTENT_EVAL_TIMEOUT_MINUTES set "IDD_INTENT_EVAL_TIMEOUT_MINUTES=20"

echo [%DATE% %TIME%] Starting IDD Intent import live evals.
echo Codex timeout per case: %IDD_INTENT_EVAL_TIMEOUT_MINUTES% minutes

dotnet test ".\tests\Idd.Intent.LiveTests\Idd.Intent.LiveTests.csproj" --nologo --filter "Category=LiveIntentEval"
exit /b %ERRORLEVEL%
