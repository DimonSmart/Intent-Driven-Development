param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not locate the repository root." }
Set-Location $repoRoot

$status = & git status --porcelain
if ($LASTEXITCODE -ne 0) { throw "Could not inspect source status." }
if ($status) { throw "Release certification requires a clean source tree." }

$revision = (& git rev-parse 'HEAD^{commit}').Trim()
if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-fA-F]{40}$') { throw "Could not resolve the exact source revision." }
$tag = (& git describe --tags --exact-match HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $tag -ne "v$Version") { throw "HEAD must be the exact release tag v$Version." }
$previousTag = (& git describe --tags --abbrev=0 'HEAD^').Trim()
if ($LASTEXITCODE -ne 0 -or $previousTag -notmatch '^v(?<version>\d+\.\d+\.\d+)$') { throw "Release certification requires a previous semver tag for the reinstall lifecycle check." }
& powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Check.ps1 -Version $Version
if ($LASTEXITCODE -ne 0) { throw "Deterministic release checks failed." }

$env:IDD_FACTORY_EVAL_VERSION = $Version
$env:IDD_FACTORY_PREVIOUS_VERSION = $Matches.version
$env:IDD_FACTORY_RELEASE_CERTIFICATION = "1"
& .\run-live-factory-evals.bat --release-certification
if ($LASTEXITCODE -ne 0) { throw "Installed-plugin release live eval failed." }

Write-Host "Certified v$Version at $revision."
