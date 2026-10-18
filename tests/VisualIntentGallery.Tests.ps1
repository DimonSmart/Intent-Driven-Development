$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $root 'src/canonical/visual-intent-library'
$script = Join-Path $source 'Show-Cards.ps1'

if (-not (Test-Path -LiteralPath $script -PathType Leaf)) {
    throw "Missing visual card gallery script: $script"
}

$tokens = $null
$parseErrors = $null
[void] [System.Management.Automation.Language.Parser]::ParseFile(
    $script, [ref] $tokens, [ref] $parseErrors)
if (@($parseErrors).Count -ne 0) {
    throw "Visual card gallery has PowerShell syntax errors: $($parseErrors -join '; ')"
}

foreach ($sample in @(
    @{ Cards = @('V09', 'V15'); Ids = @('V09','V15') },
    @{ Cards = @('09,15'); Ids = @('V09','V15') }
)) {
    $output = & $script -Cards $sample.Cards -NoOpen
    $galleryPath = [string] ($output | Select-Object -Last 1)
    if (-not (Test-Path -LiteralPath $galleryPath -PathType Leaf)) {
        throw "Gallery was not created: $galleryPath"
    }
    try {
        $html = Get-Content -LiteralPath $galleryPath -Raw -Encoding UTF8
        if (-not $html.Contains('Visual Intent comparison cards')) {
            throw 'Gallery missing heading.'
        }
        foreach ($id in $sample.Ids) {
            $numeric = $id.Substring(1)
            if (-not $html.Contains($id)) { throw "Gallery missing card label $id" }
            if (-not $html.Contains("/Images/$numeric.png")) {
                throw "Gallery missing actual image /Images/$numeric.png"
            }
        }
        $count = [regex]::Matches($html, '<section class="comparison">').Count
        if ($count -ne $sample.Ids.Count) {
            throw "Expected $($sample.Ids.Count) cards in gallery, found $count"
        }
    }
    finally {
        Remove-Item -LiteralPath $galleryPath -Force
    }
}

$rejected = $false
try {
    & $script -Cards 'V99' -NoOpen | Out-Null
}
catch {
    $rejected = $true
}
if (-not $rejected) { throw 'Out-of-range card IDs must be rejected.' }

Write-Host 'Visual Intent gallery generation and image mapping: OK'
