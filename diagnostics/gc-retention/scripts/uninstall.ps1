[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MagickaDir,
    [string]$BackupDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$gameRoot = [System.IO.Path]::GetFullPath($MagickaDir)
$backupRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $gameRoot 'CommunityPatch\gc-retention-backups')
)
$requiredFiles = @(
    'Magicka.exe',
    'PolygonHead.dll',
    'Magicka.GcDiagnostics.dll'
)

if ([string]::IsNullOrWhiteSpace($BackupDir)) {
    $selectedBackup = Get-ChildItem -LiteralPath $backupRoot `
            -Directory `
            -ErrorAction Stop |
        Where-Object {
            Test-Path -LiteralPath (
                Join-Path $_.FullName 'install-state.tsv'
            ) -PathType Leaf
        } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $selectedBackup) {
        throw "No GC-retention installation backup was found in $backupRoot"
    }

    $BackupDir = $selectedBackup.FullName
}

$selectedBackupRoot = [System.IO.Path]::GetFullPath($BackupDir)
$backupPrefix = $backupRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
if (-not $selectedBackupRoot.StartsWith(
        $backupPrefix,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
    throw "Backup directory must be inside $backupRoot"
}

$statePath = Join-Path $selectedBackupRoot 'install-state.tsv'
if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    throw "Installation state is missing: $statePath"
}

$originalState = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($line in (Get-Content -LiteralPath $statePath)) {
    $parts = $line.Split(
        @("`t"),
        2,
        [System.StringSplitOptions]::None
    )
    if ($parts.Length -ne 2 `
            -or @('present', 'missing') -notcontains $parts[0] `
            -or $requiredFiles -notcontains $parts[1] `
            -or $originalState.ContainsKey($parts[1])) {
        throw "Invalid installation state line: $line"
    }

    $originalState.Add($parts[1], $parts[0])
}

if ($originalState.Count -ne $requiredFiles.Count) {
    throw "Installation state does not cover exactly the installed files."
}

foreach ($fileName in $requiredFiles) {
    if ($originalState[$fileName] -eq 'present') {
        $backupFile = Join-Path $selectedBackupRoot $fileName
        if (-not (Test-Path -LiteralPath $backupFile -PathType Leaf)) {
            throw "Required backup file is missing: $backupFile"
        }
    }
}

$rollbackRoot = Join-Path (
    $gameRoot
) 'CommunityPatch\gc-retention-uninstall-rollbacks'
$rollbackName = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') `
    + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$rollbackDir = Join-Path $rollbackRoot $rollbackName
New-Item -ItemType Directory -Force -Path $rollbackDir | Out-Null

$currentState = [System.Collections.Generic.Dictionary[string, string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($fileName in $requiredFiles) {
    $target = Join-Path $gameRoot $fileName
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        Copy-Item -LiteralPath $target `
            -Destination (Join-Path $rollbackDir $fileName) `
            -Force
        $currentState.Add($fileName, 'present')
    }
    else {
        $currentState.Add($fileName, 'missing')
    }
}

try {
    foreach ($fileName in $requiredFiles) {
        $target = Join-Path $gameRoot $fileName
        if ($originalState[$fileName] -eq 'present') {
            Copy-Item -LiteralPath (
                Join-Path $selectedBackupRoot $fileName
            ) -Destination $target -Force
        }
        elseif (Test-Path -LiteralPath $target -PathType Leaf) {
            Remove-Item -LiteralPath $target -Force
        }
    }
}
catch {
    $uninstallError = $_
    $rollbackErrors = [System.Collections.Generic.List[string]]::new()
    foreach ($fileName in $requiredFiles) {
        $target = Join-Path $gameRoot $fileName
        try {
            if ($currentState[$fileName] -eq 'present') {
                Copy-Item -LiteralPath (
                    Join-Path $rollbackDir $fileName
                ) -Destination $target -Force
            }
            elseif (Test-Path -LiteralPath $target -PathType Leaf) {
                Remove-Item -LiteralPath $target -Force
            }
        }
        catch {
            $rollbackErrors.Add("$fileName`: $($_.Exception.Message)")
        }
    }

    if ($rollbackErrors.Count -ne 0) {
        throw "Uninstall failed ($($uninstallError.Exception.Message)); " `
            + "rollback also failed: $($rollbackErrors -join '; ')"
    }

    throw "Uninstall failed and was rolled back: " `
        + $uninstallError.Exception.Message
}

Write-Host "Restored the pre-diagnostic game state from $selectedBackupRoot"
Write-Host "The replaced diagnostic state was backed up to $rollbackDir"
