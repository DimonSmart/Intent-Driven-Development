$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not locate repository root." }

$documents = @((Join-Path $repositoryRoot "README.md")) +
    @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "docs") -Filter "*.md" -Recurse -File | ForEach-Object FullName)

function Test-LocalTarget {
    param([string] $Document, [string] $Target)

    if ([string]::IsNullOrWhiteSpace($Target) -or $Target.StartsWith('#') -or
        $Target.StartsWith('//') -or $Target.StartsWith('/')) { return }
    if ($Target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') { return }

    $filePart = ($Target -split '[?#]', 2)[0]
    if ($filePart -match '%(?![0-9A-Fa-f]{2})') { throw "Invalid URL encoding in $Document : $Target" }
    $unescaped = [Uri]::UnescapeDataString($filePart)
    $platformPath = $unescaped.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $resolved = [IO.Path]::GetFullPath([IO.Path]::Combine([IO.Path]::GetDirectoryName($Document), $platformPath))
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $repoPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($repoPrefix, $comparison)) {
        throw "Link escapes repository in $Document : $Target"
    }
    if (-not [IO.File]::Exists($resolved)) {
        throw "Broken relative link in $Document : $Target (resolved to $resolved)"
    }
}

function Test-FragmentLinks {
    param([string] $Document, [string] $Fragment)

    # Inline Markdown links (ordinary URLs and <path with spaces>) and images.
    $inline = '!?\[[^\]\r\n]*\]\(\s*(?<target><[^>\r\n]+>|[^\s)\r\n]+)'
    foreach ($match in [regex]::Matches($Fragment, $inline)) {
        $target = $match.Groups['target'].Value.Trim('<', '>')
        Test-LocalTarget $Document $target
    }

    # Reference-style Markdown destinations, including optional title text.
    $reference = '^[ ]{0,3}\[[^\]\r\n]+\]:\s*(?<target><[^>\r\n]+>|[^\s>\r\n]+)'
    foreach ($match in [regex]::Matches($Fragment, $reference)) {
        $target = $match.Groups['target'].Value.Trim('<', '>')
        Test-LocalTarget $Document $target
    }

    # The top-level README uses an HTML hero image.
    foreach ($tag in [regex]::Matches($Fragment, '(?i)<img\b[^>]*>')) {
        $src = [regex]::Match($tag.Value, '(?i)\bsrc\s*=\s*(?<quote>["''])(?<target>.*?)\k<quote>')
        if ($src.Success) { Test-LocalTarget $Document $src.Groups['target'].Value }
    }
}

$tick = [string][char]96
$codeSpanPattern = '(' + [regex]::Escape($tick) + '+)(.*?)\1'
$fencePattern = '^[ ]{0,3}(?<marker>(?:' + [regex]::Escape($tick) + '){3,}|~{3,})'

foreach ($document in $documents) {
    $text = [IO.File]::ReadAllText($document)
    $lines = [regex]::Split([regex]::Replace($text, '\r\n?', [string][char]10), '(?<=\n)')
    $fenceChar = ''
    $fenceLength = 0
    foreach ($line in $lines) {
        $trimmed = $line.TrimEnd([char]13, [char]10)
        if ($fenceLength -gt 0) {
            $ending = '^[ ]{0,3}' + [regex]::Escape($fenceChar) + '{' + $fenceLength + ',}[ \t]*$'
            if ([regex]::IsMatch($trimmed, $ending)) {
                $fenceLength = 0
                $fenceChar = ''
            }
            continue
        }

        $fence = [regex]::Match($trimmed, $fencePattern)
        if ($fence.Success) {
            $marker = $fence.Groups['marker'].Value
            $fenceChar = $marker.Substring(0, 1)
            $fenceLength = $marker.Length
            continue
        }

        $position = 0
        foreach ($span in [regex]::Matches($line, $codeSpanPattern)) {
            Test-FragmentLinks -Document $document -Fragment ($line.Substring($position, $span.Index - $position))
            $position = $span.Index + $span.Length
        }
        Test-FragmentLinks -Document $document -Fragment ($line.Substring($position))
    }

    if ($text -match 'factory-benchmarking\.md' -or
        $text -match '(?<![/\\\w])(?:\./)?run-live-factory-evals\.(?:bat|zsh)' -or
        $text -match 'run-live-intent-evals\.bat') {
        throw "Stale documentation link or launcher path in $document"
    }
}

$zsh = Join-Path $repositoryRoot "scripts/evals/run-live-factory-evals.zsh"
$bat = Join-Path $repositoryRoot "scripts/evals/run-live-factory-evals.bat"
foreach ($file in @($zsh, $bat)) {
    if (-not [IO.File]::Exists($file)) { throw "Missing Factory launcher: $file" }
}
foreach ($removed in @("run-live-factory-evals.zsh", "run-live-factory-evals.bat",
                       "run-live-intent-evals.bat", "docs/factory-benchmarking.md")) {
    if ([IO.File]::Exists((Join-Path $repositoryRoot $removed))) {
        throw "Obsolete tracked path is still present: $removed"
    }
}

Write-Host "Documentation link tests completed ($($documents.Count) Markdown files)."
