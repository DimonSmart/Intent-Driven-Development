<#
.SYNOPSIS
Install the published IDD plugins from the GitHub Codex marketplace.

.DESCRIPTION
Replace any installed local IDD plugins with the published GitHub marketplace
version. A previously registered local marketplace named
'intent-driven-development' is removed and re-registered from GitHub.
Other marketplace registrations, including 'idd-local-test', are preserved.

.EXAMPLE
pwsh ./scripts/Install-Idd-Codex-From-GitHub.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$marketplaceName = 'intent-driven-development'
$repository = 'DimonSmart/Intent-Driven-Development'
$pluginNames = @('idd-factory', 'idd-intent')

function Invoke-Codex {
    param([Parameter(Mandatory)][string[]] $Arguments)

    Write-Host "> codex $($Arguments -join ' ')"
    & codex @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Codex failed (exit $LASTEXITCODE): codex $($Arguments -join ' ')"
    }
}

function Read-CodexJson {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = @(& codex @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not query Codex (exit $LASTEXITCODE): codex $($Arguments -join ' ')"
    }
    $json = ($output -join [Environment]::NewLine).Trim()
    if (-not $json) {
        throw "Codex returned empty JSON: codex $($Arguments -join ' ')"
    }
    return ($json | ConvertFrom-Json)
}

try {
    if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
        throw "'codex' was not found in PATH."
    }

    $plugins = Read-CodexJson -Arguments @('plugin', 'list', '--json')
    $marketplaces = Read-CodexJson -Arguments @('plugin', 'marketplace', 'list', '--json')
    if ($null -eq $plugins.installed -or $null -eq $marketplaces.marketplaces) {
        throw 'Unexpected Codex plugin or marketplace list JSON.'
    }

    $installedIdd = @($plugins.installed | Where-Object {
        $null -ne $_ -and $_.name -in $pluginNames
    })
    foreach ($plugin in $installedIdd) {
        if ([string]::IsNullOrWhiteSpace([string] $plugin.pluginId)) {
            throw "Cannot safely uninstall '$($plugin.name)': missing pluginId."
        }
    }

    # Uninstall the old IDD packages before replacing their marketplace source.
    foreach ($name in $pluginNames) {
        foreach ($plugin in @($installedIdd | Where-Object { $_.name -eq $name })) {
            Invoke-Codex -Arguments @('plugin', 'remove', $plugin.pluginId)
        }
    }

    # Always re-register the published source, even when the old name was local.
    $registered = @($marketplaces.marketplaces | Where-Object {
        $null -ne $_ -and $_.name -eq $marketplaceName
    })
    if ($registered.Count -gt 0) {
        Invoke-Codex -Arguments @('plugin', 'marketplace', 'remove', $marketplaceName)
    }

    Invoke-Codex -Arguments @('plugin', 'marketplace', 'add', $repository, '--ref', 'marketplace')
    Invoke-Codex -Arguments @('plugin', 'add', "idd-intent@$marketplaceName")
    Invoke-Codex -Arguments @('plugin', 'add', "idd-factory@$marketplaceName")

    $verified = Read-CodexJson -Arguments @('plugin', 'list', '--json')
    $registered = Read-CodexJson -Arguments @('plugin', 'marketplace', 'list', '--json')
    $published = @($registered.marketplaces | Where-Object {
        $null -ne $_ -and $_.name -eq $marketplaceName
    })
    if ($published.Count -ne 1 -or $published[0].marketplaceSource.source -eq 'local') {
        throw "Marketplace '$marketplaceName' is not confirmed as a non-local source."
    }
    foreach ($name in $pluginNames) {
        $matches = @($verified.installed | Where-Object {
            $null -ne $_ -and $_.name -eq $name -and $_.marketplaceName -eq $marketplaceName
        })
        if ($matches.Count -ne 1) {
            throw "Installation could not be verified: $name@$marketplaceName"
        }
    }

    Write-Host 'IDD Intent and Factory are installed from the published GitHub marketplace.'
    Write-Host 'Start a new Codex session to load the updated skills.'
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
