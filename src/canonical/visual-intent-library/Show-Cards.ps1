<#
.SYNOPSIS
Display existing Visual Intent comparison cards in a local HTML gallery.

.EXAMPLE
pwsh ./Show-Cards.ps1 -Cards V09,V15,V30

.EXAMPLE
pwsh ./Show-Cards.ps1 -Cards V09,V15 -NoOpen
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $Cards,
    [string] $LibraryRoot = $PSScriptRoot,
    [switch] $NoOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($LibraryRoot)
$imagesRoot = Join-Path $root 'Images'
$catalogPath = Join-Path $root 'VISUAL_INTENT_CATALOG.md'
if (-not (Test-Path -LiteralPath $imagesRoot -PathType Container)) {
    throw "Visual Intent image directory not found: $imagesRoot"
}
if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
    throw "Visual Intent catalog not found: $catalogPath"
}

$catalogLines = Get-Content -LiteralPath $catalogPath -Encoding UTF8
$allImages = @(Get-ChildItem -LiteralPath $imagesRoot -File | Where-Object {
    $_.Extension -in @('.png', '.jpg', '.jpeg', '.webp')
})

function Encode-Html([string] $Value) {
    return [System.Net.WebUtility]::HtmlEncode($Value)
}

$blocks = [System.Collections.Generic.List[string]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($inputId in @($Cards | ForEach-Object { $_ -split ',' })) {
    $cardId = $inputId.Trim().ToUpperInvariant()
    if ($cardId -notmatch '^V?(\d{1,2})$') {
        throw "Invalid card ID '$inputId'. Examples: V09 or 09."
    }
    $number = [int] $Matches[1]
    if ($number -lt 1 -or $number -gt 50) {
        throw "Card number must be between 1 and 50: $inputId"
    }
    $id = 'V{0:D2}' -f $number
    if (-not $seen.Add($id)) { continue }
    $numericName = '{0:D2}' -f $number
    $exact = @($allImages | Where-Object {
        $_.BaseName -ieq $numericName -or $_.BaseName -ieq $id
    })
    if ($exact.Count -gt 1) {
        throw "More than one exact image for $id in $imagesRoot"
    }
    $matchesForId = @(if ($exact.Count -eq 1) {
        $exact
    }
    else {
        $allImages | Where-Object {
            $_.BaseName -match ('^' + [regex]::Escape($id) + '(?:[-_.].+)
    if ($matchesForId.Count -ne 1) {
        throw "Expected one image for $id; found $($matchesForId.Count). Check $imagesRoot"
    }
    $image = $matchesForId[0]

    $title = $id
    $question = 'Which visual treatment is closest to what you want, if any?'
    foreach ($line in $catalogLines) {
        if ($line -match ('^\|\s*\*\*' + [regex]::Escape($id) + '\s+[—–-]\s+(.+?)\*\*')) {
            $title = "$id — $($Matches[1])"
            $columns = @($line -split '\|')
            if ($columns.Count -ge 4) {
                $catalogQuestion = $columns[3].Trim()
                if ($catalogQuestion) { $question = $catalogQuestion }
            }
            break
        }
    }

    $imgUrl = ([Uri]::new($image.FullName)).AbsoluteUri
    $safeTitle = Encode-Html $title
    $safeQuestion = Encode-Html $question
    $safeImage = Encode-Html $imgUrl
    $blocks.Add(@"
<section class="comparison">
  <h2>$safeTitle</h2>
  <a href="$safeImage" title="Open full-size $id" target="_blank">
    <img src="$safeImage" alt="$safeTitle" loading="eager">
  </a>
  <p class="question">$safeQuestion</p>
</section>
"@)
}

if ($blocks.Count -eq 0) { throw 'No visual cards selected.' }

$sections = [string]::Join([Environment]::NewLine, $blocks)
$html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Visual Intent comparison cards</title>
<style>
:root { color-scheme: light dark; font-family: system-ui, sans-serif; }
body { margin: 0 auto; padding: 24px; max-width: 1280px; }
header { margin-bottom: 24px; }
h1 { margin: 0 0 8px; font-size: 24px; }
h2 { margin: 0 0 12px; font-size: 18px; }
header p { margin: 0; color: #6a7079; }
.grid { display: grid; grid-template-columns: repeat(auto-fit,minmax(min(100%,480px),1fr)); gap: 24px; }
.comparison { border: 1px solid #9996; border-radius: 10px; padding: 16px; min-width: 0; }
.comparison img { display:block; width:100%; max-height:720px; object-fit:contain; border-radius: 4px; }
.comparison a { display: block; cursor: zoom-in; }
.question { font-size: 16px; line-height: 1.5; font-weight: 600; }
@media (max-width: 600px) { body { padding: 12px; } .grid { display: block; } .comparison { margin-bottom: 16px; } }
</style>
</head>
<body>
<header>
<h1>Visual Intent comparison cards</h1>
<p>Compare the real images, then answer the corresponding questions in Codex. Click an image to see it full-size.</p>
</header>
<main class="grid">
$sections
</main>
</body>
</html>
"@

$galleryDir = Join-Path ([IO.Path]::GetTempPath()) 'idd-visual-intent'
[void] [IO.Directory]::CreateDirectory($galleryDir)
$output = Join-Path $galleryDir ("cards-" + [Guid]::NewGuid().ToString('N') + '.html')
[IO.File]::WriteAllText($output, $html, [Text.UTF8Encoding]::new($false))
Write-Output $output

if (-not $NoOpen) {
    try {
        if ($IsWindows) {
            Start-Process -FilePath $output | Out-Null
        }
        elseif ($IsMacOS) {
            & open $output
        }
        elseif (Get-Command xdg-open -ErrorAction SilentlyContinue) {
            & xdg-open $output | Out-Null
        }
        else {
            Write-Warning 'No supported browser opener; open the HTML path manually.'
        }
    }
    catch {
        Write-Warning "Unable to open browser; open the HTML path manually: $($_.Exception.Message)"
    }
}
)
        }
    })
    if ($matchesForId.Count -ne 1) {
        throw "Expected one image for $id; found $($matchesForId.Count). Check $imagesRoot"
    }
    $image = $matchesForId[0]

    $title = $id
    $question = 'Which visual treatment is closest to what you want, if any?'
    foreach ($line in $catalogLines) {
        if ($line -match ('^\|\s*\*\*' + [regex]::Escape($id) + '\s+[—–-]\s+(.+?)\*\*')) {
            $title = "$id — $($Matches[1])"
            $columns = @($line -split '\|')
            if ($columns.Count -ge 4) {
                $catalogQuestion = $columns[3].Trim()
                if ($catalogQuestion) { $question = $catalogQuestion }
            }
            break
        }
    }

    $imgUrl = ([Uri]::new($image.FullName)).AbsoluteUri
    $safeTitle = Encode-Html $title
    $safeQuestion = Encode-Html $question
    $safeImage = Encode-Html $imgUrl
    $blocks.Add(@"
<section class="comparison">
  <h2>$safeTitle</h2>
  <a href="$safeImage" title="Open full-size $id" target="_blank">
    <img src="$safeImage" alt="$safeTitle" loading="eager">
  </a>
  <p class="question">$safeQuestion</p>
</section>
"@)
}

if ($blocks.Count -eq 0) { throw 'No visual cards selected.' }

$sections = [string]::Join([Environment]::NewLine, $blocks)
$html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Visual Intent comparison cards</title>
<style>
:root { color-scheme: light dark; font-family: system-ui, sans-serif; }
body { margin: 0 auto; padding: 24px; max-width: 1280px; }
header { margin-bottom: 24px; }
h1 { margin: 0 0 8px; font-size: 24px; }
h2 { margin: 0 0 12px; font-size: 18px; }
header p { margin: 0; color: #6a7079; }
.grid { display: grid; grid-template-columns: repeat(auto-fit,minmax(min(100%,480px),1fr)); gap: 24px; }
.comparison { border: 1px solid #9996; border-radius: 10px; padding: 16px; min-width: 0; }
.comparison img { display:block; width:100%; max-height:720px; object-fit:contain; border-radius: 4px; }
.comparison a { display: block; cursor: zoom-in; }
.question { font-size: 16px; line-height: 1.5; font-weight: 600; }
@media (max-width: 600px) { body { padding: 12px; } .grid { display: block; } .comparison { margin-bottom: 16px; } }
</style>
</head>
<body>
<header>
<h1>Visual Intent comparison cards</h1>
<p>Compare the real images, then answer the corresponding questions in Codex. Click an image to see it full-size.</p>
</header>
<main class="grid">
$sections
</main>
</body>
</html>
"@

$galleryDir = Join-Path ([IO.Path]::GetTempPath()) 'idd-visual-intent'
[void] [IO.Directory]::CreateDirectory($galleryDir)
$output = Join-Path $galleryDir ("cards-" + [Guid]::NewGuid().ToString('N') + '.html')
[IO.File]::WriteAllText($output, $html, [Text.UTF8Encoding]::new($false))
Write-Output $output

if (-not $NoOpen) {
    try {
        if ($IsWindows) {
            Start-Process -FilePath $output | Out-Null
        }
        elseif ($IsMacOS) {
            & open $output
        }
        elseif (Get-Command xdg-open -ErrorAction SilentlyContinue) {
            & xdg-open $output | Out-Null
        }
        else {
            Write-Warning 'No supported browser opener; open the HTML path manually.'
        }
    }
    catch {
        Write-Warning "Unable to open browser; open the HTML path manually: $($_.Exception.Message)"
    }
}
