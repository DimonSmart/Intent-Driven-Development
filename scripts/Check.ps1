$ErrorActionPreference = "Stop"

$repoRoot = & git rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0) {
    throw "Could not locate repository root."
}

Set-Location $repoRoot

dotnet run --project tools/generate
dotnet run --project tools/generate -- --check
dotnet run --project tools/smoke-tests

Write-Host "Check completed."
