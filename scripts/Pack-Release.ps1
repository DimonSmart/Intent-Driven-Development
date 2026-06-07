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
$npmStagingRoot = Join-Path $artifactsRoot "npm-package"
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

function Write-Manifest([string]$Path) {
    $entryPoints = [ordered]@{}
    $targets = @()

    Get-ChildItem -LiteralPath (Join-Path $repoRoot "src/adapters") -Directory |
        Sort-Object Name |
        ForEach-Object {
            $adapter = Get-Content -LiteralPath (Join-Path $_.FullName "adapter.json") -Raw | ConvertFrom-Json
            $targets += $adapter.agent
            $entryPoints[$adapter.agent] = $adapter.entryPoint
        }

    $manifest = [ordered]@{
        name = "Intent-Driven Development"
        version = $Version
        canonicalSource = "src/canonical"
        generatedRoot = "generated"
        targets = $targets
        entryPoints = $entryPoints
    }

    $json = $manifest | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8NoBOM
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

function Copy-NpmSource {
    New-Item -ItemType Directory -Force -Path $npmStagingRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "npm/package.json") -Destination $npmStagingRoot -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "npm/README.md") -Destination $npmStagingRoot -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "npm/bin") -Destination (Join-Path $npmStagingRoot "bin") -Recurse -Force

    $packageJsonPath = Join-Path $npmStagingRoot "package.json"
    $packageJson = Get-Content -LiteralPath $packageJsonPath -Raw | ConvertFrom-Json
    $packageJson.version = $Version
    $packageJson | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $packageJsonPath -Encoding utf8NoBOM

    $packageContentRoot = Join-Path $npmStagingRoot "package-content"
    New-Item -ItemType Directory -Force -Path $packageContentRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "manifest.json") -Destination $packageContentRoot -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "generated") -Destination (Join-Path $packageContentRoot "generated") -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "src") -Destination (Join-Path $packageContentRoot "src") -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $packageContentRoot -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $packageContentRoot -Force
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
Remove-DirectoryIfExists $npmStagingRoot
Remove-FileIfExists $artifactChecksumsPath
Get-ChildItem -LiteralPath $artifactsRoot -Filter "intent-driven-development-v*.zip" -File | Remove-Item -Force
Get-ChildItem -LiteralPath $artifactsRoot -Filter "*.nupkg" -File | Remove-Item -Force
Get-ChildItem -LiteralPath $artifactsRoot -Filter "*.tgz" -File | Remove-Item -Force

if (-not $SkipCheck) {
    & (Join-Path $repoRoot "scripts/Check.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Check failed with exit code $LASTEXITCODE."
    }
}
else {
    Invoke-RequiredCommand -Description "Generator" -Command { dotnet run --project tools/generate }
}

Write-Manifest $manifestPath

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

Copy-NpmSource
Invoke-RequiredCommand -Description "npm package pack" -Command { npm pack $npmStagingRoot --pack-destination $artifactsRoot }

$toolPackagePath = Join-Path $artifactsRoot "DimonSmart.IntentDrivenDevelopment.Tool.$Version.nupkg"
if (-not (Test-Path -LiteralPath $toolPackagePath)) {
    throw "Expected .NET tool package not found: $toolPackagePath"
}

$artifactFiles = @(
    $releaseZipPath
    (Get-ChildItem -LiteralPath $artifactsRoot -Filter "*.tgz" -File | Select-Object -First 1).FullName
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