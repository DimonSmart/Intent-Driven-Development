$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script = Join-Path $repoRoot 'scripts/Switch-Idd-Codex.ps1'
$batch = Join-Path $repoRoot 'scripts/Switch-Idd-Codex.bat'

if (-not (Test-Path -LiteralPath $script -PathType Leaf)) {
    throw "Missing PowerShell switcher: $script"
}
if (-not (Test-Path -LiteralPath $batch -PathType Leaf)) {
    throw "Missing BAT switcher: $batch"
}

$tokens = $null
$errors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    $script, [ref] $tokens, [ref] $errors)
if (@($errors).Count -gt 0) {
    $detail = @($errors | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    throw "PowerShell syntax errors in Codex switcher:$([Environment]::NewLine)$detail"
}

$psText = Get-Content -LiteralPath $script -Raw
$batText = Get-Content -LiteralPath $batch -Raw
foreach ($phrase in @(
    "'Published', 'Local'",
    'DimonSmart/Intent-Driven-Development',
    "idd-local-test",
    "plugin', 'marketplace', 'upgrade'",
    "plugin', 'marketplace', 'add'",
    "plugin', 'remove'",
    "plugin', 'list', '--json'",
    "tools/generate/Generate.csproj",
    "marketplace-local-test",
    "idd-factory"
)) {
    if (-not $psText.Contains($phrase)) {
        throw "Codex switcher must contain: $phrase"
    }
}
if (-not $batText.Contains('Switch-Idd-Codex.ps1')) {
    throw 'BAT launcher does not call the PowerShell switcher.'
}
Write-Host 'Codex plugin switcher syntax and contracts: OK'
