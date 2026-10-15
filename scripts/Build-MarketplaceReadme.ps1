[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $SourceReadme,
    [Parameter(Mandatory = $true)][string] $OutputReadme,
    [Parameter(Mandatory = $true)][string] $Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Version must have MAJOR.MINOR.PATCH format (without a v prefix): '$Version'."
}

$sourcePath = [IO.Path]::GetFullPath($SourceReadme)
$outputPath = [IO.Path]::GetFullPath($OutputReadme)
if ([string]::Equals($sourcePath, $outputPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputReadme must differ from SourceReadme."
}
if (-not [IO.File]::Exists($sourcePath)) {
    throw "Source README does not exist: $sourcePath"
}

$sourceRoot = [IO.Path]::GetDirectoryName($sourcePath)
$releaseTag = "v$Version"
$repositoryUrl = "https://github.com/DimonSmart/Intent-Driven-Development/blob/$releaseTag/"
$rawUrl = "https://raw.githubusercontent.com/DimonSmart/Intent-Driven-Development/$releaseTag/"
$pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }

# An old output must never survive a failed generation and appear publishable.
if ([IO.File]::Exists($outputPath)) {
    Remove-Item -LiteralPath $outputPath -Force
}

function Convert-DocsTarget {
    param([string] $Target, [bool] $IsImage)

    if ($Target -notmatch '^(?:\./)?docs/') { return $Target }

    $parts = [regex]::Match($Target, '^(?<path>[^?#]*)(?<suffix>[?#].*)?$')
    $encodedPath = $parts.Groups["path"].Value
    $suffix = $parts.Groups["suffix"].Value
    if ($encodedPath -match '%(?![0-9A-Fa-f]{2})') {
        throw "Invalid URL encoding in README target: $Target"
    }
    $decodedPath = [Uri]::UnescapeDataString($encodedPath)
    $platformPath = $decodedPath.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $resolved = [IO.Path]::GetFullPath([IO.Path]::Combine($sourceRoot, $platformPath))
    $rootPrefix = $sourceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($rootPrefix, $pathComparison)) {
        throw "README target escapes source root: $Target"
    }
    if (-not [IO.File]::Exists($resolved)) {
        throw "README target does not exist: $Target (resolved as $resolved)"
    }
    $relative = [IO.Path]::GetRelativePath($sourceRoot, $resolved).Replace('\', '/')
    $segments = @($relative.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) })
    $escapedRelative = $segments -join '/'
    $prefix = if ($IsImage) { $rawUrl } else { $repositoryUrl }
    return $prefix + $escapedRelative + $suffix
}

function Convert-ActiveFragment {
    param([string] $Fragment)

    # Deliberately support the README's ordinary and angle-bracket Markdown links,
    # including image links. No attempt is made to implement a Markdown parser.
    $angleLink = '(?<prefix>!?\[[^\]\r\n]*\]\(\s*<)(?<target>[^>\r\n]+)(?<suffix>>\s*(?:"[^"]*"|''[^'']*'')?\s*\))'
    $plainLink = '(?<prefix>!?\[[^\]\r\n]*\]\(\s*)(?<target>[^\s)\r\n]+)(?<suffix>\s*(?:"[^"]*"|''[^'']*'')?\s*\))'
    foreach ($pattern in @($angleLink, $plainLink)) {
        $Fragment = [regex]::Replace($Fragment, $pattern, [System.Text.RegularExpressions.MatchEvaluator] {
            param($match)
            $imageLink = $match.Value.StartsWith('!', [StringComparison]::Ordinal)
            $target = Convert-DocsTarget -Target $match.Groups['target'].Value -IsImage $imageLink
            return $match.Groups['prefix'].Value + $target + $match.Groups['suffix'].Value
        })
    }

    # HTML img tags retain every attribute except the rewritten src value.
    $Fragment = [regex]::Replace($Fragment, '(?i)<img\b[^>]*>', [System.Text.RegularExpressions.MatchEvaluator] {
        param($imageTag)
        return [regex]::Replace($imageTag.Value, '(?i)(?<prefix>\bsrc\s*=\s*)(?<quote>["''])(?<target>.*?)\k<quote>', [System.Text.RegularExpressions.MatchEvaluator] {
            param($attribute)
            $target = Convert-DocsTarget -Target $attribute.Groups['target'].Value -IsImage $true
            return $attribute.Groups['prefix'].Value + $attribute.Groups['quote'].Value + $target + $attribute.Groups['quote'].Value
        })
    })

    # Reference-style definitions are allowed, as are their optional titles.
    $definition = '(?m)^(?<prefix>[ ]{0,3}\[[^\]\r\n]+\]:[ \t]*<?)(?<target>[^\s>\r\n]+)(?<suffix>>?[^\r\n]*)'
    $Fragment = [regex]::Replace($Fragment, $definition, [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        $target = Convert-DocsTarget -Target $match.Groups['target'].Value -IsImage $false
        return $match.Groups['prefix'].Value + $target + $match.Groups['suffix'].Value
    })

    # Fail closed if the README starts using another active docs/ link syntax.
    # Remote URLs have a slash before docs/ and are intentionally excluded.
    if ([regex]::IsMatch($Fragment, '(?<![\w/])(?:\./)?docs/')) {
        throw "Unsupported relative docs/ reference in README: $($Fragment.Trim())"
    }
    return $Fragment
}

$sourceText = [IO.File]::ReadAllText($sourcePath, [Text.Encoding]::UTF8)
$sourceText = [regex]::Replace($sourceText, '\r\n?', [string][char]10)
$linePieces = [regex]::Split($sourceText, '(?<=\n)')
$backtick = [string][char]96
$codeSpanPattern = '(' + [regex]::Escape($backtick) + '+)(.*?)\1'
$fencePattern = '^[ ]{0,3}(?<marker>(?:' + [regex]::Escape($backtick) + '){3,}|~{3,})'
$fenceCharacter = ''
$fenceLength = 0
$output = [Text.StringBuilder]::new()

foreach ($line in $linePieces) {
    $trimmed = $line.TrimEnd([char]13, [char]10)
    if ($fenceLength -gt 0) {
        $closingPattern = '^[ ]{0,3}' + [regex]::Escape($fenceCharacter) + '{' + $fenceLength + ',}[ \t]*$'
        if ([regex]::IsMatch($trimmed, $closingPattern)) {
            $fenceLength = 0
            $fenceCharacter = ''
        }
        [void]$output.Append($line)
        continue
    }

    $fence = [regex]::Match($trimmed, $fencePattern)
    if ($fence.Success) {
        $marker = $fence.Groups['marker'].Value
        $fenceCharacter = $marker.Substring(0, 1)
        $fenceLength = $marker.Length
        [void]$output.Append($line)
        continue
    }

    # Keep inline code spans byte-for-byte (except normalized line endings).
    $last = 0
    foreach ($codeSpan in [regex]::Matches($line, $codeSpanPattern)) {
        [void]$output.Append((Convert-ActiveFragment -Fragment ($line.Substring($last, $codeSpan.Index - $last))))
        [void]$output.Append($codeSpan.Value)
        $last = $codeSpan.Index + $codeSpan.Length
    }
    [void]$output.Append((Convert-ActiveFragment -Fragment ($line.Substring($last))))
}

$destinationDirectory = [IO.Path]::GetDirectoryName($outputPath)
$tempPath = $null
try {
    [void][IO.Directory]::CreateDirectory($destinationDirectory)
    $tempPath = [IO.Path]::Combine($destinationDirectory, '.' + [IO.Path]::GetFileName($outputPath) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($tempPath, $output.ToString(), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tempPath -Destination $outputPath -Force
}
catch {
    if ($null -ne $tempPath -and [IO.File]::Exists($tempPath)) {
        Remove-Item -LiteralPath $tempPath -Force
    }
    if ([IO.File]::Exists($outputPath)) {
        Remove-Item -LiteralPath $outputPath -Force
    }
    throw
}
