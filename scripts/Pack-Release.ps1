param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [switch]$SkipCheck
)

$ErrorActionPreference = "Stop"

$repoRoot = & git rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0) {
    throw "Could not locate repository root."
}

Set-Location $repoRoot

$artifactsRoot = Join-Path $repoRoot "artifacts"
$releaseContentRoot = Join-Path $artifactsRoot "release-content"
$releaseZipPath = Join-Path $artifactsRoot "intent-driven-development-v$Version.zip"
$artifactChecksumsPath = Join-Path $artifactsRoot "checksums.txt"
$manifestPath = Join-Path $repoRoot "manifest.json"
$contentChecksumsPath = Join-Path $releaseContentRoot "checksums.txt"

function Remove-DirectoryIfExists([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $resolvedArtifactsRoot = (Resolve-Path -LiteralPath $artifactsRoot).Path
    if (-not $resolvedPath.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside artifacts: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

function Remove-FileIfExists([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Copy-ReleasePath([string]$RelativePath) {
    $source = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required release path not found: $RelativePath"
    }

    $destination = Join-Path $releaseContentRoot $RelativePath
    $destinationParent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
}

function Write-Checksums([string]$Root, [string]$OutputPath) {
    $lines = Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.FullName -ne $OutputPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relativePath"
        }

    Set-Content -LiteralPath $OutputPath -Value $lines -Encoding utf8NoBOM
}

function Invoke-RequiredCommand {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
Remove-DirectoryIfExists $releaseContentRoot
Remove-FileIfExists $artifactChecksumsPath
Get-ChildItem -LiteralPath $artifactsRoot -Filter "intent-driven-development-v*.zip" -File | Remove-Item -Force
Get-ChildItem -LiteralPath $artifactsRoot -Filter "*.nupkg" -File | Remove-Item -Force

if (-not $SkipCheck) {
    & (Join-Path $repoRoot "scripts/Check.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Check failed with exit code $LASTEXITCODE."
    }
}

Invoke-RequiredCommand -Description "Generator" -Command {
    dotnet run --project tools/generate -- --manifest-version $Version
}

Invoke-RequiredCommand -Description "Generator check" -Command {
    dotnet run --project tools/generate -- --check --manifest-version $Version
}

New-Item -ItemType Directory -Force -Path $releaseContentRoot | Out-Null
Copy-ReleasePath "src/canonical"
Copy-ReleasePath "src/adapters"
Copy-ReleasePath "generated"
Copy-ReleasePath "README.md"
Copy-ReleasePath "LICENSE"
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $releaseContentRoot "manifest.json") -Force
Write-Checksums $releaseContentRoot $contentChecksumsPath

Compress-Archive -Path (Join-Path $releaseContentRoot "*") -DestinationPath $releaseZipPath -Force

Invoke-RequiredCommand -Description ".NET tool package pack" -Command {
    dotnet pack tools/idd-tool/IntentDrivenDevelopment.Tool.csproj `
        --configuration Release `
        --output artifacts `
        -p:PackageVersion=$Version `
        -p:Version=$Version
}

$toolPackagePath = Join-Path $artifactsRoot "DimonSmart.IntentDrivenDevelopment.Tool.$Version.nupkg"
if (-not (Test-Path -LiteralPath $toolPackagePath)) {
    throw "Expected .NET tool package not found: $toolPackagePath"
}

$artifactFiles = @(
    $releaseZipPath
    $toolPackagePath
)

$artifactLines = $artifactFiles |
    Sort-Object |
    ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($artifactsRoot, $_).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relativePath"
    }

Set-Content -LiteralPath $artifactChecksumsPath -Value $artifactLines -Encoding utf8NoBOM

Write-Host "Release artifacts created in $artifactsRoot"
