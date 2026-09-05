param(
    [string]$OriginalExe = "..\..\Magicka_orig.exe",
    [string]$OutputDirectory = "..\..\tmp\inventory-box-simple-static-run"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2

$experimentRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $experimentRoot "..\.."))

function Resolve-ArgumentPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) {
        return [System.IO.Path]::GetFullPath($path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $experimentRoot $path))
}

$originalPath = Resolve-ArgumentPath $OriginalExe
$outputRoot = Resolve-ArgumentPath $OutputDirectory
$patchedPath = Join-Path $outputRoot "Magicka.exe"
$originalSource = Join-Path $outputRoot "original.cs"
$patchedSource = Join-Path $outputRoot "patched.cs"
$fullDiff = Join-Path $outputRoot "InventoryBox.full.diff"
$actualDiff = Join-Path $outputRoot "InventoryBox.diff"
$expectedDiff = Join-Path $experimentRoot "expected\InventoryBox.cs.diff"
$toolOutput = Join-Path $outputRoot "tools"

function Invoke-SimpleStaticVariant {
    Assert-FreshOutput
    Restore-Decompiler
    Build-RequiredTools
    Create-PatchedExecutable
    Decompile-InventoryBox $originalPath $originalSource
    Decompile-InventoryBox $patchedPath $patchedSource
    Remove-DecompilerComments
    Create-CanonicalDiff
    Assert-DiffMatchesExpected
    Write-Output "result=PASS"
    Write-Output "patched_executable=$patchedPath"
    Write-Output "verified_diff=$actualDiff"
}

function Assert-FreshOutput {
    if (-not (Test-Path -LiteralPath $originalPath -PathType Leaf)) {
        throw "Original executable does not exist: $originalPath"
    }
    if (Test-Path -LiteralPath $outputRoot) {
        throw "Refusing to overwrite existing output: $outputRoot"
    }
    New-Item -ItemType Directory -Path $outputRoot, $toolOutput | Out-Null
}

function Restore-Decompiler {
    Push-Location $experimentRoot
    try {
        & dotnet tool restore
        Assert-ExitCode "ILSpy restore"
    }
    finally {
        Pop-Location
    }
}

function Build-RequiredTools {
    & dotnet build (Join-Path $experimentRoot "src\StaticPatcher\StaticPatcher.csproj") `
        --configuration Release --output (Join-Path $toolOutput "patcher")
    Assert-ExitCode "static patcher build"
    & dotnet build (Join-Path $experimentRoot "src\SourceCommentStripper\SourceCommentStripper.csproj") `
        --configuration Release --output (Join-Path $toolOutput "stripper")
    Assert-ExitCode "comment stripper build"
}

function Create-PatchedExecutable {
    & dotnet (Join-Path $toolOutput "patcher\StaticPatcher.dll") $originalPath $patchedPath
    Assert-ExitCode "static patch"
}

function Decompile-InventoryBox([string]$assemblyPath, [string]$destination) {
    Push-Location $experimentRoot
    try {
        $source = @(& dotnet tool run ilspycmd -- `
            --disable-updatecheck `
            --referencepath $repositoryRoot `
            --languageversion CSharp3 `
            --type "Magicka.GameLogic.UI.InventoryBox" `
            $assemblyPath)
        Assert-ExitCode "InventoryBox decompilation"
        [System.IO.File]::WriteAllLines($destination, [string[]]$source)
    }
    finally {
        Pop-Location
    }
}

function Remove-DecompilerComments {
    & dotnet (Join-Path $toolOutput "stripper\SourceCommentStripper.dll") $outputRoot
    Assert-ExitCode "comment stripping"
}

function Create-CanonicalDiff {
    & git -c core.safecrlf=false diff --no-index --output=$fullDiff --unified=3 -- $originalSource $patchedSource
    if ($LASTEXITCODE -notin 0, 1) {
        throw "git diff failed."
    }

    $lines = @(Get-Content -LiteralPath $fullDiff)
    $hunkStart = [Array]::FindIndex($lines, [Predicate[string]] { param($line) $line.StartsWith("@@ ") })
    if ($hunkStart -lt 0) {
        throw "The decompiled methods have no diff."
    }
    $lines[$hunkStart..($lines.Count - 1)] |
        Set-Content -LiteralPath $actualDiff -Encoding utf8
}

function Assert-DiffMatchesExpected {
    $expected = Read-NormalizedText $expectedDiff
    $actual = Read-NormalizedText $actualDiff
    if ($actual -cne $expected) {
        throw "The decompiled method diff does not match the expected string."
    }
}

function Read-NormalizedText([string]$path) {
    return [System.IO.File]::ReadAllText($path).Replace("`r`n", "`n").TrimEnd([char[]]"`r`n")
}

function Assert-ExitCode([string]$operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE"
    }
}

Invoke-SimpleStaticVariant
