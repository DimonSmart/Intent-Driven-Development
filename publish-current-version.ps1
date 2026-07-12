param(
    [string] $Remote = "origin",
    [string] $Branch = "main"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Could not locate the repository root."
}

Set-Location $repoRoot

if (Invoke-Git -Arguments @("status", "--porcelain")) {
    throw "Working tree is not clean. Commit or stash changes before publishing a release tag."
}

Invoke-Git -Arguments @("fetch", "--prune", "--tags", $Remote)
Invoke-Git -Arguments @("switch", $Branch)
Invoke-Git -Arguments @("pull", "--ff-only", $Remote, $Branch)

$version = (Get-Content -Raw VERSION).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "VERSION '$version' must use MAJOR.MINOR.PATCH format."
}

$tag = "v$version"
if (Invoke-Git -Arguments @("tag", "--list", $tag)) {
    throw "Tag '$tag' already exists. Update VERSION before publishing."
}

Invoke-Git -Arguments @("tag", "-a", $tag, "-m", "Release $version")
Invoke-Git -Arguments @("push", $Remote, $Branch, $tag)

Write-Host "Published '$tag' from '$Branch' to '$Remote'."
