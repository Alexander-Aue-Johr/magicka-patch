[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MagickaDir,
    [string]$PayloadDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$diagnosticsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..')
)
if ([string]::IsNullOrWhiteSpace($PayloadDir)) {
    $PayloadDir = Join-Path $diagnosticsRoot 'payload'
}

$payloadRoot = [System.IO.Path]::GetFullPath($PayloadDir)
$gameRoot = [System.IO.Path]::GetFullPath($MagickaDir)
$requiredFiles = @(
    'Magicka.exe',
    'PolygonHead.dll',
    'Magicka.GcDiagnostics.dll'
)

$manifestPath = Join-Path $payloadRoot 'payload-manifest.tsv'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Diagnostic payload manifest is missing: $manifestPath"
}

$expectedHashes = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($line in (Get-Content -LiteralPath $manifestPath)) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $parts = $line.Split(
        @("`t"),
        2,
        [System.StringSplitOptions]::None
    )
    if ($parts.Length -ne 2 `
            -or $parts[0] -notmatch '^[0-9a-fA-F]{64}$' `
            -or $requiredFiles -notcontains $parts[1] `
            -or $expectedHashes.ContainsKey($parts[1])) {
        throw "Invalid diagnostic payload manifest line: $line"
    }

    $expectedHashes.Add($parts[1], $parts[0].ToLowerInvariant())
}

if ($expectedHashes.Count -ne $requiredFiles.Count) {
    throw "Diagnostic payload manifest does not cover exactly the required files."
}

foreach ($fileName in $requiredFiles) {
    $source = Join-Path $payloadRoot $fileName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Diagnostic payload file is missing: $source"
    }

    $actualHash = (Get-FileHash `
            -LiteralPath $source `
            -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHashes[$fileName]) {
        throw "Diagnostic payload hash mismatch: $source"
    }
}

foreach ($fileName in @('Magicka.exe', 'PolygonHead.dll')) {
    $target = Join-Path $gameRoot $fileName
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "This is not a Magicka game directory; missing $target"
    }
}

$backupRoot = Join-Path $gameRoot 'CommunityPatch\gc-retention-backups'
$backupName = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') `
    + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$backupDir = Join-Path $backupRoot $backupName
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

$previouslyMissing = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
$stateLines = [System.Collections.Generic.List[string]]::new()
foreach ($fileName in $requiredFiles) {
    $target = Join-Path $gameRoot $fileName
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        Copy-Item -LiteralPath $target `
            -Destination (Join-Path $backupDir $fileName) `
            -Force
        $stateLines.Add("present`t$fileName")
    }
    else {
        [void]$previouslyMissing.Add($fileName)
        $stateLines.Add("missing`t$fileName")
    }
}

[System.IO.File]::WriteAllLines(
    (Join-Path $backupDir 'install-state.tsv'),
    $stateLines,
    [System.Text.UTF8Encoding]::new($false)
)

try {
    foreach ($fileName in $requiredFiles) {
        Copy-Item -LiteralPath (Join-Path $payloadRoot $fileName) `
            -Destination (Join-Path $gameRoot $fileName) `
            -Force
    }
}
catch {
    $installError = $_
    $rollbackErrors = [System.Collections.Generic.List[string]]::new()
    foreach ($fileName in $requiredFiles) {
        $target = Join-Path $gameRoot $fileName
        $backup = Join-Path $backupDir $fileName
        try {
            if (Test-Path -LiteralPath $backup -PathType Leaf) {
                Copy-Item -LiteralPath $backup -Destination $target -Force
            }
            elseif ($previouslyMissing.Contains($fileName) `
                    -and (Test-Path -LiteralPath $target -PathType Leaf)) {
                Remove-Item -LiteralPath $target -Force
            }
        }
        catch {
            $rollbackErrors.Add("$fileName`: $($_.Exception.Message)")
        }
    }

    if ($rollbackErrors.Count -ne 0) {
        throw "Installation failed ($($installError.Exception.Message)); " `
            + "rollback also failed: $($rollbackErrors -join '; ')"
    }

    throw "Installation failed and was rolled back: " `
        + $installError.Exception.Message
}

Write-Host "Installed GC-retention diagnostic payload in $gameRoot"
Write-Host "Previous state of all overwritten files backed up to $backupDir"
