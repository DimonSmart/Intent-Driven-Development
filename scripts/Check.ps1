$ErrorActionPreference = "Stop"

$repoRoot = & git rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0) {
    throw "Could not locate repository root."
}

Set-Location $repoRoot

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

Invoke-CheckedNative dotnet build tools/generate/Generate.csproj --nologo
Invoke-CheckedNative dotnet build tools/smoke-tests/SmokeTests.csproj --nologo

Invoke-CheckedNative dotnet exec tools/generate/bin/Debug/net10.0/Generate.dll
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "artifacts/marketplace/codex/marketplace.json"))) {
    throw "Generator did not create Codex marketplace."
}
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "artifacts/marketplace/claude/marketplace.json"))) {
    throw "Generator did not create Claude marketplace."
}

Invoke-CheckedNative dotnet exec tools/generate/bin/Debug/net10.0/Generate.dll --check
Invoke-CheckedNative dotnet exec tools/smoke-tests/bin/Debug/net10.0/SmokeTests.dll

Write-Host "Check completed."
