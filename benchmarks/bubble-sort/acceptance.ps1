$ErrorActionPreference = "Stop"

$solutions = @(Get-ChildItem -LiteralPath . -File -Recurse | Where-Object { $_.Extension -in '.sln', '.slnx' })
if ($solutions.Count -ne 1) { throw "Expected exactly one .sln or .slnx solution; found $($solutions.Count)." }

$projects = @(Get-ChildItem -LiteralPath . -Filter *.csproj -File -Recurse)
$testProjects = @($projects | Where-Object { Select-String -LiteralPath $_.FullName -Pattern 'xunit' -Quiet })
$consoleProjects = @($projects | Where-Object { Select-String -LiteralPath $_.FullName -Pattern '<OutputType>Exe</OutputType>' -Quiet })
if ($testProjects.Count -ne 1) { throw "Expected exactly one xUnit test project; found $($testProjects.Count)." }
if ($consoleProjects.Count -ne 1) { throw "Expected exactly one console project; found $($consoleProjects.Count)." }

$sources = @(Get-ChildItem -LiteralPath . -Filter *.cs -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' })
$productSources = @($sources | Where-Object { $_.FullName -notmatch '(?i)test' })
$productText = ($productSources | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"
if ($productText -notmatch '(?i)bubble\s*sort|bubblesort') { throw "Bubble Sort implementation was not found." }
if ($productText -notmatch '\bfor\s*\(|\bwhile\s*\(') { throw "Bubble Sort implementation does not contain an explicit loop." }
if ($productText -match '(?i)Array\.Sort|MemoryExtensions\.Sort|\.AsSpan\s*\(\s*\)\.Sort|\bOrderBy(?:Descending)?\s*\(|\bOrder(?:Descending)?\s*\(') { throw "A forbidden framework sorting API is used." }
$listVariables = @([regex]::Matches($productText, '(?i)(?:List\s*<[^>]+>\s+|var\s+)(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?:List\s*<[^>]+>|\(\))'))
foreach ($match in $listVariables) {
    $name = [regex]::Escape($match.Groups['name'].Value)
    if ($productText -match "\b$name\.Sort\s*\(") { throw "A forbidden List<T>.Sort call is used." }
}

$buildSucceeded = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    $buildOutput = @(& dotnet build $solutions[0].FullName --nologo 2>&1)
    $buildExitCode = $LASTEXITCODE
    $buildOutput | Write-Output
    if ($buildExitCode -eq 0) { $buildSucceeded = $true; break }
    $transientLock = ($buildOutput -join "`n") -match 'MSB3491|being used by another process|Access to the path .* is denied'
    if (-not $transientLock -or $attempt -eq 3) { throw "dotnet build failed with exit code $buildExitCode." }
    Start-Sleep -Seconds 2
}
if (-not $buildSucceeded) { throw "dotnet build did not succeed." }

$consoleDirectory = $consoleProjects[0].Directory.FullName
$probeProject = Join-Path $PSScriptRoot 'acceptance\BubbleSortAcceptance.csproj'
& dotnet run --project $probeProject -- $consoleDirectory
if ($LASTEXITCODE -ne 0) { throw "Deterministic Bubble Sort probe failed with exit code $LASTEXITCODE." }

$resultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ("idd-bubble-sort-acceptance-" + [Guid]::NewGuid().ToString('N'))
try {
    $testOutput = @(& dotnet test $solutions[0].FullName --no-build --nologo --logger 'trx;LogFileName=acceptance.trx' --results-directory $resultsDirectory 2>&1)
    $testExitCode = $LASTEXITCODE
    $testOutput | Write-Output
    if ($testExitCode -ne 0) { throw "dotnet test failed with exit code $testExitCode." }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $resultsDirectory 'acceptance.trx') -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -ne 7 -or [int]$counters.passed -ne 7) { throw "Expected exactly seven passing tests; TRX reported total=$($counters.total), passed=$($counters.passed)." }
} finally {
    if (Test-Path -LiteralPath $resultsDirectory) { Remove-Item -LiteralPath $resultsDirectory -Recurse -Force }
}

Write-Output "Acceptance passed: one solution, one console project, one xUnit project, genuine Bubble Sort, seven tests, build and test successful."
