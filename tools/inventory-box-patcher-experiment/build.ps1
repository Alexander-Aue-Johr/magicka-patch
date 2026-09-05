param(
    [string]$OriginalExe = "..\..\Magicka_orig.exe",
    [string]$CurrentPatchExe = "..\..\Magicka.exe",
    [string]$OutputDirectory = "..\..\tmp\inventory-box-patcher-run",
    [switch]$SkipSourceAnalysis
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
$currentPatchPath = Resolve-ArgumentPath $CurrentPatchExe
$outputRoot = Resolve-ArgumentPath $OutputDirectory
$backupDirectory = Join-Path $outputRoot "backup"
$staticDirectory = Join-Path $outputRoot "static"
$runtimeDirectory = Join-Path $outputRoot "runtime"
$auditDirectory = Join-Path $outputRoot "audit"
$toolBuildDirectory = Join-Path $outputRoot "tool-build"
$sourceAnalysisDirectory = Join-Path $outputRoot "source-analysis"
$expectedDiffPath = Join-Path $experimentRoot "expected\InventoryBox.cs.diff"

function Invoke-Experiment {
    Assert-ExperimentInputs
    Prepare-FreshExperimentDirectory
    Backup-OriginalExecutable
    Restore-ExperimentTools
    Build-ExperimentTools
    if (-not $SkipSourceAnalysis) {
        Analyze-SourceDifference
        Assert-SelectedSourceDiff
    }
    Create-StaticPatchVariant
    Create-RuntimePatchVariant
    Test-RuntimePatchRegistrationAgainstOriginal
    Test-RuntimePatchBehavior
    Verify-AssemblyChanges
    Verify-DecompiledStaticDiff
    Verify-RuntimeEffectiveDiff
    Write-ExperimentSummary
    Write-Output "Experiment: $outputRoot"
}

function Assert-ExperimentInputs {
    if (-not (Test-Path -LiteralPath $originalPath -PathType Leaf)) {
        throw "Original executable does not exist: $originalPath"
    }
    if (-not (Test-Path -LiteralPath $currentPatchPath -PathType Leaf)) {
        throw "Current patch executable does not exist: $currentPatchPath"
    }
    if (-not (Test-Path -LiteralPath $expectedDiffPath -PathType Leaf)) {
        throw "Expected diff does not exist: $expectedDiffPath"
    }
}

function Prepare-FreshExperimentDirectory {
    if (Test-Path -LiteralPath $outputRoot) {
        throw "Refusing to overwrite existing experiment directory: $outputRoot"
    }

    New-Item -ItemType Directory -Path `
        $backupDirectory, `
        $staticDirectory, `
        $runtimeDirectory, `
        $auditDirectory, `
        $toolBuildDirectory | Out-Null
}

function Backup-OriginalExecutable {
    $backupPath = Join-Path $backupDirectory "Magicka_orig.exe"
    Copy-Item -LiteralPath $originalPath -Destination $backupPath
    if ((Get-FileHash $originalPath -Algorithm SHA256).Hash -ne
        (Get-FileHash $backupPath -Algorithm SHA256).Hash) {
        throw "The original executable backup hash does not match."
    }
}

function Restore-ExperimentTools {
    Push-Location $experimentRoot
    try {
        & dotnet tool restore
        Assert-LastExitCode "ILSpy restore"
    }
    finally {
        Pop-Location
    }
}

function Build-ExperimentTools {
    Build-Project "src\StaticPatcher\StaticPatcher.csproj" "static-patcher"
    Build-Project "src\RuntimeLoaderInjector\RuntimeLoaderInjector.csproj" "runtime-loader-injector"
    Build-Project "src\RuntimeSelfTest\RuntimeSelfTest.csproj" "runtime-self-test"
    Build-Project "src\RuntimeRegistrationProbe\RuntimeRegistrationProbe.csproj" "runtime-registration-probe"
    Build-Project "src\AssemblyVerifier\AssemblyVerifier.csproj" "assembly-verifier"
    Build-Project "src\SourceCommentStripper\SourceCommentStripper.csproj" "comment-stripper"
}

function Build-Project([string]$relativeProjectPath, [string]$outputName) {
    $projectPath = Join-Path $experimentRoot $relativeProjectPath
    $projectOutput = Join-Path $toolBuildDirectory $outputName
    & dotnet build $projectPath --configuration Release --output $projectOutput
    Assert-LastExitCode "build of $relativeProjectPath"
}

function Analyze-SourceDifference {
    $analysisScript = Join-Path $experimentRoot "analyze.ps1"
    & powershell -ExecutionPolicy Bypass -File $analysisScript `
        -OriginalExe $originalPath `
        -CurrentPatchExe $currentPatchPath `
        -OutputDirectory $sourceAnalysisDirectory
    Assert-LastExitCode "source analysis"
}

function Assert-SelectedSourceDiff {
    $selectedDiff = Join-Path $sourceAnalysisDirectory "file-diffs\Magicka\GameLogic\UI\InventoryBox.cs.diff"
    $canonicalDiff = Join-Path $auditDirectory "selected-current-patch.diff"
    Write-CanonicalDiff $selectedDiff $canonicalDiff
    Assert-TextFilesEqual $expectedDiffPath $canonicalDiff "selected current-patch diff"
}

function Create-StaticPatchVariant {
    $patcher = Join-Path $toolBuildDirectory "static-patcher\StaticPatcher.dll"
    $output = Join-Path $staticDirectory "Magicka.exe"
    & dotnet $patcher $originalPath $output
    Assert-LastExitCode "static patching"
}

function Create-RuntimePatchVariant {
    $injector = Join-Path $toolBuildDirectory "runtime-loader-injector\RuntimeLoaderInjector.dll"
    $hostOutput = Join-Path $runtimeDirectory "Magicka.exe"
    & dotnet $injector $originalPath $hostOutput
    Assert-LastExitCode "runtime loader injection"

    $runtimeBuild = Join-Path $toolBuildDirectory "runtime-self-test"
    Copy-Item -LiteralPath `
        (Join-Path $runtimeBuild "Magicka.InventoryBox.RuntimePatch.dll"), `
        (Join-Path $runtimeBuild "0Harmony.dll") `
        -Destination $runtimeDirectory
}

function Test-RuntimePatchBehavior {
    $runtimeSelfTestDirectory = Join-Path $toolBuildDirectory "runtime-self-test"
    Push-Location $runtimeSelfTestDirectory
    try {
        & ".\RuntimeSelfTest.exe" 2>&1 |
            Tee-Object -FilePath (Join-Path $auditDirectory "runtime-self-test.txt")
        Assert-LastExitCode "CLR-2 runtime patch self-test"
        Copy-Item -LiteralPath ".\inventory-box-runtime-audit.txt" `
            -Destination (Join-Path $auditDirectory "runtime-patch-audit.txt")
    }
    finally {
        Pop-Location
    }
}

function Test-RuntimePatchRegistrationAgainstOriginal {
    $probeDirectory = Join-Path $toolBuildDirectory "runtime-registration-probe"
    Push-Location $probeDirectory
    try {
        & ".\RuntimeRegistrationProbe.exe" $originalPath 2>&1 |
            Tee-Object -FilePath (Join-Path $auditDirectory "runtime-original-registration.txt")
        Assert-LastExitCode "runtime registration against the original Magicka assembly"
        Copy-Item -LiteralPath ".\inventory-box-runtime-audit.txt" `
            -Destination (Join-Path $auditDirectory "runtime-original-registration-audit.txt")
    }
    finally {
        Pop-Location
    }
}

function Verify-AssemblyChanges {
    $verifier = Join-Path $toolBuildDirectory "assembly-verifier\AssemblyVerifier.dll"
    & dotnet $verifier `
        $originalPath `
        $currentPatchPath `
        (Join-Path $staticDirectory "Magicka.exe") `
        (Join-Path $runtimeDirectory "Magicka.exe") `
        (Join-Path $runtimeDirectory "Magicka.InventoryBox.RuntimePatch.dll") `
        (Join-Path $runtimeDirectory "0Harmony.dll") `
        $auditDirectory
    Assert-LastExitCode "assembly verification"
}

function Verify-DecompiledStaticDiff {
    $csharpDirectory = Join-Path $auditDirectory "csharp"
    New-Item -ItemType Directory -Path $csharpDirectory | Out-Null
    $originalSource = Join-Path $csharpDirectory "original.cs"
    $currentSource = Join-Path $csharpDirectory "current-patch.cs"
    $staticSource = Join-Path $csharpDirectory "static-patch.cs"

    Decompile-InventoryBox $originalPath $originalSource
    Decompile-InventoryBox $currentPatchPath $currentSource
    Decompile-InventoryBox (Join-Path $staticDirectory "Magicka.exe") $staticSource
    Remove-AuditSourceComments $csharpDirectory
    Assert-TextFilesEqual $currentSource $staticSource "static decompilation versus current patch"

    $actualDiff = Join-Path $auditDirectory "static-patch-full.diff"
    & git -c core.safecrlf=false diff --no-index --output=$actualDiff --unified=3 -- $originalSource $staticSource
    if ($LASTEXITCODE -notin 0, 1) {
        throw "git diff failed for the static patch."
    }

    $canonicalDiff = Join-Path $auditDirectory "static-patch.diff"
    Write-CanonicalDiff $actualDiff $canonicalDiff
    Assert-TextFilesEqual $expectedDiffPath $canonicalDiff "decompiled static-patch diff"
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
        Assert-LastExitCode "InventoryBox decompilation of $assemblyPath"
        [System.IO.File]::WriteAllLines($destination, [string[]]$source)
    }
    finally {
        Pop-Location
    }
}

function Remove-AuditSourceComments([string]$csharpDirectory) {
    $stripper = Join-Path $toolBuildDirectory "comment-stripper\SourceCommentStripper.dll"
    & dotnet $stripper $csharpDirectory
    Assert-LastExitCode "audit-source comment stripping"
}

function Verify-RuntimeEffectiveDiff {
    $runtimeAuditPath = Join-Path $auditDirectory "runtime-original-registration-audit.txt"
    $auditLines = @(Get-Content -LiteralPath $runtimeAuditPath)
    if ($auditLines -notcontains "result=PASS" -or
        $auditLines -notcontains "patch_end=InventoryBox screen size") {
        throw "The runtime audit does not contain a successful InventoryBox patch result."
    }
    $start = [Array]::IndexOf($auditLines, "csharp_context_diff_begin")
    $end = [Array]::IndexOf($auditLines, "csharp_context_diff_end")
    if ($start -lt 0 -or $end -le $start) {
        throw "The runtime audit does not contain a complete C# diff."
    }

    $runtimeDiffPath = Join-Path $auditDirectory "runtime-effective.diff"
    $auditLines[($start + 1)..($end - 1)] |
        Set-Content -LiteralPath $runtimeDiffPath -Encoding utf8
}

function Write-CanonicalDiff([string]$fullDiffPath, [string]$canonicalPath) {
    $lines = @(Get-Content -LiteralPath $fullDiffPath)
    $hunkStart = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index].StartsWith("@@ ")) {
            $hunkStart = $index
            break
        }
    }
    if ($hunkStart -lt 0) {
        throw "No C# diff hunk found in $fullDiffPath"
    }

    $lines[$hunkStart..($lines.Count - 1)] |
        Set-Content -LiteralPath $canonicalPath -Encoding utf8
}

function Assert-TextFilesEqual([string]$expectedPath, [string]$actualPath, [string]$label) {
    $expected = Read-NormalizedText $expectedPath
    $actual = Read-NormalizedText $actualPath
    if ($expected -cne $actual) {
        throw "$label does not match $expectedDiffPath"
    }
}

function Read-NormalizedText([string]$path) {
    return [System.IO.File]::ReadAllText($path).Replace("`r`n", "`n").TrimEnd([char[]]"`r`n")
}

function Write-ExperimentSummary {
    $artifactPaths = @(
        (Join-Path $staticDirectory "Magicka.exe"),
        (Join-Path $runtimeDirectory "Magicka.exe"),
        (Join-Path $runtimeDirectory "Magicka.InventoryBox.RuntimePatch.dll"),
        (Join-Path $runtimeDirectory "0Harmony.dll")
    )
    $summary = New-Object System.Collections.Generic.List[string]
    $summary.Add("result=PASS")
    $summary.Add("selected_file=Magicka\GameLogic\UI\InventoryBox.cs")
    $summary.Add("selected_change=mTextBoxEffect.ScreenSize = new Vector2(screenSize.X, screenSize.Y);")
    $summary.Add("static_diff=PASS")
    $summary.Add("runtime_registration=PASS")
    $summary.Add("runtime_original_assembly_probe=PASS")
    $summary.Add("runtime_behavior=PASS")
    $summary.Add("assembly_verification=PASS")

    foreach ($artifactPath in $artifactPaths) {
        $file = Get-Item -LiteralPath $artifactPath
        $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash
        $relativePath = $artifactPath.Substring($outputRoot.TrimEnd('\').Length + 1)
        $summary.Add("artifact=$relativePath|bytes=$($file.Length)|sha256=$hash")
    }

    $summary | Set-Content -LiteralPath (Join-Path $outputRoot "experiment-summary.txt") -Encoding utf8
    $summary
}

function Assert-LastExitCode([string]$operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE"
    }
}

Invoke-Experiment
