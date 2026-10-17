<#
.SYNOPSIS
Switch the active Codex IDD plugin between the published marketplace and
the generated marketplace from this local source checkout.

.EXAMPLE
pwsh ./scripts/Switch-Idd-Codex.ps1 -Source Local

.EXAMPLE
pwsh ./scripts/Switch-Idd-Codex.ps1 -Source Published

.EXAMPLE
pwsh ./scripts/Switch-Idd-Codex.ps1 -Source Local -WithFactory
#>
[CmdletBinding()]
param(
    [ValidateSet('Published', 'Local')]
    [string] $Source,
    [string] $RepoRoot,
    [switch] $WithFactory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$publishedMarketplace = 'intent-driven-development'
$localMarketplace = 'idd-local-test'
$managedMarketplaces = @($publishedMarketplace, $localMarketplace)
$publishedRepository = 'DimonSmart/Intent-Driven-Development'

function Invoke-Codex {
    param([Parameter(Mandatory)][string[]] $Arguments)

    Write-Host "> codex $($Arguments -join ' ')" -ForegroundColor DarkGray
    & codex @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Codex failed (exit $LASTEXITCODE): codex $($Arguments -join ' ')"
    }
}

function Read-CodexJson {
    param([Parameter(Mandatory)][string[]] $Arguments)
    $output = @(& codex @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not query Codex state (exit $LASTEXITCODE): codex $($Arguments -join ' ')"
    }
    $json = ($output -join [Environment]::NewLine).Trim()
    if (-not $json) {
        throw "Codex returned empty JSON: codex $($Arguments -join ' ')"
    }
    return ($json | ConvertFrom-Json)
}

function Get-InstalledIdd {
    $data = Read-CodexJson @('plugin', 'list', '--json')
    if ($null -eq $data.installed) {
        throw "Unexpected Codex plugin list JSON: missing 'installed'."
    }

    $all = @($data.installed | Where-Object { $null -ne $_ })
    $idd = @($all | Where-Object { $_.name -in @('idd-intent', 'idd-factory') })
    $unmanaged = @($idd | Where-Object { $_.marketplaceName -notin $managedMarketplaces })
    if ($unmanaged.Count -gt 0) {
        $names = ($unmanaged | ForEach-Object { $_.pluginId }) -join ', '
        throw "IDD is installed from an unmanaged marketplace: $names. Resolve this conflict manually."
    }
    return $idd
}

function Get-Marketplace {
    param([string] $Name)
    $list = Read-CodexJson @('plugin', 'marketplace', 'list', '--json')
    if ($null -eq $list.marketplaces) {
        throw "Unexpected Codex marketplace JSON: missing 'marketplaces'."
    }
    return @($list.marketplaces | Where-Object { $null -ne $_ -and $_.name -eq $Name } | Select-Object -First 1)
}

function Prepare-Published {
    $existing = @(Get-Marketplace $publishedMarketplace)
    if ($existing.Count -eq 0) {
        Invoke-Codex @('plugin', 'marketplace', 'add', $publishedRepository, '--ref', 'marketplace')
        return
    }

    $market = $existing[0]
    if ($null -ne $market.marketplaceSource -and $market.marketplaceSource.source -eq 'local') {
        throw "Marketplace '$publishedMarketplace' points to a local folder; refusing to silently overwrite it."
    }
    Invoke-Codex @('plugin', 'marketplace', 'upgrade', $publishedMarketplace)
}

function Prepare-Local {
    param([Parameter(Mandatory)][string] $Root)

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET 10 SDK is needed to generate the local plugin."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $Root 'tools/generate/Generate.csproj') -PathType Leaf)) {
        throw "Not an IDD source checkout: $Root"
    }

    Push-Location $Root
    try {
        Write-Host "Generating Codex marketplace from local source..." -ForegroundColor Cyan
        & dotnet run --project 'tools/generate/Generate.csproj' -- --version '0.0.0'
        if ($LASTEXITCODE -ne 0) {
            throw "Local plugin generation failed. Existing installation has not been touched."
        }
    }
    finally {
        Pop-Location
    }

    $artifacts = Join-Path $Root 'artifacts'
    $generated = Join-Path $artifacts 'marketplace'
    $catalogRel = '.agents/plugins/marketplace.json'
    $catalog = Join-Path $generated $catalogRel
    if (-not (Test-Path -LiteralPath $catalog -PathType Leaf)) {
        throw "Generator did not create $catalog"
    }

    # Keep the generator output pristine. Create a private local marketplace
    # with a DIFFERENT name so it can coexist with the published source.
    $target = Join-Path $artifacts 'marketplace-local-test'
    $stage = Join-Path $artifacts 'marketplace-local-test-stage'
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }

    Copy-Item -LiteralPath $generated -Destination $stage -Recurse -Force
    $catalogPath = Join-Path $stage $catalogRel
    $manifest = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    if ($manifest.name -ne $publishedMarketplace) {
        throw "Unexpected generated marketplace name: $($manifest.name)"
    }
    $manifest.name = $localMarketplace
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $catalogPath -Encoding utf8

    # Never point an existing local marketplace at an unrelated directory.
    $existing = @(Get-Marketplace $localMarketplace)
    if ($existing.Count -gt 0) {
        $configuredRoot = [IO.Path]::GetFullPath([string] $existing[0].root)
        $expectedRoot = [IO.Path]::GetFullPath($target)
        if (-not [string]::Equals($configuredRoot.TrimEnd('\', '/'),
                                 $expectedRoot.TrimEnd('\', '/'),
                                 [StringComparison]::OrdinalIgnoreCase)) {
            throw "Marketplace '$localMarketplace' is already registered elsewhere: $configuredRoot"
        }
    }

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    Move-Item -LiteralPath $stage -Destination $target

    if ($existing.Count -eq 0) {
        Invoke-Codex @('plugin', 'marketplace', 'add', $target)
    }
}

try {
    if (-not $Source) {
        Write-Host 'Switch Codex IDD plugin:'
        Write-Host '  1. Published (GitHub marketplace branch)'
        Write-Host '  2. Local (this IDD source checkout)'
        $answer = Read-Host 'Select 1 or 2'
        $Source = switch ($answer) {
            '1' { 'Published' }
            '2' { 'Local' }
            default { throw "Invalid selection: $answer" }
        }
    }

    if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
        throw "'codex' was not found in PATH. Install/update the Codex CLI first."
    }
    if (-not $RepoRoot) {
        $RepoRoot = Join-Path $PSScriptRoot '..'
    }
    $RepoRoot = [IO.Path]::GetFullPath($RepoRoot)

    Write-Host "Switching Codex IDD to $Source" -ForegroundColor Cyan
    $previous = @(Get-InstalledIdd)
    $needFactory = $WithFactory.IsPresent -or
        (@($previous | Where-Object { $_.name -eq 'idd-factory' }).Count -gt 0)

    # Prepare the replacement before uninstalling the currently active IDD.
    if ($Source -eq 'Local') {
        Prepare-Local $RepoRoot
        $destination = $localMarketplace
    }
    else {
        Prepare-Published
        $destination = $publishedMarketplace
    }

    # Uninstall Factory first, then Intent. Always reinstall the selected
    # source, even if already selected, to refresh the Codex plugin cache.
    foreach ($name in @('idd-factory', 'idd-intent')) {
        foreach ($entry in @($previous | Where-Object { $_.name -eq $name })) {
            Invoke-Codex @('plugin', 'remove', $entry.pluginId)
        }
    }

    Invoke-Codex @('plugin', 'add', "idd-intent@$destination")
    if ($needFactory) {
        Invoke-Codex @('plugin', 'add', "idd-factory@$destination")
    }

    $current = @(Get-InstalledIdd)
    $intent = @($current | Where-Object {
        $_.name -eq 'idd-intent' -and $_.marketplaceName -eq $destination
    })
    if ($intent.Count -ne 1) {
        throw "Installation finished, but installed 'idd-intent@$destination' could not be verified."
    }

    Write-Host ''
    Write-Host "Active IDD: idd-intent@$destination" -ForegroundColor Green
    if ($needFactory) {
        Write-Host "Active Factory: idd-factory@$destination" -ForegroundColor Green
    }
    Write-Host 'Restart Codex (start a new session) to load the selected skills.'
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    Write-Host 'Check the active installation with: codex plugin list --json'
    exit 1
}
