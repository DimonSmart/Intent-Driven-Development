param(
    [ValidateNotNullOrEmpty()][string] $StandardModel = "gpt-6.1-terra"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$keys = @("IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS",
          "IDD_FACTORY_EVAL_STANDARD_MODEL",
          "IDD_FACTORY_EVAL_TIMEOUT_MINUTES")
$previous = @{}
foreach ($key in $keys) {
    $previous[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
}

$repo = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, ".."))
Push-Location $repo
try {
    $env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS = "1"
    $env:IDD_FACTORY_EVAL_STANDARD_MODEL = $StandardModel
    $env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES = "60"

    & dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --filter "FullyQualifiedName~Factory_NativeTenTasksRotatesOnlyAtPlanBoundariesWithLimitThree"
    if ($LASTEXITCODE -ne 0) {
        throw "Factory v2 normal-operation E2E failed; inspect live artifacts."
    }
}
finally {
    foreach ($key in $keys) {
        [Environment]::SetEnvironmentVariable($key, $previous[$key], "Process")
    }
    Pop-Location
}
