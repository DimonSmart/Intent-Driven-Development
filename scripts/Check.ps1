param(
    [ValidateSet("Fast", "Live", "Release")]
    [string] $Mode = "Fast",
    [string] $Version,
    [ValidateNotNullOrEmpty()][string] $Model,
    [ValidateNotNullOrEmpty()][string] $Reasoning
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Mode -ne "Live" -and ($PSBoundParameters.ContainsKey("Model") -or $PSBoundParameters.ContainsKey("Reasoning"))) {
    throw "-Model and -Reasoning are supported only with -Mode Live."
}

$repoRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, ".."))

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-RepositoryStatus {
    $status = @(& git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read repository status."
    }
    return $status
}

function Assert-RepositoryStatusUnchanged {
    param([string[]] $Before)

    $after = @(Get-RepositoryStatus)
    if (($Before -join "`n") -ne ($after -join "`n")) {
        throw "Validation changed the repository working tree.`nBefore:`n$($Before -join "`n")`nAfter:`n$($after -join "`n")"
    }
}

function Resolve-ValidationVersion {
    param([string] $RequestedVersion)

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return $RequestedVersion
    }

    $tagOutput = @(& git describe --tags --abbrev=0 2>$null)
    $tag = ($tagOutput -join "").Trim()
    if ($LASTEXITCODE -ne 0 -or $tag -notmatch '^v(?<version>\d+\.\d+\.\d+)$') {
        throw "Pass -Version MAJOR.MINOR.PATCH or create a vMAJOR.MINOR.PATCH tag."
    }

    return $Matches.version
}

function Invoke-FastValidation {
    param([Parameter(Mandatory = $true)][string] $ValidationVersion)

    $previousGenerationVersion = $env:IDD_GENERATION_TEST_VERSION
    $env:IDD_GENERATION_TEST_VERSION = $ValidationVersion
    try {
        Invoke-CheckedNative -FilePath "pwsh" -Arguments @("-NoProfile", "-File", "tests/DocumentationLinks.Tests.ps1")
        Invoke-CheckedNative -FilePath "pwsh" -Arguments @("-NoProfile", "-File", "tests/LiveCheck.Tests.ps1")
        Invoke-CheckedNative -FilePath "pwsh" -Arguments @("-NoProfile", "-File", "tests/MarketplaceReadme.Tests.ps1")
        Invoke-CheckedNative -FilePath "pwsh" -Arguments @("-NoProfile", "-File", "tests/CodexPluginSwitch.Tests.ps1")
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("build", "Intent-Driven-Development.slnx", "--nologo")
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("test", "tests/Idd.Generation.Tests/Idd.Generation.Tests.csproj", "--no-build", "--nologo")
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("test", "tests/Idd.Factory.Report.Tests/Idd.Factory.Report.Tests.csproj", "--no-build", "--nologo")
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("test", "tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj", "--no-build", "--nologo", "--filter", "Category!=LiveFactoryEval")
    }
    finally {
        $env:IDD_GENERATION_TEST_VERSION = $previousGenerationVersion
    }
}

function Unlock-FactoryTestDll {
    $testDll = Join-Path $repoRoot "tests/Idd.Factory.LiveTests/bin/Debug/net10.0/Idd.Factory.LiveTests.dll"
    if (-not (Test-Path -LiteralPath $testDll)) { return }

    if ($IsWindows) {
        try {
            $stream = [IO.File]::Open($testDll, 'Open', 'ReadWrite', 'None')
            $stream.Dispose()
            return
        }
        catch [IO.IOException] {
            $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'testhost.exe'" |
                Where-Object { $_.CommandLine -like '*Idd.Factory.LiveTests*' })
            if ($processes.Count -eq 0) {
                throw "The Factory test DLL is locked, but no matching testhost.exe was found: $testDll"
            }
            foreach ($process in $processes) {
                & taskkill.exe /PID $process.ProcessId /T /F | Out-Host
                if ($LASTEXITCODE -ne 0) {
                    throw "Could not stop testhost.exe PID $($process.ProcessId) (exit code $LASTEXITCODE)."
                }
            }
            Start-Sleep -Milliseconds 500
        }
        return
    }

    # On Unix, stop only testhosts which have this specific assembly open.
    $lsof = Get-Command "lsof" -ErrorAction SilentlyContinue
    if ($null -eq $lsof) { return }
    $pids = @(& $lsof.Source -t $testDll 2>$null)
    foreach ($item in $pids) {
        $pidNumber = 0
        if (-not [int]::TryParse("$item", [ref] $pidNumber)) { continue }
        $command = (& ps -p $pidNumber -o command= 2>$null) -join ' '
        if ($command -like '*testhost*' -and $command -like '*Idd.Factory.LiveTests*') {
            & kill -TERM $pidNumber
            if ($LASTEXITCODE -ne 0) { throw "Could not stop Factory testhost PID $pidNumber." }
        }
    }
    if ($pids.Count -gt 0) { Start-Sleep -Milliseconds 500 }
}

function Invoke-LiveValidation {
    param(
        [string] $ValidationVersion,
        [string] $SelectedModel,
        [string] $SelectedReasoning,
        [bool] $OverrideModel,
        [bool] $OverrideReasoning
    )

    $names = @(
        "IDD_RUN_LIVE_FACTORY_EVALS",
        "IDD_FACTORY_EVAL_VERSION",
        "IDD_FACTORY_EVAL_MODEL",
        "IDD_FACTORY_EVAL_MODEL_SOURCE",
        "IDD_FACTORY_EVAL_REASONING_EFFORT",
        "IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE",
        "IDD_FACTORY_EVAL_TIMEOUT_MINUTES",
        "IDD_FACTORY_EVAL_TEMP_ROOT",
        "IDD_FACTORY_EVAL_ARTIFACT_DIR",
        "IDD_FACTORY_EVAL_KEEP_TEMP"
    )
    $previous = @{}
    foreach ($name in $names) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
    }

    $ownsTempRoot = $false
    $testExitCode = 0
    $reportExitCode = 0
    try {
        if ($OverrideModel) {
            $env:IDD_FACTORY_EVAL_MODEL = $SelectedModel
            $env:IDD_FACTORY_EVAL_MODEL_SOURCE = "argument"
        }
        if ($OverrideReasoning) {
            $env:IDD_FACTORY_EVAL_REASONING_EFFORT = $SelectedReasoning
            $env:IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE = "argument"
        }
        if (-not $env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES) {
            $env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES = "20"
        }
        if (-not $env:IDD_FACTORY_EVAL_TEMP_ROOT) {
            $env:IDD_FACTORY_EVAL_TEMP_ROOT = [IO.Path]::Combine(
                [IO.Path]::GetTempPath(), "idd-factory-native-eval", [Guid]::NewGuid().ToString("N"))
            [void][IO.Directory]::CreateDirectory($env:IDD_FACTORY_EVAL_TEMP_ROOT)
            $ownsTempRoot = $true
        }
        if (-not $env:IDD_FACTORY_EVAL_ARTIFACT_DIR) {
            $runId = "$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff'))-$([Guid]::NewGuid().ToString('N'))"
            $env:IDD_FACTORY_EVAL_ARTIFACT_DIR = Join-Path $repoRoot "artifacts/factory-evals/$runId"
        }
        [void][IO.Directory]::CreateDirectory($env:IDD_FACTORY_EVAL_ARTIFACT_DIR)
        $env:IDD_FACTORY_EVAL_KEEP_TEMP = "1"
        $env:IDD_RUN_LIVE_FACTORY_EVALS = "1"
        if (-not [string]::IsNullOrWhiteSpace($ValidationVersion)) {
            $env:IDD_FACTORY_EVAL_VERSION = $ValidationVersion
        }

        Write-Host "Starting IDD Factory live tests (token-consuming)."
        Write-Host "Codex timeout: $env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES minutes"
        $modelLabel = if ($env:IDD_FACTORY_EVAL_MODEL) { $env:IDD_FACTORY_EVAL_MODEL } else { "repository config" }
        $reasoningLabel = if ($env:IDD_FACTORY_EVAL_REASONING_EFFORT) { $env:IDD_FACTORY_EVAL_REASONING_EFFORT } else { "repository config" }
        Write-Host "Model override: $modelLabel"
        Write-Host "Reasoning override: $reasoningLabel"
        Write-Host "Live artifacts: $env:IDD_FACTORY_EVAL_ARTIFACT_DIR"
        Write-Host "Eval temp root: $env:IDD_FACTORY_EVAL_TEMP_ROOT"

        Unlock-FactoryTestDll

        & dotnet build tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --nologo | Out-Host
        $testExitCode = $LASTEXITCODE
        if ($testExitCode -eq 0) {
            & dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --no-build --nologo --filter 'Category=LiveFactoryEval' --logger 'trx;LogFileName=live-tests.trx' --results-directory $env:IDD_FACTORY_EVAL_ARTIFACT_DIR | Out-Host
            $testExitCode = $LASTEXITCODE
        }
        Write-Host "Factory live tests finished with exit code $testExitCode. Running Factory report."

        $workspace = Join-Path $env:IDD_FACTORY_EVAL_TEMP_ROOT "workspace"
        $codexHome = Join-Path $env:IDD_FACTORY_EVAL_TEMP_ROOT "codex-home"
        if (-not (Test-Path -LiteralPath (Join-Path $workspace ".git")) -or
            -not (Test-Path -LiteralPath $codexHome)) {
            Write-Warning "Factory report unavailable: live-eval workspace or Codex home not found."
            $reportExitCode = 2
        }
        else {
            $markdown = Join-Path $env:IDD_FACTORY_EVAL_ARTIFACT_DIR "factory-report.md"
            $json = Join-Path $env:IDD_FACTORY_EVAL_ARTIFACT_DIR "factory-report.json"
            & dotnet run --project tools/idd-factory-report/Idd.Factory.Report.csproj -- latest --repo $workspace --codex-home $codexHome --json $json --markdown $markdown | Out-Host
            $reportExitCode = $LASTEXITCODE
            if ($reportExitCode -eq 0) {
                if (Test-Path -LiteralPath $markdown -PathType Leaf) {
                    Write-Host "===== Human-readable Factory report ====="
                    Get-Content -LiteralPath $markdown | Out-Host
                    Write-Host "===== End Factory report ====="
                    Write-Host "Markdown report: $markdown"
                    Write-Host "TRX results: $(Join-Path $env:IDD_FACTORY_EVAL_ARTIFACT_DIR 'live-tests.trx')"
                }
                else {
                    Write-Warning "Factory report command succeeded but did not create $markdown"
                    $reportExitCode = 2
                }
            }
        }
    }
    finally {
        if ($ownsTempRoot) {
            try {
                if (Test-Path -LiteralPath $env:IDD_FACTORY_EVAL_TEMP_ROOT) {
                    Remove-Item -LiteralPath $env:IDD_FACTORY_EVAL_TEMP_ROOT -Recurse -Force
                }
            }
            catch { Write-Warning "Could not remove owned temporary Factory directory: $_" }
        }
        foreach ($name in $names) {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], "Process")
        }
    }

    if ($testExitCode -ne 0) { return $testExitCode }
    return $reportExitCode
}

function Assert-PublishLayout {
    $required = @(
        "artifacts/marketplace/.claude-plugin/marketplace.json",
        "artifacts/marketplace/.agents/plugins/marketplace.json",
        "artifacts/marketplace/plugins/claude/idd-intent",
        "artifacts/marketplace/plugins/claude/idd-factory",
        "artifacts/marketplace/plugins/codex/idd-intent",
        "artifacts/marketplace/plugins/codex/idd-factory"
    )
    foreach ($path in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $path))) {
            throw "Missing required publish path: $path"
        }
    }
}

function Invoke-ReleaseValidation {
    param([Parameter(Mandatory = $true)][string] $ValidationVersion)

    Invoke-FastValidation -ValidationVersion $ValidationVersion
    Invoke-CheckedNative -FilePath "pwsh" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "tests/ReleaseScripts.Tests.ps1")

    $generator = "tools/generate/bin/Debug/net10.0/Generate.dll"
    Invoke-CheckedNative -FilePath "dotnet" -Arguments @("exec", $generator, "--version", $ValidationVersion)
    Assert-PublishLayout
    Invoke-CheckedNative -FilePath "dotnet" -Arguments @("exec", $generator, "--check", "--version", $ValidationVersion)

    $claude = Get-Command "claude" -ErrorAction SilentlyContinue
    if ($null -ne $claude) {
        Invoke-CheckedNative -FilePath $claude.Source -Arguments @("plugin", "validate", "artifacts/marketplace")
        Invoke-CheckedNative -FilePath $claude.Source -Arguments @("plugin", "validate", "artifacts/marketplace/plugins/claude/idd-intent")
        Invoke-CheckedNative -FilePath $claude.Source -Arguments @("plugin", "validate", "artifacts/marketplace/plugins/claude/idd-factory")
    }
    else {
        Write-Host "Claude CLI is not available; native Claude plugin validation was skipped."
    }
}

$exitCode = 0
Push-Location $repoRoot
try {
    $initialStatus = @(Get-RepositoryStatus)
    switch ($Mode) {
        "Fast" {
            $resolvedVersion = Resolve-ValidationVersion -RequestedVersion $Version
            Invoke-FastValidation -ValidationVersion $resolvedVersion
        }
        "Live" {
            $exitCode = Invoke-LiveValidation -ValidationVersion $Version -SelectedModel $Model -SelectedReasoning $Reasoning -OverrideModel ($PSBoundParameters.ContainsKey("Model")) -OverrideReasoning ($PSBoundParameters.ContainsKey("Reasoning"))
        }
        "Release" {
            if ($initialStatus.Count -ne 0) {
                throw "Release validation requires a clean working tree."
            }
            $resolvedVersion = Resolve-ValidationVersion -RequestedVersion $Version
            Invoke-ReleaseValidation -ValidationVersion $resolvedVersion
        }
    }
    Assert-RepositoryStatusUnchanged -Before $initialStatus
}
finally { Pop-Location }

if ($exitCode -ne 0) {
    [Console]::Error.WriteLine("$Mode validation failed with exit code $exitCode.")
    exit $exitCode
}
Write-Host "$Mode validation completed."
