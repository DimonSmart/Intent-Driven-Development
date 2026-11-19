param(
    [ValidateNotNullOrEmpty()][string] $StandardModel = "gpt-6.1-terra",
    [switch] $WriterOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$previous = @{}
$keys = @(
    "IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS",
    "IDD_FACTORY_EVAL_STANDARD_MODEL",
    "IDD_FACTORY_EVAL_TIMEOUT_MINUTES"
)
foreach ($key in $keys) {
    $previous[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
}

$repo = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, ".."))
$project = "tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj"
Push-Location $repo
try {
    $env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS = "1"
    $env:IDD_FACTORY_EVAL_STANDARD_MODEL = $StandardModel

    Write-Host "Native process profile: features.unified_exec=false (one-shot candidate)." -ForegroundColor Cyan
    Write-Host "This is a real cancellation gate, not a deterministic CI test." -ForegroundColor Cyan
    $env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES = "15"
    & dotnet test $project --filter "FullyQualifiedName~Factory_Stage0NativeDescendantFirstCancellationStopsGrandchildWriter"
    if ($LASTEXITCODE -ne 0) {
        throw "Gate 1 FAIL: Windows host did not prove native Writer process termination with unified_exec disabled. Preserve artifacts; do not run Gate 2 or merge Factory v2."
    }

    if (-not $WriterOnly) {
        $env:IDD_FACTORY_EVAL_TIMEOUT_MINUTES = "60"
        & dotnet test $project --filter "FullyQualifiedName~Factory_NativeTenTasksRotatesOnlyAtPlanBoundariesWithLimitThree"
        if ($LASTEXITCODE -ne 0) {
            throw "Gate 2 FAIL: ten-Worker plan-boundary evaluation did not pass."
        }
    }

    Write-Host "Requested native Factory v2 gates PASSED." -ForegroundColor Green
}
finally {
    foreach ($key in $keys) {
        [Environment]::SetEnvironmentVariable($key, $previous[$key], "Process")
    }
    Pop-Location
}
