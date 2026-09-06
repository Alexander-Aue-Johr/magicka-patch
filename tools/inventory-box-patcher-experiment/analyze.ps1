param(
    [string]$OriginalExe = "..\..\Magicka_orig.exe",
    [string]$CurrentPatchExe = "..\..\Magicka.exe",
    [string]$OutputDirectory = "..\..\tmp\inventory-box-source-analysis"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2

$experimentRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Resolve-ArgumentPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $experimentRoot $path))
}

$originalPath = Resolve-ArgumentPath $OriginalExe
$currentPatchPath = Resolve-ArgumentPath $CurrentPatchExe
$outputRoot = Resolve-ArgumentPath $OutputDirectory
$inputRoot = Join-Path $outputRoot "inputs"
$originalInputRoot = Join-Path $inputRoot "original"
$currentInputRoot = Join-Path $inputRoot "current-patch"
$referenceRoot = Join-Path $outputRoot "references"
$originalReferenceRoot = Join-Path $referenceRoot "original"
$currentReferenceRoot = Join-Path $referenceRoot "current-patch"
$originalSourceRoot = Join-Path $outputRoot "decompiled\original"
$currentSourceRoot = Join-Path $outputRoot "decompiled\current-patch"
$diffRoot = Join-Path $outputRoot "file-diffs"
$commentStripperProject = Join-Path $experimentRoot "src\SourceCommentStripper\SourceCommentStripper.csproj"

function Invoke-Analysis {
    Assert-AnalysisInputs
    Prepare-FreshAnalysisDirectory
    Restore-AnalysisTools
    Prepare-IsolatedInputs
    Decompile-Assemblies
    Remove-DecompilerComments
    $identicalCount = Remove-IdenticalSourcePairs
    $ranking = Write-FileDiffsAndRanking
    Write-AnalysisSummary $identicalCount $ranking
    Show-SmallestDiff $ranking
}

function Assert-AnalysisInputs {
    if (-not (Test-Path -LiteralPath $originalPath -PathType Leaf)) {
        throw "Original executable does not exist: $originalPath"
    }
    if (-not (Test-Path -LiteralPath $currentPatchPath -PathType Leaf)) {
        throw "Current patch executable does not exist: $currentPatchPath"
    }
}

function Prepare-FreshAnalysisDirectory {
    if (Test-Path -LiteralPath $outputRoot) {
        throw "Refusing to overwrite existing analysis directory: $outputRoot"
    }

    New-Item -ItemType Directory -Path `
        $originalInputRoot, `
        $currentInputRoot, `
        $originalReferenceRoot, `
        $currentReferenceRoot, `
        $originalSourceRoot, `
        $currentSourceRoot, `
        $diffRoot | Out-Null
}

function Restore-AnalysisTools {
    Push-Location $experimentRoot
    try {
        & dotnet tool restore
        Assert-LastExitCode "ILSpy restore"
        & dotnet build $commentStripperProject --configuration Release
        Assert-LastExitCode "comment-stripper build"
    }
    finally {
        Pop-Location
    }
}

function Decompile-Assemblies {
    Invoke-ProjectDecompilation `
        (Join-Path $originalInputRoot "Magicka.exe") `
        $originalReferenceRoot `
        $originalSourceRoot
    Invoke-ProjectDecompilation `
        (Join-Path $currentInputRoot "Magicka.exe") `
        $currentReferenceRoot `
        $currentSourceRoot
}

function Prepare-IsolatedInputs {
    Copy-Item -LiteralPath $originalPath -Destination (Join-Path $originalInputRoot "Magicka.exe")
    Copy-Item -LiteralPath $currentPatchPath -Destination (Join-Path $currentInputRoot "Magicka.exe")

    $originalDirectory = Split-Path -Parent $originalPath
    $currentDirectory = Split-Path -Parent $currentPatchPath
    Copy-AssemblyReferences $originalDirectory $originalReferenceRoot
    Copy-AssemblyReferences $originalDirectory $currentReferenceRoot
    Copy-AssemblyReferences $currentDirectory $currentReferenceRoot
}

function Copy-AssemblyReferences([string]$source, [string]$destination) {
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Filter *.dll) {
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}

function Invoke-ProjectDecompilation(
    [string]$assemblyPath,
    [string]$referencePath,
    [string]$destination) {
    Push-Location $experimentRoot
    try {
        & dotnet tool run ilspycmd -- `
            --disable-updatecheck `
            --nested-directories `
            --project `
            --languageversion CSharp3 `
            --referencepath $referencePath `
            --outputdir $destination `
            $assemblyPath
        Assert-LastExitCode "decompilation of $assemblyPath"
    }
    finally {
        Pop-Location
    }
}

function Remove-DecompilerComments {
    & dotnet run `
        --project $commentStripperProject `
        --configuration Release `
        --no-build `
        -- `
        $originalSourceRoot `
        $currentSourceRoot
    Assert-LastExitCode "comment stripping"
}

function Remove-IdenticalSourcePairs {
    $originalPrefix = $originalSourceRoot.TrimEnd('\') + '\'
    $currentPrefix = $currentSourceRoot.TrimEnd('\') + '\'
    $identicalPairs = New-Object System.Collections.Generic.List[object]

    foreach ($originalFile in Get-ChildItem -LiteralPath $originalSourceRoot -Recurse -File -Filter *.cs) {
        $relativePath = Get-RelativePath $originalFile.FullName $originalPrefix
        $currentPath = Join-Path $currentSourceRoot $relativePath
        if (-not (Test-Path -LiteralPath $currentPath -PathType Leaf)) {
            continue
        }

        $originalHash = (Get-FileHash -LiteralPath $originalFile.FullName -Algorithm SHA256).Hash
        $currentHash = (Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash
        if ($originalHash -eq $currentHash) {
            $identicalPairs.Add([pscustomobject]@{
                RelativePath = $relativePath
                OriginalPath = $originalFile.FullName
                CurrentPath = $currentPath
            })
        }
    }

    foreach ($pair in $identicalPairs) {
        Assert-PathStartsWith $pair.OriginalPath $originalPrefix
        Assert-PathStartsWith $pair.CurrentPath $currentPrefix
        Remove-Item -LiteralPath $pair.OriginalPath, $pair.CurrentPath -Force
    }

    $identicalPairs.RelativePath |
        Set-Content -LiteralPath (Join-Path $outputRoot "identical-files-removed.txt") -Encoding utf8
    return $identicalPairs.Count
}

function Write-FileDiffsAndRanking {
    $originalFiles = Get-FilesByRelativePath $originalSourceRoot
    $currentFiles = Get-FilesByRelativePath $currentSourceRoot
    $relativePaths = @($originalFiles.Keys + $currentFiles.Keys | Sort-Object -Unique)
    $rows = New-Object System.Collections.Generic.List[object]

    foreach ($relativePath in $relativePaths) {
        $hasOriginal = $originalFiles.ContainsKey($relativePath)
        $hasCurrent = $currentFiles.ContainsKey($relativePath)
        $beforePath = if ($hasOriginal) { $originalFiles[$relativePath] } else { "NUL" }
        $afterPath = if ($hasCurrent) { $currentFiles[$relativePath] } else { "NUL" }
        $kind = if ($hasOriginal -and $hasCurrent) { "modified" } elseif ($hasCurrent) { "added" } else { "removed" }
        $diffPath = Join-Path $diffRoot ($relativePath + ".diff")
        $diffDirectory = Split-Path -Parent $diffPath
        New-Item -ItemType Directory -Path $diffDirectory -Force | Out-Null

        & git -c core.safecrlf=false diff --no-index --output=$diffPath -- $beforePath $afterPath
        if ($LASTEXITCODE -notin 0, 1) {
            throw "git diff failed for $relativePath"
        }

        $numstatOutput = @(& git -c core.safecrlf=false diff --no-index --numstat -- $beforePath $afterPath 2>$null)
        if ($LASTEXITCODE -notin 0, 1) {
            throw "git diff --numstat failed for $relativePath"
        }
        $numstatLine = @($numstatOutput | Where-Object { $_ -match '^\d+\t\d+\t' })[-1]
        if (-not $numstatLine) {
            throw "No text numstat result for $relativePath"
        }
        $parts = $numstatLine -split "`t"
        $addedLines = [int]$parts[0]
        $deletedLines = [int]$parts[1]
        $rows.Add([pscustomobject]@{
            ChangedLines = $addedLines + $deletedLines
            AddedLines = $addedLines
            DeletedLines = $deletedLines
            Kind = $kind
            RelativePath = $relativePath
        })
    }

    $ranking = @($rows | Sort-Object ChangedLines, Kind, RelativePath)
    $ranking |
        Export-Csv -LiteralPath (Join-Path $outputRoot "file-diff-ranking.csv") -NoTypeInformation -Encoding utf8
    return $ranking
}

function Write-AnalysisSummary([int]$identicalCount, [object[]]$ranking) {
    $modifiedCount = @($ranking | Where-Object Kind -eq "modified").Count
    $addedCount = @($ranking | Where-Object Kind -eq "added").Count
    $removedCount = @($ranking | Where-Object Kind -eq "removed").Count
    $remainingOriginal = @(Get-ChildItem -LiteralPath $originalSourceRoot -Recurse -File -Filter *.cs).Count
    $remainingCurrent = @(Get-ChildItem -LiteralPath $currentSourceRoot -Recurse -File -Filter *.cs).Count
    $diffCount = @(Get-ChildItem -LiteralPath $diffRoot -Recurse -File -Filter *.diff).Count

    @(
        "original=$originalPath"
        "current_patch=$currentPatchPath"
        "identical_pairs_removed=$identicalCount"
        "remaining_original_csharp=$remainingOriginal"
        "remaining_current_csharp=$remainingCurrent"
        "modified_files=$modifiedCount"
        "added_files=$addedCount"
        "removed_files=$removedCount"
        "file_diffs=$diffCount"
        "smallest_diff=$($ranking[0].RelativePath)"
        "smallest_changed_lines=$($ranking[0].ChangedLines)"
    ) | Set-Content -LiteralPath (Join-Path $outputRoot "analysis-summary.txt") -Encoding utf8
}

function Show-SmallestDiff([object[]]$ranking) {
    $ranking | Select-Object -First 20 | Format-Table -AutoSize
    Write-Output "Analysis: $outputRoot"
    Write-Output "Smallest diff: $($ranking[0].RelativePath) ($($ranking[0].ChangedLines) changed line)"
}

function Get-FilesByRelativePath([string]$root) {
    $prefix = $root.TrimEnd('\') + '\'
    $files = @{}
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.cs) {
        $files[(Get-RelativePath $file.FullName $prefix)] = $file.FullName
    }
    return $files
}

function Get-RelativePath([string]$path, [string]$prefix) {
    Assert-PathStartsWith $path $prefix
    return $path.Substring($prefix.Length)
}

function Assert-PathStartsWith([string]$path, [string]$prefix) {
    if (-not $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped its expected root: $path"
    }
}

function Assert-LastExitCode([string]$operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE"
    }
}

Invoke-Analysis
