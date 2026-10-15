$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not locate repository root." }
$bat = Join-Path $repositoryRoot "scripts/evals/run-live-factory-evals.bat"
$zsh = Join-Path $repositoryRoot "scripts/evals/run-live-factory-evals.zsh"
foreach ($script in @($bat, $zsh)) {
    if (-not [IO.File]::Exists($script)) { throw "Missing launcher: $script" }
}

$batText = [IO.File]::ReadAllText($bat)
$zshText = [IO.File]::ReadAllText($zsh)
foreach ($required in @('IDD_FACTORY_EVAL_MODEL=%~2', 'IDD_FACTORY_EVAL_MODEL_SOURCE=argument',
                       'IDD_FACTORY_EVAL_REASONING_EFFORT=%~2', 'IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE=argument',
                       'call :ParseArguments', 'pushd', 'popd', ':UnlockTestDll')) {
    if (-not $batText.Contains($required)) { throw "BAT lost launcher contract: $required" }
}
foreach ($required in @('IDD_FACTORY_EVAL_MODEL_SOURCE="argument"',
                       'IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE="argument"',
                       'git status --porcelain', 'IDD_FACTORY_EVAL_ARTIFACT_DIR', 'idd-factory-report')) {
    if (-not $zshText.Contains($required)) { throw "Zsh lost launcher contract: $required" }
}

$mode = @(& git -C $repositoryRoot ls-files --stage -- scripts/evals/run-live-factory-evals.zsh)
if ($LASTEXITCODE -ne 0 -or ($mode -join '') -notmatch '^100755\s') {
    throw "Zsh launcher must remain executable in Git (mode 100755)."
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) ("idd launcher smoke " + [Guid]::NewGuid().ToString("N"))
try {
    $scriptDir = Join-Path $fixture "scripts/evals"
    $cwd = Join-Path $fixture "other cwd with spaces"
    [void][IO.Directory]::CreateDirectory($scriptDir)
    [void][IO.Directory]::CreateDirectory($cwd)
    Copy-Item -LiteralPath $bat -Destination $scriptDir
    Copy-Item -LiteralPath $zsh -Destination $scriptDir

    if ($IsWindows) {
        $stubs = Join-Path $fixture "stub commands"
        [void][IO.Directory]::CreateDirectory($stubs)
        $invoked = Join-Path $fixture "forbidden-command-invoked.txt"
        foreach ($command in @("powershell.cmd", "dotnet.cmd")) {
            $stubText = @('@echo off', ('echo invoked >> "' + $invoked + '"'), 'exit /b 99') -join [Environment]::NewLine
            [IO.File]::WriteAllText((Join-Path $stubs $command), $stubText)
        }

        $originalPath = $env:PATH
        $env:PATH = $stubs + [IO.Path]::PathSeparator + $env:PATH
        try {
            foreach ($case in @(
                @{ Args = "--help"; Code = 0; Text = "Usage:" }
                @{ Args = "-h"; Code = 0; Text = "Usage:" }
                @{ Args = "--invalid"; Code = 2; Text = "Unknown argument" }
                @{ Args = "--model"; Code = 2; Text = "requires a value" }
                @{ Args = "--reasoning"; Code = 2; Text = "requires a value" }
            )) {
                $runner = Join-Path $fixture "smoke.cmd"
                $lines = @(
                    '@echo off'
                    ('cd /d "' + $cwd + '"')
                    ('call "' + (Join-Path $scriptDir 'run-live-factory-evals.bat') + '" ' + $case.Args)
                    'set RESULT=%ERRORLEVEL%'
                    'echo CURRENT_DIRECTORY=%CD%'
                    'exit /b %RESULT%'
                )
                [IO.File]::WriteAllText($runner, ($lines -join [Environment]::NewLine))
                $result = @(& cmd.exe /d /c ('call "' + $runner + '"') 2>&1) -join [Environment]::NewLine
                $code = $LASTEXITCODE
                if ($code -ne $case.Code) {
                    throw "BAT $($case.Args) returned $code (expected $($case.Code)): $result"
                }
                if (-not $result.Contains($case.Text)) {
                    throw "BAT $($case.Args) omitted expected diagnostic: $result"
                }
                if (-not $result.Contains("CURRENT_DIRECTORY=$cwd")) {
                    throw "BAT $($case.Args) changed the invoking current directory: $result"
                }
                if ([IO.File]::Exists($invoked)) { throw "BAT argument check invoked a forbidden external command." }
                if ([IO.Directory]::Exists((Join-Path $fixture "artifacts"))) {
                    throw "BAT argument check generated artifacts."
                }
            }
        }
        finally { $env:PATH = $originalPath }
    }

    $zshCommand = Get-Command "zsh" -ErrorAction SilentlyContinue
    if ($null -ne $zshCommand) {
        & zsh -n $zsh
        if ($LASTEXITCODE -ne 0) { throw "Zsh syntax check failed." }
        Push-Location $cwd
        try {
            foreach ($case in @(
                @{ Args = "--help"; Code = 0 }
                @{ Args = "-h"; Code = 0 }
                @{ Args = "--unknown"; Code = 2 }
                @{ Args = "--model"; Code = 2 }
                @{ Args = "--reasoning"; Code = 2 }
            )) {
                $messages = @(& zsh (Join-Path $scriptDir "run-live-factory-evals.zsh") $case.Args 2>&1)
                if ($LASTEXITCODE -ne $case.Code) {
                    throw "Zsh $($case.Args) returned $LASTEXITCODE; expected $($case.Code): $messages"
                }
            }
        }
        finally { Pop-Location }
    }
    else {
        Write-Host "Zsh is unavailable here; manual macOS Zsh smoke remains required."
    }
}
finally {
    if ([IO.Directory]::Exists($fixture)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
Write-Host "Factory launcher smoke tests completed."
