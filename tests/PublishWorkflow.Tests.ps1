$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, ".."))
$workflowPath = Join-Path $repositoryRoot ".github/workflows/publish-marketplace.yml"
$workflow = [IO.File]::ReadAllText($workflowPath)

if ($workflow -notmatch 'pwsh\s+\./scripts/Check\.ps1\s+-Mode\s+Release\b') {
    throw "Marketplace publication must run the canonical Check.ps1 -Mode Release validation."
}

# An optional standalone unittest discovery step must point to an existing directory.
# This catches obsolete CI checks that survive removal of their test sources.
$discoveryCommands = [regex]::Matches(
    $workflow,
    '(?m)\bpython(?:3)?(?:\s+-B)?\s+-m\s+unittest\s+discover\s+-s\s+(?<directory>[^\s''"]+)'
)
foreach ($command in $discoveryCommands) {
    $relative = $command.Groups["directory"].Value
    $resolved = [IO.Path]::GetFullPath([IO.Path]::Combine($repositoryRoot, $relative))
    if (-not [IO.Directory]::Exists($resolved)) {
        throw "Publish workflow references a missing Python unittest directory: $relative"
    }
}

Write-Host "Marketplace publish workflow checks passed."
