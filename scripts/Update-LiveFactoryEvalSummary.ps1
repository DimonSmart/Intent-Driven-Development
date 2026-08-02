param(
    [Parameter(Mandatory = $true)] [string] $RepositoryRoot,
    [Parameter(Mandatory = $true)] [string] $DiscoveryId,
    [Parameter(Mandatory = $true)] [string] $SelectedProfile,
    [Parameter(Mandatory = $true)] [string] $WriteProbeResult,
    [Parameter(Mandatory = $true)] [string] $TelemetryResult,
    [Parameter(Mandatory = $true)] [string] $FactoryEvaluationResult
)

$ErrorActionPreference = 'Stop'
$artifactReport = Join-Path $RepositoryRoot 'artifacts\factory-evals\codex-launch-profile-report.md'
$summaryPath = Join-Path $RepositoryRoot 'tests\Idd.Factory.LiveTests\Tests\Fixtures\codex-0.146.0-windows-launch-profile-result.md'
$profiles = @()

if (Test-Path -LiteralPath $artifactReport) {
    $discoveryDirectory = Join-Path (Join-Path $RepositoryRoot 'artifacts\factory-evals\codex-launch-profiles') $DiscoveryId
    $profiles = Get-ChildItem -LiteralPath $discoveryDirectory -Filter 'launch-profile-attempt.json' -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json } |
        Sort-Object ProfileName
}

$codexVersion = if ($profiles.Count -gt 0) { $profiles[0].CodexVersion } else { 'unavailable' }
$selectedProfileDisplay = if ([string]::IsNullOrWhiteSpace($SelectedProfile)) { 'none' } else { '`' + $SelectedProfile + '`' }
$windows = Get-CimInstance Win32_OperatingSystem
$windowsVersion = "$($windows.Caption) $($windows.Version) (build $($windows.BuildNumber))"

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Codex Windows launch-profile result')
$lines.Add('')
$lines.Add("Generated (UTC): $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss') -Format 'yyyy-MM-dd HH:mm:ss')")
$lines.Add("Discovery: ``$DiscoveryId``")
$lines.Add("Codex version: ``$codexVersion``")
$lines.Add("Windows version: $windowsVersion")
$lines.Add('')
$lines.Add('## Profiles')
$lines.Add('')
$lines.Add('| Profile | Result | Exit code | Timeout | Failure reason |')
$lines.Add('|---|---:|---:|---:|---|')
foreach ($profile in $profiles) {
    $failureReason = if ([string]::IsNullOrWhiteSpace($profile.FailureReason)) { 'none' } else { $profile.FailureReason.Replace('|', '\|') }
    $lines.Add("| ``$($profile.ProfileName)`` | $(if ($profile.Passed) { 'PASS' } else { 'FAIL' }) | $($profile.ExitCode) | $($profile.TimedOut) | $failureReason |")
}
if ($profiles.Count -eq 0) { $lines.Add('| No profile attempts were recorded | unavailable | unavailable | unavailable | unavailable |') }
$lines.Add('')
$lines.Add("Selected profile: $selectedProfileDisplay")
$lines.Add("Write probe result: $WriteProbeResult")
$lines.Add("Subagent telemetry result: $TelemetryResult")
$lines.Add("Factory evaluation result: $FactoryEvaluationResult")
$lines.Add('')
$lines.Add("Full runtime details: ``artifacts/factory-evals/codex-launch-profile-report.md`` (ignored by Git).")

[System.IO.File]::WriteAllLines($summaryPath, $lines, [System.Text.UTF8Encoding]::new($false))
