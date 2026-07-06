[CmdletBinding()]
param(
    [string]$Version = "",
    [int]$BuildNumber = -1,
    [string]$OldExeVersion = "",
    [string]$Flutter = "",
    [string]$OutputDir = "",
    [switch]$SkipExeVersionPatch,
    [switch]$SkipBuild,
    [switch]$SkipAutoUpdaterUi,
    [switch]$KeepStage
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Join-PathChecked {
    param(
        [Parameter(Mandatory = $true)][string]$Base,
        [Parameter(Mandatory = $true)][string]$Child
    )
    return [System.IO.Path]::GetFullPath((Join-Path $Base $Child))
}

function Read-PubspecVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    $match = Select-String -Path $Path -Pattern '^\s*version:\s*(?<version>[^\s#]+)' | Select-Object -First 1
    if ($null -eq $match) {
        throw "No version entry found in $Path"
    }

    $fullVersion = $match.Matches[0].Groups["version"].Value.Trim()
    $semanticVersion = ($fullVersion -split '\+')[0]
    if ($semanticVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Unsupported version '$fullVersion' in $Path"
    }

    [pscustomobject]@{
        Full = $fullVersion
        Semantic = $semanticVersion
    }
}

function Read-AppPatchVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    $text = [System.IO.File]::ReadAllText($Path)
    $match = [System.Text.RegularExpressions.Regex]::Match(
        $text,
        "static\s+const\s+patchVersion\s*=\s*'(?<version>[^']+)'"
    )
    if (-not $match.Success) {
        throw "No AppConstants.patchVersion entry found in $Path"
    }

    return $match.Groups["version"].Value
}

function Get-BuildNumberForVersion {
    param(
        [Parameter(Mandatory = $true)][string]$SemanticVersion,
        [int]$RequestedBuildNumber
    )

    if ($RequestedBuildNumber -ge 0) {
        return $RequestedBuildNumber
    }

    if ($SemanticVersion -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z.-]+)?$') {
        throw "Cannot infer Flutter build number from version '$SemanticVersion'. Pass -BuildNumber."
    }

    return [int]$Matches[3]
}

function Format-FlutterVersion {
    param(
        [Parameter(Mandatory = $true)][string]$SemanticVersion,
        [Parameter(Mandatory = $true)][int]$RequestedBuildNumber
    )

    if ($SemanticVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Unsupported release version '$SemanticVersion'. Use a semantic version like 0.0.14."
    }

    $resolvedBuildNumber = Get-BuildNumberForVersion $SemanticVersion $RequestedBuildNumber
    return "$SemanticVersion+$resolvedBuildNumber"
}

function Set-TextFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

function Replace-RegexRequired {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Replacement,
        [Parameter(Mandatory = $true)][string]$Description,
        [System.Text.RegularExpressions.RegexOptions]$Options = [System.Text.RegularExpressions.RegexOptions]::Multiline
    )

    $text = [System.IO.File]::ReadAllText($Path)
    $regex = New-Object System.Text.RegularExpressions.Regex($Pattern, $Options)
    $matches = $regex.Matches($text)
    if ($matches.Count -lt 1) {
        throw "Could not update $Description in $Path"
    }

    $newText = $regex.Replace($text, $Replacement)
    if ($newText -ne $text) {
        Set-TextFile $Path $newText
        Write-Host "Updated ${Description}: $Path" -ForegroundColor DarkGray
    }
}

function Replace-RegexIfPresent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Replacement,
        [Parameter(Mandatory = $true)][string]$Description,
        [System.Text.RegularExpressions.RegexOptions]$Options = [System.Text.RegularExpressions.RegexOptions]::Multiline
    )

    $text = [System.IO.File]::ReadAllText($Path)
    $regex = New-Object System.Text.RegularExpressions.Regex -ArgumentList $Pattern, $Options
    if (-not $regex.IsMatch($text)) {
        return
    }

    $newText = $regex.Replace($text, $Replacement)
    if ($newText -ne $text) {
        Set-TextFile $Path $newText
        Write-Host "Updated ${Description}: $Path" -ForegroundColor DarkGray
    }
}

function Replace-LiteralIfPresent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$OldValue,
        [Parameter(Mandatory = $true)][string]$NewValue,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($OldValue -eq $NewValue) {
        return
    }

    $text = [System.IO.File]::ReadAllText($Path)
    if (-not $text.Contains($OldValue)) {
        return
    }

    Set-TextFile $Path ($text.Replace($OldValue, $NewValue))
    Write-Host "Updated ${Description}: $Path" -ForegroundColor DarkGray
}

function Set-UpdaterLockPackageVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$FullVersion
    )

    $text = [System.IO.File]::ReadAllText($Path)
    $pattern = '(?ms)(^\s+magicka_community_patch_installer_ui:\s+.*?^\s+source:\s+path\s+^\s+version:\s*")([^"]+)(")'
    $options = [System.Text.RegularExpressions.RegexOptions](([int][System.Text.RegularExpressions.RegexOptions]::Multiline) -bor ([int][System.Text.RegularExpressions.RegexOptions]::Singleline))
    $regex = New-Object System.Text.RegularExpressions.Regex -ArgumentList $pattern, $options
    $matches = $regex.Matches($text)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one path dependency lock entry for magicka_community_patch_installer_ui in $Path, found $($matches.Count)"
    }

    $newText = $regex.Replace(
        $text,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            return $match.Groups[1].Value + $FullVersion + $match.Groups[3].Value
        }
    )
    if ($newText -ne $text) {
        Set-TextFile $Path $newText
        Write-Host "Updated updater lock dependency version: $Path" -ForegroundColor DarkGray
    }
}

function Initialize-ByteSearchType {
    if ($null -ne ('ReleaseByteSearch' -as [type])) {
        return
    }

    Add-Type -TypeDefinition @'
using System;

public static class ReleaseByteSearch {
    public static int Count(byte[] haystack, byte[] needle) {
        if (haystack == null || needle == null || needle.Length == 0 || haystack.Length < needle.Length) return 0;
        int count = 0;
        for (int i = 0; i <= haystack.Length - needle.Length; i++) {
            int j = 0;
            for (; j < needle.Length; j++) {
                if (haystack[i + j] != needle[j]) break;
            }
            if (j == needle.Length) count++;
        }
        return count;
    }

    public static int IndexOf(byte[] haystack, byte[] needle) {
        if (haystack == null || needle == null || needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (int i = 0; i <= haystack.Length - needle.Length; i++) {
            int j = 0;
            for (; j < needle.Length; j++) {
                if (haystack[i + j] != needle[j]) break;
            }
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
'@
}

function Set-ExeVersionString {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$OldVersion,
        [Parameter(Mandatory = $true)][string]$NewVersion
    )

    if ($OldVersion.Length -ne $NewVersion.Length) {
        throw "Cannot binary patch $Path from '$OldVersion' to '$NewVersion': string lengths differ. Rebuild or patch the assembly source instead."
    }

    Initialize-ByteSearchType
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $oldBytes = [System.Text.Encoding]::Unicode.GetBytes($OldVersion)
    $newBytes = [System.Text.Encoding]::Unicode.GetBytes($NewVersion)
    $oldCount = [ReleaseByteSearch]::Count($bytes, $oldBytes)
    $newCount = [ReleaseByteSearch]::Count($bytes, $newBytes)

    if ($OldVersion -eq $NewVersion) {
        if ($newCount -ne 1) {
            throw "Expected exactly one UTF-16 version string '$NewVersion' in $Path, found $newCount"
        }
        Write-Host "Magicka.exe already contains version $NewVersion" -ForegroundColor DarkGray
        return
    }

    if ($oldCount -eq 0 -and $newCount -eq 1) {
        Write-Host "Magicka.exe already contains version $NewVersion" -ForegroundColor DarkGray
        return
    }

    if ($oldCount -ne 1) {
        throw "Expected exactly one UTF-16 version string '$OldVersion' in $Path, found $oldCount. Pass -OldExeVersion or -SkipExeVersionPatch."
    }

    $offset = [ReleaseByteSearch]::IndexOf($bytes, $oldBytes)
    [System.Array]::Copy($newBytes, 0, $bytes, $offset, $newBytes.Length)
    [System.IO.File]::WriteAllBytes($Path, $bytes)

    $verifyBytes = [System.IO.File]::ReadAllBytes($Path)
    $verifyCount = [ReleaseByteSearch]::Count($verifyBytes, $newBytes)
    if ($verifyCount -ne 1) {
        throw "Version patch verification failed for $Path. Expected one '$NewVersion', found $verifyCount."
    }

    Write-Host "Updated Magicka.exe version: $OldVersion -> $NewVersion" -ForegroundColor DarkGray
}

function Set-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)][string]$SemanticVersion,
        [Parameter(Mandatory = $true)][string]$FullVersion,
        [Parameter(Mandatory = $true)][string]$OldSemanticVersion,
        [Parameter(Mandatory = $true)][string]$OldFullVersion,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$InstallerProject,
        [Parameter(Mandatory = $true)][string]$UpdaterProject
    )

    $installerPubspecPath = Join-PathChecked $InstallerProject 'pubspec.yaml'
    $updaterPubspecPath = Join-PathChecked $UpdaterProject 'pubspec.yaml'
    $updaterLockPath = Join-PathChecked $UpdaterProject 'pubspec.lock'
    $installerMainPath = Join-PathChecked $InstallerProject 'lib\main.dart'
    $widgetTestPath = Join-PathChecked $InstallerProject 'test\widget_test.dart'
    $installerReadmePath = Join-PathChecked $InstallerProject 'README.md'
    $updaterReadmePath = Join-PathChecked $UpdaterProject 'README.md'

    Replace-RegexRequired $installerPubspecPath '^\s*version:\s*[^\s#]+' "version: $FullVersion" 'installer pubspec version'
    Replace-RegexRequired $updaterPubspecPath '^\s*version:\s*[^\s#]+' "version: $FullVersion" 'auto-updater pubspec version'
    Set-UpdaterLockPackageVersion $updaterLockPath $FullVersion
    Replace-RegexRequired $installerMainPath "static\s+const\s+patchVersion\s*=\s*'[^']+'" "static const patchVersion = '$SemanticVersion'" 'AppConstants.patchVersion'
    Replace-RegexRequired $installerMainPath 'MAGICKA COMMUNITY PATCH \d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "MAGICKA COMMUNITY PATCH $SemanticVersion" 'installer header version'
    Replace-RegexRequired $widgetTestPath 'MAGICKA COMMUNITY PATCH \d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "MAGICKA COMMUNITY PATCH $SemanticVersion" 'widget test header version'
    Replace-RegexRequired $installerReadmePath '^Version:\s+\*\*[^*]+\*\*' "Version: **$SemanticVersion**" 'installer README version'

    Replace-RegexIfPresent $installerReadmePath 'v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "v$SemanticVersion" 'installer README release tag examples'
    Replace-RegexIfPresent $installerReadmePath 'magicka-community-patch-\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\.zip' "magicka-community-patch-$SemanticVersion.zip" 'installer README ZIP examples'
    Replace-RegexIfPresent $updaterReadmePath '"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?"' "`"$SemanticVersion`"" 'auto-updater README command version example'

    foreach ($path in @($installerReadmePath, $updaterReadmePath)) {
        Replace-LiteralIfPresent $path $OldSemanticVersion $SemanticVersion 'README version examples'
    }

    if (-not $SkipExeVersionPatch) {
        $exePath = Join-PathChecked $RepoRoot 'Magicka.exe'
        $resolvedOldExeVersion = if ([string]::IsNullOrWhiteSpace($OldExeVersion)) { $OldSemanticVersion } else { $OldExeVersion }
        Set-ExeVersionString $exePath $resolvedOldExeVersion $SemanticVersion
    }
}

function Resolve-Flutter {
    param([string]$Requested)

    if (-not [string]::IsNullOrWhiteSpace($Requested)) {
        if (-not (Test-Path -LiteralPath $Requested)) {
            throw "Flutter executable not found: $Requested"
        }
        return (Resolve-Path $Requested).Path
    }

    $command = Get-Command flutter -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:FLUTTER_ROOT)) {
        $candidates += (Join-Path $env:FLUTTER_ROOT 'bin\flutter.bat')
    }
    $candidates += 'R:\flutter\bin\flutter.bat'

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Flutter was not found. Pass -Flutter C:\path\to\flutter.bat or add flutter to PATH."
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    Write-Host ""
    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor Cyan
    Write-Host "  cwd: $WorkingDirectory" -ForegroundColor DarkGray

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-File {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file missing: $Path"
    }
}

function Assert-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Required directory missing: $Path"
    }
}

function Remove-PathInside {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = [System.IO.Path]::GetFullPath((Resolve-Path $Path).Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath((Resolve-Path $AllowedRoot).Path)
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside output directory: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

function Assert-ZipEntries {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string[]]$RequiredEntries
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
        foreach ($entry in $RequiredEntries) {
            if ($entries -notcontains $entry) {
                throw "Release ZIP is missing required entry: $entry"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir '..'))
$installerProject = Join-PathChecked $repoRoot 'magicka-patch-installer-ui'
$updaterProject = Join-PathChecked $installerProject 'src\magicka-community-patch-auto-updater-ui'
$installerPubspec = Join-PathChecked $installerProject 'pubspec.yaml'
$updaterPubspec = Join-PathChecked $updaterProject 'pubspec.yaml'
$installerMain = Join-PathChecked $installerProject 'lib\main.dart'

$installerVersion = Read-PubspecVersion $installerPubspec
$originalInstallerVersion = $installerVersion
$originalAppVersion = Read-AppPatchVersion $installerMain

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $targetSemanticVersion = $Version.Trim()
    $targetFullVersion = Format-FlutterVersion $targetSemanticVersion $BuildNumber
    $oldSemanticVersion = $originalInstallerVersion.Semantic
    if ($oldSemanticVersion -eq $targetSemanticVersion -and $originalAppVersion -ne $targetSemanticVersion) {
        $oldSemanticVersion = $originalAppVersion
    }

    Write-Host "Setting release version: $($originalInstallerVersion.Full) -> $targetFullVersion" -ForegroundColor Green
    Set-ProjectVersion `
        -SemanticVersion $targetSemanticVersion `
        -FullVersion $targetFullVersion `
        -OldSemanticVersion $oldSemanticVersion `
        -OldFullVersion $originalInstallerVersion.Full `
        -RepoRoot $repoRoot `
        -InstallerProject $installerProject `
        -UpdaterProject $updaterProject
    $installerVersion = Read-PubspecVersion $installerPubspec
}

$updaterVersion = Read-PubspecVersion $updaterPubspec
if ($installerVersion.Full -ne $updaterVersion.Full) {
    throw "Version mismatch: installer has $($installerVersion.Full), updater has $($updaterVersion.Full)"
}

$mainText = Get-Content -LiteralPath $installerMain -Raw
if ($mainText -notmatch "static\s+const\s+patchVersion\s*=\s*'$([regex]::Escape($installerVersion.Semantic))'") {
    throw "AppConstants.patchVersion in $installerMain does not match $($installerVersion.Semantic)"
}

$version = $installerVersion.Semantic
$flutterExe = Resolve-Flutter $Flutter
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-PathChecked $repoRoot 'release'
}
else {
    $OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}

$stageDir = Join-PathChecked $OutputDir "magicka-community-patch-$version"
$zipPath = Join-PathChecked $OutputDir "magicka-community-patch-$version.zip"

Write-Host "Release version: $version ($($installerVersion.Full))" -ForegroundColor Green
Write-Host "Output ZIP: $zipPath" -ForegroundColor Green

if ($SkipBuild -and -not [string]::IsNullOrWhiteSpace($Version)) {
    Write-Warning "Version files were updated but -SkipBuild is set. Existing Flutter build artifacts may still contain the old version. Run without -SkipBuild for the final release package."
}

if (-not $SkipBuild) {
    Invoke-Tool $flutterExe @('pub', 'get') $installerProject
    Invoke-Tool $flutterExe @('build', 'windows', '--release') $installerProject

    if (-not $SkipAutoUpdaterUi) {
        Invoke-Tool $flutterExe @('pub', 'get') $updaterProject
        Invoke-Tool $flutterExe @('build', 'windows', '--release') $updaterProject
    }
}

$installerRelease = Join-PathChecked $installerProject 'build\windows\x64\runner\Release'
$updaterRelease = Join-PathChecked $updaterProject 'build\windows\x64\runner\Release'
$installerExe = Join-PathChecked $installerRelease 'magicka-community-patch-installer-ui.exe'
$updaterExe = Join-PathChecked $updaterRelease 'magicka-community-patch-auto-updater-ui.exe'

Assert-File (Join-PathChecked $repoRoot 'Magicka.exe')
Assert-File (Join-PathChecked $repoRoot 'PolygonHead.dll')
Assert-File $installerExe
Assert-File (Join-PathChecked $installerRelease 'flutter_windows.dll')
Assert-Directory (Join-PathChecked $installerRelease 'data')
if (-not $SkipAutoUpdaterUi) {
    Assert-File $updaterExe
    Assert-File (Join-PathChecked $updaterRelease 'flutter_windows.dll')
    Assert-Directory (Join-PathChecked $updaterRelease 'data')
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Remove-PathInside $stageDir $OutputDir
if (Test-Path -LiteralPath $zipPath) {
    Remove-PathInside $zipPath $OutputDir
}
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'Magicka.exe') -Destination (Join-PathChecked $stageDir 'Magicka.exe')
Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'PolygonHead.dll') -Destination (Join-PathChecked $stageDir 'PolygonHead.dll')
Copy-Item -LiteralPath $installerExe -Destination (Join-PathChecked $stageDir 'MagickaPatchInstaller.exe')
Copy-Item -LiteralPath (Join-PathChecked $installerRelease 'flutter_windows.dll') -Destination (Join-PathChecked $stageDir 'flutter_windows.dll')
Copy-Item -LiteralPath (Join-PathChecked $installerRelease 'data') -Destination (Join-PathChecked $stageDir 'data') -Recurse

$installerStage = Join-PathChecked $stageDir 'tools\installer'
New-Item -ItemType Directory -Force -Path $installerStage | Out-Null
Copy-Item -LiteralPath $installerExe -Destination (Join-PathChecked $installerStage 'MagickaPatchInstaller.exe')
Copy-Item -LiteralPath $installerExe -Destination (Join-PathChecked $installerStage 'MagickaPatchTool.exe')
Copy-Item -LiteralPath $installerExe -Destination (Join-PathChecked $installerStage 'MagickaPatchUninstaller.exe')
Copy-Item -LiteralPath (Join-PathChecked $installerRelease 'flutter_windows.dll') -Destination (Join-PathChecked $installerStage 'flutter_windows.dll')
Copy-Item -LiteralPath (Join-PathChecked $installerRelease 'data') -Destination (Join-PathChecked $installerStage 'data') -Recurse

if (-not $SkipAutoUpdaterUi) {
    $updaterStage = Join-PathChecked $stageDir 'tools\auto-updater'
    New-Item -ItemType Directory -Force -Path $updaterStage | Out-Null
    Copy-Item -LiteralPath $updaterExe -Destination (Join-PathChecked $updaterStage 'MagickaPatchAutoUpdater.exe')
    Copy-Item -LiteralPath (Join-PathChecked $updaterRelease 'flutter_windows.dll') -Destination (Join-PathChecked $updaterStage 'flutter_windows.dll')
    Copy-Item -LiteralPath (Join-PathChecked $updaterRelease 'data') -Destination (Join-PathChecked $updaterStage 'data') -Recurse
}

Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force

$requiredEntries = @(
    'MagickaPatchInstaller.exe',
    'Magicka.exe',
    'PolygonHead.dll',
    'flutter_windows.dll',
    'data/flutter_assets/AssetManifest.bin',
    'tools/installer/MagickaPatchInstaller.exe',
    'tools/installer/MagickaPatchTool.exe',
    'tools/installer/MagickaPatchUninstaller.exe',
    'tools/installer/flutter_windows.dll',
    'tools/installer/data/flutter_assets/AssetManifest.bin'
)
if (-not $SkipAutoUpdaterUi) {
    $requiredEntries += @(
        'tools/auto-updater/MagickaPatchAutoUpdater.exe',
        'tools/auto-updater/flutter_windows.dll',
        'tools/auto-updater/data/flutter_assets/AssetManifest.bin'
    )
}
Assert-ZipEntries $zipPath $requiredEntries

$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
$zipItem = Get-Item -LiteralPath $zipPath
$fileCount = (Get-ChildItem -LiteralPath $stageDir -Recurse -File | Measure-Object).Count

Write-Host ""
Write-Host "Created release package:" -ForegroundColor Green
Write-Host "  $zipPath"
Write-Host "  Version: $version"
Write-Host "  Size: $($zipItem.Length) bytes"
Write-Host "  Files staged: $fileCount"
Write-Host "  SHA256: $($hash.Hash)"

if ($KeepStage) {
    Write-Host ""
    Write-Host "Stage directory kept for inspection: $stageDir" -ForegroundColor DarkGray
}
else {
    Remove-PathInside $stageDir $OutputDir
    Write-Host ""
    Write-Host "Stage directory removed. Use -KeepStage to keep it for inspection." -ForegroundColor DarkGray
}
