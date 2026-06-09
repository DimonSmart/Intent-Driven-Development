$ErrorActionPreference = "Stop"

$repoRoot = & git rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0) {
    throw "Could not locate repository root."
}

Set-Location $repoRoot

dotnet build tools/generate/Generate.csproj --nologo
dotnet build tools/idd-tool/IntentDrivenDevelopment.Tool.csproj --nologo
dotnet build tools/smoke-tests/SmokeTests.csproj --nologo

dotnet exec tools/generate/bin/Debug/net10.0/Generate.dll
dotnet exec tools/generate/bin/Debug/net10.0/Generate.dll --check
dotnet exec tools/smoke-tests/bin/Debug/net10.0/SmokeTests.dll

Write-Host "Check completed."
