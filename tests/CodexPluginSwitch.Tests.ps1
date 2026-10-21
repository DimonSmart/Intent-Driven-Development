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

# PowerShell syntax checking does not parse CMD batches. Exercise the BAT
# launcher in a real Windows cmd.exe process with a fake pwsh executable.
# This prevents regressions caused by an unescaped closing parenthesis.
if ($IsWindows) {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("idd-codex-bat-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temp | Out-Null
    $originalPath = $env:PATH
    $originalLog = [Environment]::GetEnvironmentVariable('IDD_SWITCH_TEST_ARGS', 'Process')
    try {
        $fakePwsh = Join-Path $temp 'pwsh.cmd'
        Set-Content -LiteralPath $fakePwsh -Encoding ascii -Value @(
            '@echo off'
            'echo %* > "%IDD_SWITCH_TEST_ARGS%"'
            'exit /b 0'
        )
        $env:PATH = "$temp;$originalPath"
        $env:IDD_SWITCH_TEST_ARGS = Join-Path $temp 'args.txt'
        foreach ($source in @('local', 'published')) {
            foreach ($suffix in @('', ' -WithFactory')) {
                if (Test-Path -LiteralPath $env:IDD_SWITCH_TEST_ARGS) {
                    Remove-Item -LiteralPath $env:IDD_SWITCH_TEST_ARGS
                }
                & $env:ComSpec /D /C ('"' + $batch + '" ' + $source + $suffix) | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "BAT launcher failed for '$source$suffix' (exit $LASTEXITCODE)."
                }
                if (-not (Test-Path -LiteralPath $env:IDD_SWITCH_TEST_ARGS)) {
                    throw "BAT launcher did not invoke fake pwsh for '$source$suffix'."
                }
                $arguments = Get-Content -LiteralPath $env:IDD_SWITCH_TEST_ARGS -Raw
                $explicitSource = '-Source "' + $source + '"'
                $positionalSource = '(?i)(?:^|\s)' + [regex]::Escape($source) + '(?:\s|$)'
                if (-not $arguments.Contains($explicitSource) -and
                    $arguments -notmatch $positionalSource) {
                    throw "BAT launcher forwarded wrong source for '$source$suffix': $arguments"
                }
                if ($suffix -and -not $arguments.Contains('-WithFactory')) {
                    throw "BAT launcher dropped -WithFactory for '$source': $arguments"
                }
            }
        }
    }
    finally {
        $env:PATH = $originalPath
        [Environment]::SetEnvironmentVariable('IDD_SWITCH_TEST_ARGS', $originalLog, 'Process')
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}

Write-Host 'Codex plugin switcher syntax and contracts: OK'
