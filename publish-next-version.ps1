param(
    [string] $Remote = "origin",
    [string] $Branch = "main",
    [string] $FirstVersion = "1.0.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not locate the repository root." }
Set-Location $repoRoot

if (Invoke-Git -Arguments @("status", "--porcelain")) {
    throw "Working tree is not clean. Commit or stash changes before publishing a release tag."
}

Invoke-Git -Arguments @("fetch", "--prune", "--tags", $Remote)
Invoke-Git -Arguments @("switch", $Branch)
Invoke-Git -Arguments @("pull", "--ff-only", $Remote, $Branch)

$firstVersionMatch = [regex]::Match($FirstVersion, '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$')
if (-not $firstVersionMatch.Success) { throw "First version '$FirstVersion' must use MAJOR.MINOR.PATCH format." }

$latestTag = Invoke-Git -Arguments @("tag", "--list", "v*") | ForEach-Object {
    $tag = $_.Trim(); $match = [regex]::Match($tag, '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$')
    if ($match.Success) { [pscustomobject]@{ Major = [int] $match.Groups["major"].Value; Minor = [int] $match.Groups["minor"].Value; Patch = [int] $match.Groups["patch"].Value } }
} | Sort-Object Major, Minor, Patch | Select-Object -Last 1

if ($latestTag) { $version = "$($latestTag.Major).$($latestTag.Minor).$($latestTag.Patch + 1)" }
else { $version = "$($firstVersionMatch.Groups["major"].Value).$($firstVersionMatch.Groups["minor"].Value).$($firstVersionMatch.Groups["patch"].Value)" }

$tag = "v$version"
Invoke-Git -Arguments @("tag", "-a", $tag, "-m", "Release $version")
try {
    Invoke-Git -Arguments @("push", $Remote, $tag)
}
catch {
    Invoke-Git -Arguments @("tag", "--delete", $tag)
    throw
}
Write-Host "Published '$tag' from '$Branch' to '$Remote'."
