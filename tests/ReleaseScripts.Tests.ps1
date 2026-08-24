$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not locate repository root." }

function Invoke-GitChecked {
    param(
        [Parameter(Mandatory = $true)][string] $WorkingDirectory,
        [Parameter(Mandatory = $true)][string[]] $Arguments
    )

    & git -C $WorkingDirectory @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
}

function New-ReleaseFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("idd-release-script-" + [Guid]::NewGuid().ToString("N"))
    $remote = Join-Path $root "remote.git"
    $work = Join-Path $root "work"
    New-Item -ItemType Directory -Path $root | Out-Null
    & git init --bare $remote | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create test remote." }
    & git init -b main $work | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create test worktree." }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "publish-next-version.ps1") -Destination $work
    Set-Content -LiteralPath (Join-Path $work "certify-release.ps1") -Encoding utf8 -Value @'
param([Parameter(Mandatory = $true)][string] $Version)
if ($env:IDD_FACTORY_CODEX_LIFECYCLE_REPORT) { exit 23 }
if ($env:RELEASE_TEST_CERTIFY_RESULT -eq "fail") { exit 17 }
exit 0
'@
    Set-Content -LiteralPath (Join-Path $work "README.md") -Encoding utf8 -Value "release fixture"
    Invoke-GitChecked $work @("config", "user.name", "Release Script Test")
    Invoke-GitChecked $work @("config", "user.email", "release-script-test@local")
    Invoke-GitChecked $work @("add", ".")
    Invoke-GitChecked $work @("commit", "-m", "fixture")
    Invoke-GitChecked $work @("tag", "-a", "v1.0.0", "-m", "Release 1.0.0")
    Invoke-GitChecked $work @("remote", "add", "origin", $remote)
    Invoke-GitChecked $work @("push", "-u", "origin", "main")
    Invoke-GitChecked $work @("push", "origin", "v1.0.0")
    [pscustomobject]@{ Root = $root; Remote = $remote; Work = $work }
}

function Remove-ReleaseFixture {
    param([Parameter(Mandatory = $true)] $Fixture)
    $resolved = [IO.Path]::GetFullPath($Fixture.Root)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $insideTemporaryRoot = $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)
    $hasExpectedName = ([IO.Path]::GetFileName($resolved)).StartsWith("idd-release-script-", [StringComparison]::Ordinal)
    if (-not $insideTemporaryRoot -or -not $hasExpectedName) {
        throw "Refusing to remove unexpected release fixture path '$resolved'."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Assert-TagExists {
    param([string] $Repository, [string] $Tag, [bool] $Expected)
    & git -C $Repository show-ref --verify --quiet "refs/tags/$Tag"
    $actual = $LASTEXITCODE -eq 0
    if ($actual -ne $Expected) { throw "Expected tag '$Tag' existence to be $Expected in '$Repository', but it was $actual." }
}

function Invoke-PublishFixture {
    param([Parameter(Mandatory = $true)][string] $WorkingDirectory)
    Push-Location $WorkingDirectory
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File ".\publish-next-version.ps1" -Remote origin -Branch main | Out-Host
        $exitCode = $LASTEXITCODE
        return $exitCode
    }
    finally { Pop-Location }
}

$certificationText = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot "certify-release.ps1")
if ($certificationText -match "IDD_FACTORY_CODEX_LIFECYCLE_REPORT|CodexLifecycleReport") {
    throw "Release certification still contains the removed external lifecycle-report contract."
}

$previousLifecycleReport = $env:IDD_FACTORY_CODEX_LIFECYCLE_REPORT
$previousCertifyResult = $env:RELEASE_TEST_CERTIFY_RESULT
try {
    Remove-Item Env:IDD_FACTORY_CODEX_LIFECYCLE_REPORT -ErrorAction SilentlyContinue

    $success = $null
    try {
        $success = New-ReleaseFixture
        $env:RELEASE_TEST_CERTIFY_RESULT = "pass"
        $publishExitCode = Invoke-PublishFixture $success.Work
        if ($publishExitCode -ne 0) { throw "Successful publish fixture failed with exit code $publishExitCode." }
        Assert-TagExists $success.Work "v1.0.1" $true
        Assert-TagExists $success.Remote "v1.0.1" $true
    }
    finally { if ($null -ne $success) { Remove-ReleaseFixture $success } }

    $failure = $null
    try {
        $failure = New-ReleaseFixture
        $env:RELEASE_TEST_CERTIFY_RESULT = "fail"
        $publishExitCode = Invoke-PublishFixture $failure.Work
        if ($publishExitCode -eq 0) { throw "Failed certification unexpectedly published a release." }
        Assert-TagExists $failure.Work "v1.0.1" $false
        Assert-TagExists $failure.Remote "v1.0.1" $false
    }
    finally { if ($null -ne $failure) { Remove-ReleaseFixture $failure } }
}
finally {
    if ($null -eq $previousLifecycleReport) { Remove-Item Env:IDD_FACTORY_CODEX_LIFECYCLE_REPORT -ErrorAction SilentlyContinue }
    else { $env:IDD_FACTORY_CODEX_LIFECYCLE_REPORT = $previousLifecycleReport }
    if ($null -eq $previousCertifyResult) { Remove-Item Env:RELEASE_TEST_CERTIFY_RESULT -ErrorAction SilentlyContinue }
    else { $env:RELEASE_TEST_CERTIFY_RESULT = $previousCertifyResult }
}

Write-Host "Release script tests completed."
