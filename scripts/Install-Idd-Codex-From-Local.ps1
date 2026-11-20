<#
.SYNOPSIS
Install development IDD plugins built from the current local checkout.

.DESCRIPTION
Generate the Codex marketplace from local source and install both IDD Intent
and Factory from the isolated 'idd-local-test' marketplace. Changes in the
working tree are included in the generated package. The published marketplace
registration is preserved.

.EXAMPLE
pwsh ./scripts/Install-Idd-Codex-From-Local.ps1

.EXAMPLE
pwsh ./scripts/Install-Idd-Codex-From-Local.ps1 -RepoRoot C:\Source\Intent-Driven-Development
#>
[CmdletBinding()]
param(
    [string] $RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
}

$installer = Join-Path $PSScriptRoot 'Switch-Idd-Codex.ps1'
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Missing local IDD installer: $installer"
}

& $installer -Source Local -RepoRoot $RepoRoot -WithFactory
if ($LASTEXITCODE -ne 0) {
    throw "Local IDD installation failed (exit $LASTEXITCODE)."
}
