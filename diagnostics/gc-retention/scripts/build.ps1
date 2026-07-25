[CmdletBinding()]
param(
    [string]$OutputDir = "",
    [switch]$RefreshPayload,
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$diagnosticsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)
$repoRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $diagnosticsRoot '..\..')
)

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $diagnosticsRoot 'build'
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputDir)
$gameOutput = Join-Path $outputRoot 'game'
$analyzerOutput = Join-Path $outputRoot 'analyzer'
$payloadOutput = Join-Path $diagnosticsRoot 'payload'

$runtimeProject = Join-Path $diagnosticsRoot 'src\Magicka.GcDiagnostics\Magicka.GcDiagnostics.csproj'
$analyzerProject = Join-Path $diagnosticsRoot 'src\Magicka.GcAnalyzer\Magicka.GcAnalyzer.csproj'
$patcherProject = Join-Path $diagnosticsRoot 'tools\RetentionPatcher\RetentionPatcher.csproj'
$validatorProject = Join-Path $diagnosticsRoot 'tools\PayloadValidator\PayloadValidator.csproj'

$runtimeDll = Join-Path $diagnosticsRoot 'src\Magicka.GcDiagnostics\bin\Release\net35\Magicka.GcDiagnostics.dll'
$analyzerBuild = Join-Path $diagnosticsRoot 'src\Magicka.GcAnalyzer\bin\Release\net8.0'

foreach ($directory in @($gameOutput, $analyzerOutput)) {
    if (Test-Path -LiteralPath $directory) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            throw "Build output path is not a directory: $directory"
        }

        Remove-Item -LiteralPath $directory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $directory | Out-Null
}

$restoreArguments = @()
if ($SkipRestore) {
    $restoreArguments += '--no-restore'
}

& dotnet build $runtimeProject -c Release @restoreArguments
if ($LASTEXITCODE -ne 0) {
    throw "Runtime helper build failed."
}

& dotnet build $analyzerProject -c Release @restoreArguments
if ($LASTEXITCODE -ne 0) {
    throw "Analyzer build failed."
}

& dotnet run --project $patcherProject -c Release @restoreArguments -- `
    (Join-Path $repoRoot 'Magicka.exe') `
    (Join-Path $repoRoot 'PolygonHead.dll') `
    $runtimeDll `
    $gameOutput
if ($LASTEXITCODE -ne 0) {
    throw "Assembly instrumentation failed."
}

Copy-Item -LiteralPath $runtimeDll `
    -Destination (Join-Path $gameOutput 'Magicka.GcDiagnostics.dll') `
    -Force

Copy-Item -Path (Join-Path $analyzerBuild '*') `
    -Destination $analyzerOutput `
    -Recurse `
    -Force

& dotnet run --project $validatorProject -c Release @restoreArguments -- `
    $gameOutput
if ($LASTEXITCODE -ne 0) {
    throw "Payload validation failed."
}

if ($RefreshPayload) {
    $payloadFiles = @(
        'Magicka.exe',
        'PolygonHead.dll',
        'Magicka.GcDiagnostics.dll'
    )
    $swapId = [Guid]::NewGuid().ToString('N')
    $stagedPayload = Join-Path $diagnosticsRoot (
        'payload-staging-' + $swapId
    )
    $previousPayload = Join-Path $diagnosticsRoot (
        'payload-previous-' + $swapId
    )
    $movedPreviousPayload = $false
    try {
        New-Item -ItemType Directory -Path $stagedPayload | Out-Null
        foreach ($fileName in $payloadFiles) {
            Copy-Item -LiteralPath (Join-Path $gameOutput $fileName) `
                -Destination (Join-Path $stagedPayload $fileName)
        }

        $manifestLines = [System.Collections.Generic.List[string]]::new()
        foreach ($fileName in $payloadFiles) {
            $hash = (Get-FileHash `
                    -LiteralPath (Join-Path $stagedPayload $fileName) `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
            $manifestLines.Add("$hash`t$fileName")
        }

        [System.IO.File]::WriteAllLines(
            (Join-Path $stagedPayload 'payload-manifest.tsv'),
            $manifestLines,
            [System.Text.UTF8Encoding]::new($false)
        )

        & dotnet run --project $validatorProject -c Release `
            @restoreArguments -- $stagedPayload
        if ($LASTEXITCODE -ne 0) {
            throw "Staged payload validation failed."
        }

        if (Test-Path -LiteralPath $payloadOutput) {
            if (-not (Test-Path -LiteralPath $payloadOutput `
                    -PathType Container)) {
                throw "Payload path is not a directory: $payloadOutput"
            }

            Move-Item -LiteralPath $payloadOutput `
                -Destination $previousPayload
            $movedPreviousPayload = $true
        }

        try {
            Move-Item -LiteralPath $stagedPayload `
                -Destination $payloadOutput
        }
        catch {
            if ($movedPreviousPayload `
                    -and -not (Test-Path -LiteralPath $payloadOutput)) {
                Move-Item -LiteralPath $previousPayload `
                    -Destination $payloadOutput
                $movedPreviousPayload = $false
            }

            throw
        }

        if ($movedPreviousPayload) {
            try {
                Remove-Item -LiteralPath $previousPayload -Recurse -Force
                $movedPreviousPayload = $false
            }
            catch {
                Write-Warning (
                    "New payload is valid, but the previous temporary " `
                    + "directory could not be removed: $previousPayload"
                )
            }
        }
    }
    catch {
        if (Test-Path -LiteralPath $stagedPayload) {
            Remove-Item -LiteralPath $stagedPayload -Recurse -Force `
                -ErrorAction SilentlyContinue
        }

        if ($movedPreviousPayload `
                -and -not (Test-Path -LiteralPath $payloadOutput)) {
            Move-Item -LiteralPath $previousPayload `
                -Destination $payloadOutput
        }

        throw
    }

    Write-Host "Refreshed diagnostic payload: $payloadOutput"
}

Write-Host "Diagnostic game payload: $gameOutput"
Write-Host "x86 analyzer build: $analyzerOutput"
