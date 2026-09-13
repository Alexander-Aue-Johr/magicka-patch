param(
    [Parameter(Mandatory = $true)][string]$OriginalMagicka,
    [Parameter(Mandatory = $true)][string]$PatchedMagicka,
    [Parameter(Mandatory = $true)][string]$OriginalPolygonHead,
    [Parameter(Mandatory = $true)][string]$PatchedPolygonHead,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$experimentRoot = Join-Path $repositoryRoot "tools\inventory-box-patcher-experiment"
$analysisScript = Join-Path $experimentRoot "analyze.ps1"
$normalizerProject = Join-Path $experimentRoot "src\SourceNoiseNormalizer\SourceNoiseNormalizer.csproj"
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)

function Assert-Input([string]$path, [string]$label) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "$label does not exist: $path"
    }
}

function Invoke-AssemblyAudit(
    [string]$name,
    [string]$original,
    [string]$patched) {
    $analysis = Join-Path $outputRoot "$name\analysis"
    & powershell -ExecutionPolicy Bypass -File $analysisScript `
        -OriginalExe $original `
        -CurrentPatchExe $patched `
        -OutputDirectory $analysis
    if ($LASTEXITCODE -ne 0) {
        throw "$name decompilation failed with exit code $LASTEXITCODE"
    }

    $normalized = Join-Path $outputRoot "$name\normalized"
    & dotnet run --project $normalizerProject --configuration Release -- `
        (Join-Path $analysis "decompiled\original") `
        (Join-Path $analysis "decompiled\current-patch") `
        (Join-Path $normalized "original") `
        (Join-Path $normalized "patched") `
        (Join-Path $normalized "locals.csv")
    if ($LASTEXITCODE -ne 0) {
        throw "$name source normalization failed with exit code $LASTEXITCODE"
    }

    Write-NormalizedDiffs $name $analysis $normalized
}

function Write-NormalizedDiffs(
    [string]$name,
    [string]$analysis,
    [string]$normalized) {
    $ranking = @(Import-Csv -LiteralPath (Join-Path $analysis "file-diff-ranking.csv") |
        Where-Object Kind -eq "modified")
    $diffRoot = Join-Path $outputRoot "$name\semantic-review-diffs"
    New-Item -ItemType Directory -Path $diffRoot -Force | Out-Null
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($entry in $ranking) {
        $relative = $entry.RelativePath
        $rawOriginal = Join-Path $analysis "decompiled\original\$relative"
        $rawPatched = Join-Path $analysis "decompiled\current-patch\$relative"
        $normalizedOriginal = Join-Path $normalized "original\$relative"
        $normalizedPatched = Join-Path $normalized "patched\$relative"
        $diffPath = Join-Path $diffRoot ($relative + ".diff")
        New-Item -ItemType Directory -Path (Split-Path -Parent $diffPath) -Force | Out-Null
        & git -c core.safecrlf=false diff --no-index --output=$diffPath -- `
            $normalizedOriginal $normalizedPatched
        if ($LASTEXITCODE -notin 0, 1) {
            throw "git diff failed for $name/$relative"
        }

        $rawLines = Get-ChangedLines $rawOriginal $rawPatched
        $normalizedLines = Get-ChangedLines $normalizedOriginal $normalizedPatched
        $status = if ($normalizedLines -lt $rawLines) {
            "normalized-local-noise"
        }
        elseif ($normalizedLines -gt $rawLines) {
            "normalizer-needs-paired-alignment"
        }
        else {
            "manual-semantic-review-pending"
        }
        $rows.Add([pscustomobject]@{
            Assembly = $name
            File = $relative
            RawChangedLines = $rawLines
            NormalizedChangedLines = $normalizedLines
            Status = $status
        })
    }

    $rows | Export-Csv -NoTypeInformation -Encoding utf8 `
        -LiteralPath (Join-Path $outputRoot "$name\noise-audit.csv")
}

function Get-ChangedLines([string]$before, [string]$after) {
    $line = @(& git -c core.safecrlf=false diff --no-index --numstat -- `
        $before $after 2>$null | Where-Object { $_ -match '^\d+\s+\d+' })[-1]
    if (-not $line) {
        return 0
    }
    $parts = $line -split '\s+'
    return [int]$parts[0] + [int]$parts[1]
}

function Write-Checklist {
    $rows = @(
        Import-Csv -LiteralPath (Join-Path $outputRoot "magicka\noise-audit.csv")
        Import-Csv -LiteralPath (Join-Path $outputRoot "polygonhead\noise-audit.csv")
    )
    $path = Join-Path $outputRoot "DENOISE_CHECKLIST.md"
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Manual payload denoise checklist")
    $lines.Add("")
    $lines.Add("Legend: `[ ]` requires semantic review; `[~]` contains recognized local-name noise; `[!]` requires paired declaration alignment; `[x]` is fully reviewed and noise-free.")
    $lines.Add("")
    foreach ($assembly in @("magicka", "polygonhead")) {
        $lines.Add("## $assembly")
        $lines.Add("")
        foreach ($row in @($rows | Where-Object Assembly -eq $assembly | Sort-Object File)) {
            $marker = switch ($row.Status) {
                "normalized-local-noise" { "~" }
                "normalizer-needs-paired-alignment" { "!" }
                default { " " }
            }
            $lines.Add("- [$marker] ``$($row.File)`` - $($row.Status); raw $($row.RawChangedLines), normalized $($row.NormalizedChangedLines) changed lines")
        }
        $lines.Add("")
    }
    $lines | Set-Content -LiteralPath $path -Encoding utf8
    Write-Output "checklist=$path"
}

Assert-Input $OriginalMagicka "Original Magicka.exe"
Assert-Input $PatchedMagicka "Patched Magicka.exe"
Assert-Input $OriginalPolygonHead "Original PolygonHead.dll"
Assert-Input $PatchedPolygonHead "Patched PolygonHead.dll"
if (Test-Path -LiteralPath $outputRoot) {
    throw "Refusing to overwrite existing audit directory: $outputRoot"
}
New-Item -ItemType Directory -Path $outputRoot | Out-Null

@(
    "original_magicka_sha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $OriginalMagicka).Hash)"
    "patched_magicka_sha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $PatchedMagicka).Hash)"
    "original_polygonhead_sha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $OriginalPolygonHead).Hash)"
    "patched_polygonhead_sha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $PatchedPolygonHead).Hash)"
) | Set-Content -LiteralPath (Join-Path $outputRoot "inputs.txt") -Encoding ascii

Invoke-AssemblyAudit "magicka" $OriginalMagicka $PatchedMagicka
Invoke-AssemblyAudit "polygonhead" $OriginalPolygonHead $PatchedPolygonHead
Write-Checklist
