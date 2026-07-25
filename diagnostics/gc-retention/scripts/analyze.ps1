[CmdletBinding()]
param(
    [string]$MagickaDir = "",
    [string]$Manifest = "",
    [string[]]$AnalyzerArguments = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$diagnosticsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)
$analyzerDll = Join-Path $diagnosticsRoot 'build\analyzer\Magicka.GcAnalyzer.dll'
$x86Dotnet = 'C:\Program Files (x86)\dotnet\dotnet.exe'

function Test-LiveRetentionManifest {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File
    )

    try {
        $headers = @{}
        foreach ($line in (Get-Content -LiteralPath $File.FullName -TotalCount 12)) {
            if (-not $line.StartsWith('# ')) {
                continue
            }

            $parts = $line.Substring(2).Split(
                @("`t"),
                2,
                [System.StringSplitOptions]::None
            )
            if ($parts.Length -eq 2) {
                $headers[$parts[0]] = $parts[1]
            }
        }

        [int]$processId = 0
        [long]$startTicks = 0
        if (-not [int]::TryParse($headers['pid'], [ref]$processId) `
                -or -not [long]::TryParse(
                    $headers['process-start-utc-ticks'],
                    [ref]$startTicks
                ) `
                -or [string]::IsNullOrWhiteSpace($headers['process-path'])) {
            return $false
        }

        $process = Get-Process -Id $processId -ErrorAction Stop
        if ($process.StartTime.ToUniversalTime().Ticks -ne $startTicks) {
            return $false
        }

        $actualPath = $process.Path
        if ([string]::IsNullOrWhiteSpace($actualPath)) {
            $actualPath = $process.MainModule.FileName
        }

        return [string]::Equals(
            [System.IO.Path]::GetFullPath($actualPath),
            [System.IO.Path]::GetFullPath($headers['process-path']),
            [System.StringComparison]::OrdinalIgnoreCase
        )
    }
    catch {
        return $false
    }
}

if (-not (Test-Path -LiteralPath $x86Dotnet -PathType Leaf)) {
    throw "The x86 .NET runtime was not found at $x86Dotnet."
}

if (-not (Test-Path -LiteralPath $analyzerDll -PathType Leaf)) {
    throw "Analyzer build is missing. Run diagnostics\gc-retention\scripts\build.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($Manifest)) {
    $manifestDirectories = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace(
            $env:MAGICKA_GC_DIAGNOSTICS_DIR
        )) {
        $manifestDirectories.Add($env:MAGICKA_GC_DIAGNOSTICS_DIR)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $manifestDirectories.Add(
            (Join-Path $env:LOCALAPPDATA 'MagickaCommunityPatch\gc-retention')
        )
    }

    if (-not [string]::IsNullOrWhiteSpace($MagickaDir)) {
        $manifestDirectories.Add(
            (Join-Path (
                [System.IO.Path]::GetFullPath($MagickaDir)
            ) 'CommunityPatch\gc-retention')
        )
    }

    $seenDirectories = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )
    $manifestFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($directory in $manifestDirectories) {
        $fullDirectory = [System.IO.Path]::GetFullPath($directory)
        if (-not $seenDirectories.Add($fullDirectory) `
                -or -not (Test-Path -LiteralPath $fullDirectory -PathType Container)) {
            continue
        }

        foreach ($file in (Get-ChildItem -LiteralPath $fullDirectory `
                    -Filter 'retention-*.tsv' -File -ErrorAction Stop)) {
            $manifestFiles.Add($file)
        }
    }

    $latest = $manifestFiles |
        Sort-Object LastWriteTimeUtc -Descending |
        Where-Object { Test-LiveRetentionManifest -File $_ } |
        Select-Object -First 1
    if ($null -eq $latest) {
        throw "No live retention manifest was found. Searched: " `
            + ($seenDirectories -join '; ')
    }

    $Manifest = $latest.FullName
}

$manifestPath = [System.IO.Path]::GetFullPath($Manifest)
& $x86Dotnet $analyzerDll $manifestPath @AnalyzerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Retention analyzer failed with exit code $LASTEXITCODE."
}
