$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, ".."))
$check = Join-Path $repoRoot "scripts/Check.ps1"
$example = Join-Path $repoRoot "scripts/Run-Live.local.ps1.example"
foreach ($path in @("scripts/evals/run-live-factory-evals.bat", "scripts/evals/run-live-factory-evals.zsh")) {
    if (Test-Path -LiteralPath (Join-Path $repoRoot $path)) {
        throw "Redundant Factory launcher remains: $path"
    }
}
if (-not (Test-Path -LiteralPath $example)) { throw "Missing local Live preset example." }
if (-not ([IO.File]::ReadAllText($example).Contains('-Model "gpt-6-luna"'))) {
    throw "Local preset must document GPT-6 Luna."
}

# A private PATH stub ensures regression checks never spend LLM tokens.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ("idd-live-check-test " + [Guid]::NewGuid().ToString("N"))
$originalPath = $env:PATH
$vars = @("IDD_FACTORY_EVAL_TEMP_ROOT", "IDD_FACTORY_EVAL_ARTIFACT_DIR",
          "IDD_FACTORY_EVAL_MODEL", "IDD_FACTORY_EVAL_MODEL_SOURCE",
          "IDD_FACTORY_EVAL_REASONING_EFFORT", "IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE",
          "IDD_TEST_LOG", "IDD_TEST_TEST_EXIT", "IDD_TEST_REPORT_EXIT")
$previous = @{}
foreach ($name in $vars) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }

function Invoke-CheckStubbed {
    param([string[]] $Arguments, [int] $ExpectedExit)
    $lines = @(& pwsh -NoProfile -File $check @Arguments 2>&1)
    $result = $lines -join [Environment]::NewLine
    if ($LASTEXITCODE -ne $ExpectedExit) {
        throw "Check.ps1 returned $LASTEXITCODE instead of $ExpectedExit for '$($Arguments -join ' ')': $result"
    }
    return $result
}

try {
    $stubDir = Join-Path $fixture "stub commands"
    $otherCwd = Join-Path $fixture "working directory with spaces"
    [void][IO.Directory]::CreateDirectory($stubDir)
    [void][IO.Directory]::CreateDirectory($otherCwd)

    if ($IsWindows) {
        $stub = Join-Path $stubDir "dotnet.cmd"
        $command = @'
@echo off
echo %1 ^| %IDD_FACTORY_EVAL_MODEL% ^| %IDD_FACTORY_EVAL_MODEL_SOURCE% ^| %IDD_FACTORY_EVAL_REASONING_EFFORT% ^| %IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE% ^| %* >> "%IDD_TEST_LOG%"
if "%1"=="test" exit /b %IDD_TEST_TEST_EXIT%
if "%1"=="run" (
  if "%IDD_TEST_REPORT_EXIT%"=="0" echo # Stub report > "%IDD_FACTORY_EVAL_ARTIFACT_DIR%\factory-report.md"
  exit /b %IDD_TEST_REPORT_EXIT%
)
exit /b 0
'@
        [IO.File]::WriteAllText($stub, $command)
    }
    else {
        $stub = Join-Path $stubDir "dotnet"
        $command = @'
#!/bin/sh
printf '%s | %s | %s | %s | %s | %s\n' "$1" "$IDD_FACTORY_EVAL_MODEL" "$IDD_FACTORY_EVAL_MODEL_SOURCE" "$IDD_FACTORY_EVAL_REASONING_EFFORT" "$IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE" "$*" >> "$IDD_TEST_LOG"
case "$1" in
  test) exit "$IDD_TEST_TEST_EXIT" ;;
  run)
    if [ "$IDD_TEST_REPORT_EXIT" = "0" ]; then printf '# Stub report\n' > "$IDD_FACTORY_EVAL_ARTIFACT_DIR/factory-report.md"; fi
    exit "$IDD_TEST_REPORT_EXIT" ;;
esac
exit 0
'@
        [IO.File]::WriteAllText($stub, $command)
        & chmod +x $stub
        if ($LASTEXITCODE -ne 0) { throw "Could not make dotnet stub executable." }
    }

    $env:PATH = $stubDir + [IO.Path]::PathSeparator + $originalPath
    $resolvedDotnet = (Get-Command dotnet).Source
    if (-not [string]::Equals([IO.Path]::GetFullPath($stub),
        [IO.Path]::GetFullPath($resolvedDotnet), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe test setup: real dotnet resolved instead of stub: $resolvedDotnet"
    }
    $env:IDD_TEST_LOG = Join-Path $fixture "invocations.txt"
    $env:IDD_TEST_TEST_EXIT = "0"
    $env:IDD_TEST_REPORT_EXIT = "0"
    $env:IDD_FACTORY_EVAL_TEMP_ROOT = Join-Path $fixture "eval temporary"
    $env:IDD_FACTORY_EVAL_ARTIFACT_DIR = Join-Path $fixture "eval artifacts"
    [void][IO.Directory]::CreateDirectory((Join-Path $env:IDD_FACTORY_EVAL_TEMP_ROOT "workspace/.git"))
    [void][IO.Directory]::CreateDirectory((Join-Path $env:IDD_FACTORY_EVAL_TEMP_ROOT "codex-home"))
    $env:IDD_FACTORY_EVAL_MODEL = "environment-model"
    $env:IDD_FACTORY_EVAL_MODEL_SOURCE = "environment"
    $env:IDD_FACTORY_EVAL_REASONING_EFFORT = "high"
    $env:IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE = "environment"

    Push-Location $otherCwd
    try {
        $output = Invoke-CheckStubbed -Arguments @("-Mode", "Live", "-Model", "gpt-6-luna", "-Reasoning", "medium") -ExpectedExit 0
    }
    finally { Pop-Location }
    if (-not $output.Contains("Factory report")) { throw "Expected Factory report output." }
    $log = [IO.File]::ReadAllText($env:IDD_TEST_LOG)
    foreach ($required in @("build | gpt-6-luna | argument | medium | argument",
                            "test | gpt-6-luna | argument | medium | argument",
                            "run | gpt-6-luna | argument | medium | argument",
                            "Category=LiveFactoryEval")) {
        if (-not $log.Contains($required)) { throw "CLI precedence or invocation missing: $required" }
    }
    if (-not [IO.File]::Exists((Join-Path $env:IDD_FACTORY_EVAL_ARTIFACT_DIR "factory-report.md"))) {
        throw "Factory report was not generated."
    }
    if (-not [IO.Directory]::Exists($env:IDD_FACTORY_EVAL_TEMP_ROOT)) {
        throw "Caller-owned temp root was removed."
    }

    [IO.File]::Delete($env:IDD_TEST_LOG)
    $null = Invoke-CheckStubbed -Arguments @("-Mode", "Live") -ExpectedExit 0
    $log = [IO.File]::ReadAllText($env:IDD_TEST_LOG)
    if (-not $log.Contains("test | environment-model | environment | high | environment")) {
        throw "Environment overrides were not preserved."
    }

    [IO.File]::Delete($env:IDD_TEST_LOG)
    $env:IDD_TEST_TEST_EXIT = "7"
    $null = Invoke-CheckStubbed -Arguments @("-Mode", "Live") -ExpectedExit 7
    if (-not ([IO.File]::ReadAllText($env:IDD_TEST_LOG)).Contains("run |")) {
        throw "Report was skipped after test failure."
    }

    $env:IDD_TEST_TEST_EXIT = "0"
    $env:IDD_TEST_REPORT_EXIT = "5"
    $null = Invoke-CheckStubbed -Arguments @("-Mode", "Live") -ExpectedExit 5

    [IO.File]::Delete($env:IDD_TEST_LOG)
    $null = Invoke-CheckStubbed -Arguments @("-Mode", "Fast", "-Model", "gpt-6-luna") -ExpectedExit 1
    if ([IO.File]::Exists($env:IDD_TEST_LOG)) {
        throw "An invalid invocation reached the dotnet stub."
    }
}
finally {
    $env:PATH = $originalPath
    foreach ($name in $vars) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], "Process")
    }
    if ([IO.Directory]::Exists($fixture)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
Write-Host "Single-entry Live Check smoke tests completed."
