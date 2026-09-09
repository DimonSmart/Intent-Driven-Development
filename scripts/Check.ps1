param(
    [ValidateSet("Fast", "Live", "Release")]
    [string] $Mode = "Fast",
    [string] $Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Could not locate repository root."
}

Set-Location $repoRoot

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
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("build", "Intent-Driven-Development.slnx", "--nologo")
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("test", "tests/FactoryBenchmark.Tests/FactoryBenchmark.Tests.csproj", "--no-build", "--nologo")
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("test", "tests/Idd.Factory.Tests/Idd.Factory.Tests.csproj", "--no-build", "--nologo")
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("test", "tests/Idd.Generation.Tests/Idd.Generation.Tests.csproj", "--no-build", "--nologo")
    }
    finally {
        $env:IDD_GENERATION_TEST_VERSION = $previousGenerationVersion
    }
}

function Invoke-LiveValidation {
    param([string] $ValidationVersion)

    $previousLive = $env:IDD_RUN_LIVE_FACTORY_EVALS
    $previousEvalVersion = $env:IDD_FACTORY_EVAL_VERSION
    $env:IDD_RUN_LIVE_FACTORY_EVALS = "1"
    if (-not [string]::IsNullOrWhiteSpace($ValidationVersion)) {
        $env:IDD_FACTORY_EVAL_VERSION = $ValidationVersion
    }

    try {
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("build", "tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj", "--nologo")
        Invoke-CheckedNative -FilePath "dotnet" -Arguments @("test", "tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj", "--no-build", "--nologo")
    }
    finally {
        $env:IDD_RUN_LIVE_FACTORY_EVALS = $previousLive
        $env:IDD_FACTORY_EVAL_VERSION = $previousEvalVersion
    }
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

$initialStatus = @(Get-RepositoryStatus)

switch ($Mode) {
    "Fast" {
        $resolvedVersion = Resolve-ValidationVersion -RequestedVersion $Version
        Invoke-FastValidation -ValidationVersion $resolvedVersion
    }
    "Live" {
        Invoke-LiveValidation -ValidationVersion $Version
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
Write-Host "$Mode validation completed."
