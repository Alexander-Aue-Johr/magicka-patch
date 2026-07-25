[CmdletBinding()]
param(
    [switch]$SkipRegistry
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$diagnosticsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)
$targetProject = Join-Path $PSScriptRoot 'AnalyzerTarget\AnalyzerTarget.csproj'
$analyzerProject = Join-Path $diagnosticsRoot 'src\Magicka.GcAnalyzer\Magicka.GcAnalyzer.csproj'
$registryProject = Join-Path $PSScriptRoot 'RegistryTarget\RegistryTarget.csproj'
$targetDll = Join-Path $PSScriptRoot 'AnalyzerTarget\bin\Release\net8.0\AnalyzerTarget.dll'
$analyzerDll = Join-Path $diagnosticsRoot 'src\Magicka.GcAnalyzer\bin\Release\net8.0\Magicka.GcAnalyzer.dll'
$registryExe = Join-Path $PSScriptRoot 'RegistryTarget\bin\Release\net48\RegistryTarget.exe'
$x86Dotnet = 'C:\Program Files (x86)\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $x86Dotnet -PathType Leaf)) {
    throw "The x86 .NET runtime was not found at $x86Dotnet."
}

& dotnet build $targetProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Analyzer integration target build failed."
}
& dotnet build $analyzerProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Analyzer build failed."
}

if (-not $SkipRegistry) {
    & dotnet build $registryProject -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Runtime registry integration target build failed."
    }
    & $registryExe
    if ($LASTEXITCODE -ne 0) {
        throw "Runtime registry integration test failed."
    }
}

$testRoot = Join-Path (
    [System.IO.Path]::GetTempPath()
) ('magicka-gc-analyzer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$manifest = Join-Path $testRoot 'retention.tsv'
$report = Join-Path $testRoot 'analysis.txt'
$limitedReport = Join-Path $testRoot 'analysis-limited.txt'
$staleManifest = Join-Path $testRoot 'retention-stale-registry.tsv'
$staleReport = Join-Path $testRoot 'analysis-stale-registry.txt'
$badManifest = Join-Path $testRoot 'retention-wrong-process.tsv'
$badReport = Join-Path $testRoot 'analysis-wrong-process.txt'
$stdout = Join-Path $testRoot 'target.out'
$stderr = Join-Path $testRoot 'target.err'

$targetArguments = '"{0}" "{1}"' -f $targetDll, $manifest

$targetProcess = Start-Process `
    -FilePath $x86Dotnet `
    -ArgumentList $targetArguments `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr `
    -PassThru

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
        if ([DateTime]::UtcNow -gt $deadline) {
            throw "Timed out waiting for analyzer target manifest."
        }
        Start-Sleep -Milliseconds 200
    }

    & $x86Dotnet $analyzerDll $manifest `
        --output $report `
        --timeout 30
    if ($LASTEXITCODE -ne 8) {
        throw "Analyzer integration run returned $LASTEXITCODE; expected incomplete status 8."
    }

    $reportText = [System.IO.File]::ReadAllText($report)
    foreach ($expected in @(
            'LEAK #1 StaleLevel',
            'STALE EDGES #2 PooledMissile',
            '--.Target--> StaleLevel',
            'REUSED/STALE #3 StaleLevel',
            'expected WeakShort',
            'REUSED/STALE #4 Wrong.Type',
            'points to PooledMissile',
            'STALE EDGES #5 PooledArray',
            'LEAK #6 RetiredScene',
            'LEAK #7 StaleLevel',
            '--.Level--> StaleLevel',
            'INCOMPLETE: 2 eligible candidate(s)'
        )) {
        if (-not $reportText.Contains($expected)) {
            throw "Analyzer report did not contain expected text: $expected"
        }
    }

    & $x86Dotnet $analyzerDll $manifest `
        --output $limitedReport `
        --timeout 30 `
        --max-detach-nodes 1
    if ($LASTEXITCODE -ne 8) {
        throw "Limited-budget analyzer run returned $LASTEXITCODE; expected incomplete status 8."
    }

    $limitedReportText = [System.IO.File]::ReadAllText($limitedReport)
    foreach ($expected in @(
            'TRUNCATED: global detach node budget 1 was reached',
            'TRUNCATED #5 PooledArray',
            'fully unanalysed owners: 1',
            'partially analysed owners: 1'
        )) {
        if (-not $limitedReportText.Contains($expected)) {
            throw "Limited report did not contain expected text: $expected"
        }
    }

    $manifestText = [System.IO.File]::ReadAllText($manifest)
    $staleManifestText = [System.Text.RegularExpressions.Regex]::Replace(
        $manifestText,
        '(?m)^# registry-version\t[0-9]+\r?$',
        "# registry-version`t1"
    )
    if ($staleManifestText -eq $manifestText) {
        throw "Could not construct the stale-registry manifest."
    }
    [System.IO.File]::WriteAllText(
        $staleManifest,
        $staleManifestText,
        [System.Text.UTF8Encoding]::new($false)
    )

    & $x86Dotnet $analyzerDll $staleManifest `
        --output $staleReport `
        --timeout 30
    if ($LASTEXITCODE -ne 7) {
        throw "Analyzer returned $LASTEXITCODE for a stale registry manifest; expected 7."
    }

    $staleReportText = [System.IO.File]::ReadAllText($staleReport)
    foreach ($expected in @(
            'MANIFEST CHANGED',
            'registry version 1',
            'process snapshot contains version 73'
        )) {
        if (-not $staleReportText.Contains($expected)) {
            throw "Stale-registry report did not contain expected text: $expected"
        }
    }

    $badManifestText = [System.Text.RegularExpressions.Regex]::Replace(
        $manifestText,
        '(?m)^# process-start-utc-ticks\t[0-9]+\r?$',
        "# process-start-utc-ticks`t1"
    )
    if ($badManifestText -eq $manifestText) {
        throw "Could not construct the wrong-process manifest."
    }
    [System.IO.File]::WriteAllText(
        $badManifest,
        $badManifestText,
        [System.Text.UTF8Encoding]::new($false)
    )

    & $x86Dotnet $analyzerDll $badManifest `
        --output $badReport `
        --timeout 30
    if ($LASTEXITCODE -eq 0) {
        throw "Analyzer accepted a manifest with the wrong process start time."
    }

    $badReportText = [System.IO.File]::ReadAllText($badReport)
    if (-not $badReportText.Contains('TARGET IDENTITY ERROR')) {
        throw "Wrong-process report did not contain the identity error."
    }

    Write-Host "GC analyzer integration test passed."
}
finally {
    if (-not $targetProcess.HasExited) {
        Stop-Process -Id $targetProcess.Id -Force
    }
    $targetProcess.WaitForExit()

    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath()
    )
    if (-not $resolvedTestRoot.StartsWith(
            $resolvedTempRoot,
            [System.StringComparison]::OrdinalIgnoreCase
        )) {
        throw "Refusing to remove test path outside the temp directory: $resolvedTestRoot"
    }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
