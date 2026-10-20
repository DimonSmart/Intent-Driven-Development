$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not locate repository root." }
$generator = Join-Path $repositoryRoot "scripts/Build-MarketplaceReadme.ps1"
$workflow = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot ".github/workflows/publish-marketplace.yml")

function Assert-Contains {
    param([string] $Text, [string] $Expected)
    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "Expected output to contain: $Expected"
    }
}

function Invoke-Generation {
    param([string] $Source, [string] $Output, [string] $Version = "1.2.3")
    $messages = @(& pwsh -NoProfile -File $generator -SourceReadme $Source -OutputReadme $Output -Version $Version 2>&1)
    return [pscustomobject]@{ Code = $LASTEXITCODE; Message = ($messages -join [Environment]::NewLine) }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ("idd-marketplace-readme-test-" + [Guid]::NewGuid().ToString("N"))
try {
    $docs = Join-Path $root "docs"
    [void][IO.Directory]::CreateDirectory($docs)
    [void][IO.File]::WriteAllText((Join-Path $docs "guide.md"), "# Guide")
    [void][IO.File]::WriteAllText((Join-Path $docs "file name.md"), "# Spaces")
    [void][IO.File]::WriteAllText((Join-Path $docs "picture.png"), "fixture")
    [void][IO.File]::WriteAllText((Join-Path $root "other.md"), "# Other")

    $tick = [string][char]96
    $fixture = @(
        "[Use](docs/guide.md#section)"
        "[Query](docs/guide.md?view=full#section)"
        '[Spaces](<./docs/file name.md?x=1#part> "Example")'
        "[Encoded](docs/file%20name.md)"
        "![An image](docs/picture.png)"
        '<img width="20" src="docs/picture.png" alt="Picture" />'
        "[Reference][guide]"
        "[guide]: docs/guide.md"
        "[External](https://example.org/docs/guide.md)"
        "[Anchor](#section)"
        "[Other](other.md)"
        "$tick[inline](docs/missing.md)$tick"
        "$tick$tick$tick" + "markdown"
        "[fenced](docs/missing.md)"
        "$tick$tick$tick"
    ) -join [Environment]::NewLine
    $source = Join-Path $root "README.md"
    $output = Join-Path $root "marketplace/README.md"
    [IO.File]::WriteAllText($source, $fixture, [Text.UTF8Encoding]::new($false))
    $before = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash

    $run = Invoke-Generation $source $output
    if ($run.Code -ne 0) { throw "Fixture generation failed: $($run.Message)" }
    $result = [IO.File]::ReadAllText($output)
    Assert-Contains $result "blob/v1.2.3/docs/guide.md#section"
    Assert-Contains $result "blob/v1.2.3/docs/guide.md?view=full#section"
    Assert-Contains $result "blob/v1.2.3/docs/file%20name.md?x=1#part"
    Assert-Contains $result "raw.githubusercontent.com/DimonSmart/Intent-Driven-Development/v1.2.3/docs/picture.png"
    Assert-Contains $result '[External](https://example.org/docs/guide.md)'
    Assert-Contains $result '[Anchor](#section)'
    Assert-Contains $result '[Other](other.md)'
    Assert-Contains $result "$tick[inline](docs/missing.md)$tick"
    Assert-Contains $result "[fenced](docs/missing.md)"
    Assert-Contains $result 'alt="Picture"'
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $before) {
        throw "Generation modified its source README."
    }
    $outputBytes = [IO.File]::ReadAllBytes($output)
    $run = Invoke-Generation $source $output
    if ($run.Code -ne 0) { throw "Repeated generation failed: $($run.Message)" }
    if (-not ([Convert]::ToBase64String($outputBytes) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($output)))) {
        throw "Repeated generation was not byte-identical."
    }

    $badCases = @(
        @{ Version = "v1.2.3"; Source = $source; Content = $fixture }
        @{ Version = "1.2"; Source = $source; Content = $fixture }
        @{ Version = "1.2.3"; Source = (Join-Path $root "missing.md"); Content = $fixture }
        @{ Version = "1.2.3"; Source = $source; Content = "[Broken](docs/missing.md)" }
        @{ Version = "1.2.3"; Source = $source; Content = "[Escape](docs/../../outside.md)" }
        @{ Version = "1.2.3"; Source = $source; Content = '<a href="docs/guide.md">unsupported</a>' }
    )
    foreach ($test in $badCases) {
        [IO.File]::WriteAllText($source, $test.Content)
        $failedOutput = Join-Path $root "marketplace/failed.md"
        if ([IO.File]::Exists($failedOutput)) { [IO.File]::Delete($failedOutput) }
        $run = Invoke-Generation $test.Source $failedOutput $test.Version
        if ($run.Code -eq 0) { throw "Invalid generation unexpectedly succeeded: $($test.Content)" }
        if ([IO.File]::Exists($failedOutput)) { throw "Failed generation left a publishable output." }
        if ([string]::IsNullOrWhiteSpace($run.Message)) { throw "Failed generation lacked diagnostics." }
    }

    $realSource = Join-Path $repositoryRoot "README.md"
    $realOutput = Join-Path $root "real/README.md"
    $realRun = Invoke-Generation $realSource $realOutput
    if ($realRun.Code -ne 0) { throw "Real README generation failed: $($realRun.Message)" }
    $realResult = [IO.File]::ReadAllText($realOutput)
    Assert-Contains $realResult "blob/v1.2.3/docs/using-idd.md"
    Assert-Contains $realResult "raw.githubusercontent.com/DimonSmart/Intent-Driven-Development/v1.2.3/docs/assets/idd-thought-experiment.png"

    $buildIndex = $workflow.IndexOf("name: Build publish root", [StringComparison]::Ordinal)
    $generateIndex = $workflow.IndexOf("name: Generate marketplace README", [StringComparison]::Ordinal)
    $publishIndex = $workflow.IndexOf("name: Publish marketplace branch", [StringComparison]::Ordinal)
    $releaseIndex = $workflow.IndexOf("name: Create GitHub Release", [StringComparison]::Ordinal)
    if ($buildIndex -lt 0 -or $generateIndex -le $buildIndex -or
        $publishIndex -le $generateIndex -or $releaseIndex -le $publishIndex) {
        throw "Marketplace README generation must precede publication and release."
    }
    Assert-Contains $workflow "steps.version.outputs.VERSION"
    Assert-Contains $workflow "scripts/Build-MarketplaceReadme.ps1"
    if ($workflow -match '(?m)^\s*cp\s+README\.md\b') { throw "Publish workflow still copies the unmodified README." }
    if ($workflow -match '(?m)^\s*cp\s+.*\bdocs/') { throw "Publish workflow copies maintainer docs." }
}
finally {
    if ([IO.Directory]::Exists($root)) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

Write-Host "Marketplace README tests completed."
