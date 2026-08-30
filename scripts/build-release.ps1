[CmdletBinding()]
param(
    [string]$Version = "",
    [int]$BuildNumber = -1,
    [string]$OldExeVersion = "",
    [string]$Flutter = "",
    [string]$Mono = "",
    [string]$OutputDir = "",
    [string]$Locale = "",
    [string]$MagickaDir = "",
    [switch]$SkipExeVersionPatch,
    [switch]$SkipBuild,
    [switch]$SkipAutoUpdaterUi,
    [switch]$SkipSteamPayloadSync,
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

function Add-UniquePath {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IList]$List,
        [AllowEmptyString()][string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    foreach ($existing in $List) {
        if ([string]::Equals($existing, $fullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }

    $List.Add($fullPath) | Out-Null
}

function Normalize-ValvePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return $Path.Replace('\\', '\').Replace('/', '\')
}

function Read-RegistryString {
    param(
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$ValueName
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'reg.exe'
    $startInfo.Arguments = "query `"$Key`" /v `"$ValueName`""
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEnd()
        [void]$process.StandardError.ReadToEnd()
        $process.WaitForExit()
        $exitCode = $process.ExitCode
    }
    catch {
        return ""
    }
    finally {
        $process.Dispose()
    }

    if ($exitCode -ne 0) {
        return ""
    }

    foreach ($line in ($stdout -split "`r?`n")) {
        if ($line -match "^\s*$([regex]::Escape($ValueName))\s+REG_\w+\s+(?<value>.+?)\s*$") {
            return $Matches["value"].Trim()
        }
    }
    return ""
}

function Get-SteamDirectories {
    $dirs = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        Add-UniquePath $dirs (Join-Path ${env:ProgramFiles(x86)} 'Steam')
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        Add-UniquePath $dirs (Join-Path $env:ProgramFiles 'Steam')
    }
    Add-UniquePath $dirs 'C:\Steam'

    foreach ($key in @(
            'HKCU\Software\Valve\Steam',
            'HKLM\Software\Valve\Steam',
            'HKLM\Software\WOW6432Node\Valve\Steam'
        )) {
        foreach ($valueName in @('SteamPath', 'InstallPath')) {
            $value = Read-RegistryString $key $valueName
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                Add-UniquePath $dirs (Normalize-ValvePath $value)
            }
        }
    }

    return @($dirs | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
}

function Get-SteamLibraryDirectories {
    $libraries = New-Object System.Collections.Generic.List[string]

    foreach ($steamDir in Get-SteamDirectories) {
        Add-UniquePath $libraries $steamDir
        $libraryFile = Join-PathChecked $steamDir 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $libraryFile -PathType Leaf)) {
            continue
        }

        $text = [System.IO.File]::ReadAllText($libraryFile)
        foreach ($match in [regex]::Matches($text, '"path"\s+"(?<path>[^"]+)"')) {
            Add-UniquePath $libraries (Normalize-ValvePath $match.Groups["path"].Value)
        }
        foreach ($match in [regex]::Matches($text, '"\d+"\s+"(?<path>(?:[A-Za-z]:|\\\\)[^"]+)"')) {
            Add-UniquePath $libraries (Normalize-ValvePath $match.Groups["path"].Value)
        }
    }

    return @($libraries | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
}

function Test-MagickaPayloadDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Test-Path -LiteralPath (Join-PathChecked $Path 'Magicka.exe') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-PathChecked $Path 'PolygonHead.dll') -PathType Leaf)
}

function Resolve-MagickaDirectory {
    param([AllowEmptyString()][string]$Requested)

    if (-not [string]::IsNullOrWhiteSpace($Requested)) {
        $resolved = [System.IO.Path]::GetFullPath($Requested)
        if (-not (Test-MagickaPayloadDirectory $resolved)) {
            throw "Magicka payload files were not found in ${resolved}. Expected Magicka.exe and PolygonHead.dll."
        }
        return $resolved
    }

    foreach ($envName in @('MAGICKA_DIR', 'MAGICKA_GAME_DIR')) {
        $envValue = [Environment]::GetEnvironmentVariable($envName)
        if (-not [string]::IsNullOrWhiteSpace($envValue) -and (Test-MagickaPayloadDirectory $envValue)) {
            return [System.IO.Path]::GetFullPath($envValue)
        }
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($libraryDir in Get-SteamLibraryDirectories) {
        $manifest = Join-PathChecked $libraryDir 'steamapps\appmanifest_42910.acf'
        if (Test-Path -LiteralPath $manifest -PathType Leaf) {
            $manifestText = [System.IO.File]::ReadAllText($manifest)
            $installDirMatch = [regex]::Match($manifestText, '"installdir"\s+"(?<dir>[^"]+)"')
            if ($installDirMatch.Success) {
                $commonDir = Join-PathChecked (Join-PathChecked $libraryDir 'steamapps') 'common'
                Add-UniquePath $candidates (Join-PathChecked $commonDir (Normalize-ValvePath $installDirMatch.Groups["dir"].Value))
            }
        }

        Add-UniquePath $candidates (Join-PathChecked $libraryDir 'steamapps\common\Magicka')
    }

    $fallbackCandidates = @(
        'C:\Steam\steamapps\common\Magicka',
        'D:\SteamLibrary\steamapps\common\Magicka',
        'G:\SteamLibrary\steamapps\common\Magicka'
    )
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $fallbackCandidates += Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Magicka'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $fallbackCandidates += Join-Path $env:ProgramFiles 'Steam\steamapps\common\Magicka'
    }

    foreach ($candidate in $fallbackCandidates) {
        Add-UniquePath $candidates $candidate
    }

    foreach ($candidate in $candidates) {
        if (Test-MagickaPayloadDirectory $candidate) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    throw "Could not find the Steam Magicka directory. Pass -MagickaDir `"C:\path\to\Steam\steamapps\common\Magicka`"."
}

function Sync-ReleasePayloadFromMagickaDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$GameDir,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    foreach ($fileName in @('Magicka.exe', 'PolygonHead.dll')) {
        $source = Join-PathChecked $GameDir $fileName
        $destination = Join-PathChecked $RepoRoot $fileName
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Required payload file missing in ${GameDir}: $fileName"
        }
        Copy-Item -LiteralPath $source -Destination $destination -Force
        Write-Host "Synced $fileName from Steam Magicka folder" -ForegroundColor DarkGray
    }
}

function Copy-ReleasePayloadToDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    Assert-Directory $DestinationDir
    foreach ($fileName in @('Magicka.exe', 'PolygonHead.dll', 'Magicka.GcDiagnostics.dll')) {
        Copy-Item -LiteralPath (Join-PathChecked $RepoRoot $fileName) -Destination (Join-PathChecked $DestinationDir $fileName) -Force
    }
    $diagnosticsSource = Join-PathChecked $RepoRoot 'release-package\gc-diagnostics'
    Assert-Directory $diagnosticsSource
    $diagnosticsTarget = Join-PathChecked $DestinationDir 'gc-diagnostics'
    if (Test-Path -LiteralPath $diagnosticsTarget) {
        Remove-PathInside $diagnosticsTarget $DestinationDir
    }
    Copy-Item -LiteralPath $diagnosticsSource -Destination $diagnosticsTarget -Recurse -Force
    $languageSource = Join-PathChecked $RepoRoot 'release-package\optional-languages\zho'
    Assert-Directory $languageSource
    $languageLineBreakCheck = Join-PathChecked $RepoRoot 'scripts\check-chinese-language-line-breaks.ps1'
    Assert-File $languageLineBreakCheck
    & $languageLineBreakCheck -LanguageDirectory $languageSource
    $languageParent = Join-PathChecked $DestinationDir 'optional-languages'
    New-Item -ItemType Directory -Force -Path $languageParent | Out-Null
    $languageTarget = Join-PathChecked $languageParent 'zho'
    if (Test-Path -LiteralPath $languageTarget) {
        Remove-PathInside $languageTarget $DestinationDir
    }
    Copy-Item -LiteralPath $languageSource -Destination $languageTarget -Recurse -Force
    Write-Host "Copied payload files next to installer EXE: $DestinationDir" -ForegroundColor DarkGray
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

function Resolve-ReleaseLocale {
    param([AllowEmptyString()][string]$Requested)

    if ([string]::IsNullOrWhiteSpace($Requested)) {
        return ""
    }

    $normalized = $Requested.Trim().Replace('_', '-')
    if ([string]::Equals($normalized, 'system', [System.StringComparison]::OrdinalIgnoreCase)) {
        return ""
    }

    $supported = @(
        'es-AR',
        'ru-RU',
        'uk-UA',
        'de-DE',
        'ja-JP',
        'en-US',
        'fr-FR',
        'pt-BR',
        'ko-KR',
        'zh-CN',
        'cs-CZ'
    )

    foreach ($locale in $supported) {
        if ([string]::Equals($normalized, $locale, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $locale
        }
    }

    throw "Unsupported release locale '$Requested'. Supported locales: $($supported -join ', ')"
}

function Get-PreviousPatchVersion {
    param([Parameter(Mandatory = $true)][string]$SemanticVersion)

    if ($SemanticVersion -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        return ""
    }

    $patch = [int]$Matches[3]
    if ($patch -le 0) {
        return ""
    }

    return "$($Matches[1]).$($Matches[2]).$($patch - 1)"
}

function Resolve-OldExeVersionForRelease {
    param(
        [Parameter(Mandatory = $true)][string]$TargetSemanticVersion,
        [Parameter(Mandatory = $true)][string]$ProjectOldSemanticVersion
    )

    if (-not [string]::IsNullOrWhiteSpace($OldExeVersion)) {
        return $OldExeVersion
    }

    $expectedPrevious = Get-PreviousPatchVersion $TargetSemanticVersion
    if ([string]::IsNullOrWhiteSpace($expectedPrevious)) {
        return $ProjectOldSemanticVersion
    }

    if ($ProjectOldSemanticVersion -ne $expectedPrevious) {
        Write-Warning "Project files currently report $ProjectOldSemanticVersion, but release $TargetSemanticVersion normally updates Magicka.exe from $expectedPrevious. Using $expectedPrevious for the EXE version check. Pass -OldExeVersion to override."
    }

    return $expectedPrevious
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

function Read-AllBytesWithRetry {
    param([Parameter(Mandatory = $true)][string]$Path)

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            return [System.IO.File]::ReadAllBytes($Path)
        }
        catch [System.IO.IOException] {
            if ($attempt -eq 20) {
                throw
            }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Write-AllBytesWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][byte[]]$Bytes
    )

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            [System.IO.File]::WriteAllBytes($Path, $Bytes)
            return
        }
        catch [System.IO.IOException] {
            if ($attempt -eq 20) {
                throw
            }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Get-FileHashWithRetry {
    param([Parameter(Mandatory = $true)][string]$Path)

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            return Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop
        }
        catch {
            if ($attempt -eq 20) {
                throw
            }
            Start-Sleep -Milliseconds 250
        }
    }
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
    $bytes = Read-AllBytesWithRetry $Path
    $oldBytes = [System.Text.Encoding]::Unicode.GetBytes($OldVersion)
    $newBytes = [System.Text.Encoding]::Unicode.GetBytes($NewVersion)
    $oldCount = [ReleaseByteSearch]::Count($bytes, $oldBytes)
    $newCount = [ReleaseByteSearch]::Count($bytes, $newBytes)
    $explicitOldVersion = -not [string]::IsNullOrWhiteSpace($script:OldExeVersion)

    if ($OldVersion -eq $NewVersion) {
        if ($newCount -eq 1) {
            Write-Host "Magicka.exe already contains version $NewVersion" -ForegroundColor DarkGray
            return
        }
        if ($newCount -eq 0 -and -not $explicitOldVersion) {
            Write-Warning "Magicka.exe does not contain UTF-16 patch version '$NewVersion'. Skipping binary EXE version patch."
            return
        }
        throw "Expected exactly one UTF-16 version string '$NewVersion' in $Path, found $newCount"
    }

    if ($oldCount -eq 0 -and $newCount -eq 1) {
        Write-Host "Magicka.exe already contains version $NewVersion" -ForegroundColor DarkGray
        return
    }

    if ($oldCount -ne 1) {
        if ($oldCount -eq 0 -and -not $explicitOldVersion) {
            Write-Warning "Magicka.exe does not contain expected UTF-16 patch version '$OldVersion'. Skipping binary EXE version patch. If the game-side version still needs changing, update the Steam Magicka.exe or pass -OldExeVersion."
            return
        }
        throw "Expected exactly one UTF-16 version string '$OldVersion' in $Path, found $oldCount. Pass -OldExeVersion or -SkipExeVersionPatch."
    }

    $offset = [ReleaseByteSearch]::IndexOf($bytes, $oldBytes)
    [System.Array]::Copy($newBytes, 0, $bytes, $offset, $newBytes.Length)
    Write-AllBytesWithRetry $Path $bytes

    $verifyBytes = Read-AllBytesWithRetry $Path
    $verifyCount = [ReleaseByteSearch]::Count($verifyBytes, $newBytes)
    if ($verifyCount -ne 1) {
        throw "Version patch verification failed for $Path. Expected one '$NewVersion', found $verifyCount."
    }

    Write-Host "Updated Magicka.exe version: $OldVersion -> $NewVersion" -ForegroundColor DarkGray
}

function Sync-VersionedExeToMagickaDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$GameDir
    )

    $sourcePath = Join-PathChecked $RepoRoot 'Magicka.exe'
    $destinationPath = Join-PathChecked $GameDir 'Magicka.exe'
    Assert-File $sourcePath
    Assert-File $destinationPath

    $sourceHash = (Get-FileHashWithRetry $sourcePath).Hash
    $destinationHash = (Get-FileHashWithRetry $destinationPath).Hash
    if ($sourceHash -eq $destinationHash) {
        Write-Host "Versioned Magicka.exe already matches the Steam Magicka folder" -ForegroundColor DarkGray
        return
    }

    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force

    $destinationHash = (Get-FileHashWithRetry $destinationPath).Hash
    if ($sourceHash -ne $destinationHash) {
        throw "Failed to synchronize versioned Magicka.exe back to $GameDir"
    }

    Write-Host "Synced versioned Magicka.exe back to Steam Magicka folder" -ForegroundColor DarkGray
}

function Set-ProjectVersion {
    param(
        [Parameter(Mandatory = $true)][string]$SemanticVersion,
        [Parameter(Mandatory = $true)][string]$FullVersion,
        [Parameter(Mandatory = $true)][string]$OldSemanticVersion,
        [Parameter(Mandatory = $true)][string]$OldFullVersion,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$InstallerProject,
        [Parameter(Mandatory = $true)][string]$UpdaterProject,
        [Parameter(Mandatory = $true)][string]$OldExeVersionForPatch
    )

    $installerPubspecPath = Join-PathChecked $InstallerProject 'pubspec.yaml'
    $updaterPubspecPath = Join-PathChecked $UpdaterProject 'pubspec.yaml'
    $updaterLockPath = Join-PathChecked $UpdaterProject 'pubspec.lock'
    $installerMainPath = Join-PathChecked $InstallerProject 'lib\main.dart'
    $localizationPath = Join-PathChecked $InstallerProject 'lib\localization.dart'
    $widgetTestPath = Join-PathChecked $InstallerProject 'test\widget_test.dart'
    $rootReadmePath = Join-PathChecked $RepoRoot 'README.md'
    $installerReadmePath = Join-PathChecked $InstallerProject 'README.md'
    $updaterReadmePath = Join-PathChecked $UpdaterProject 'README.md'
    $communityPatchInfoPath = Join-PathChecked $RepoRoot 'docs\injected-source\Magicka.CommunityPatch\CommunityPatchInfo.cs'

    Replace-RegexRequired $installerPubspecPath '^\s*version:\s*[^\s#]+' "version: $FullVersion" 'installer pubspec version'
    Replace-RegexRequired $updaterPubspecPath '^\s*version:\s*[^\s#]+' "version: $FullVersion" 'auto-updater pubspec version'
    Set-UpdaterLockPackageVersion $updaterLockPath $FullVersion
    Replace-RegexRequired $installerMainPath "static\s+const\s+patchVersion\s*=\s*'[^']+'" "static const patchVersion = '$SemanticVersion'" 'AppConstants.patchVersion'
    Replace-RegexIfPresent $installerMainPath 'MAGICKA COMMUNITY PATCH \d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "MAGICKA COMMUNITY PATCH $SemanticVersion" 'installer header version'
    Replace-RegexRequired $localizationPath 'MAGICKA COMMUNITY PATCH \d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "MAGICKA COMMUNITY PATCH $SemanticVersion" 'localized installer header version'
    Replace-RegexRequired $localizationPath '(MAGICKA \u793E\u533A\u8865\u4E01 )\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "`${1}$SemanticVersion" 'Chinese installer header version'
    Replace-RegexRequired $widgetTestPath 'MAGICKA COMMUNITY PATCH \d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "MAGICKA COMMUNITY PATCH $SemanticVersion" 'widget test header version'
    Replace-RegexRequired $widgetTestPath 'Patch-Update \d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "Patch-Update $SemanticVersion" 'widget test localized updater version'
    Replace-RegexRequired $communityPatchInfoPath 'return\s+"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?";' "return `"$SemanticVersion`";" 'documented community patch version'
    Replace-RegexRequired $rootReadmePath '\[Installer\]\(https://github\.com/Alexander-Aue-Johr/magicka-patch/releases/download/\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?/magicka-community-patch-\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?-installer\.zip\)' "[Installer](https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/$SemanticVersion/magicka-community-patch-$SemanticVersion-installer.zip)" 'root README installer download link'
    Replace-RegexIfPresent $rootReadmePath '\[Linux installer\]\(https://github\.com/Alexander-Aue-Johr/magicka-patch/releases/download/\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?/magicka-community-patch-\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?-linux-installer\.zip\)' "[Linux installer](https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/$SemanticVersion/magicka-community-patch-$SemanticVersion-linux-installer.zip)" 'root README Linux installer download link'
    Replace-RegexRequired $rootReadmePath '\[Files only / manual installation\]\(https://github\.com/Alexander-Aue-Johr/magicka-patch/releases/download/\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?/magicka-community-patch-\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?-files-only\.zip\)' "[Files only / manual installation](https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/$SemanticVersion/magicka-community-patch-$SemanticVersion-files-only.zip)" 'root README files-only download link'
    Replace-RegexRequired $installerReadmePath '^Version:\s+\*\*[^*]+\*\*' "Version: **$SemanticVersion**" 'installer README version'

    Replace-RegexIfPresent $installerReadmePath 'v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?' "v$SemanticVersion" 'installer README release tag examples'
    Replace-RegexIfPresent $installerReadmePath 'magicka-community-patch-\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?-files-only\.zip' "magicka-community-patch-$SemanticVersion-files-only.zip" 'installer README files-only ZIP examples'
    Replace-RegexIfPresent $installerReadmePath 'magicka-community-patch-\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?-installer\.zip' "magicka-community-patch-$SemanticVersion-installer.zip" 'installer README ZIP examples'
    Replace-RegexIfPresent $updaterReadmePath '"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?"' "`"$SemanticVersion`"" 'auto-updater README command version example'

    foreach ($path in @($installerReadmePath, $updaterReadmePath)) {
        Replace-LiteralIfPresent $path $OldSemanticVersion $SemanticVersion 'README version examples'
    }

    if (-not $SkipExeVersionPatch) {
        $exePath = Join-PathChecked $RepoRoot 'Magicka.exe'
        Set-ExeVersionString $exePath $OldExeVersionForPatch $SemanticVersion
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

function Resolve-Mono {
    param([string]$Requested)

    if (-not [string]::IsNullOrWhiteSpace($Requested)) {
        if (-not (Test-Path -LiteralPath $Requested -PathType Leaf)) {
            throw "Mono executable not found: $Requested"
        }
        return (Resolve-Path $Requested).Path
    }

    $command = Get-Command mono -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    foreach ($candidate in @(
            'C:\Program Files\Mono\bin\mono.exe',
            'C:\Program Files (x86)\Mono\bin\mono.exe'
        )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Mono was not found. Install Mono 6.12.0.206, pass -Mono C:\path\to\mono.exe, or add mono to PATH. Release validation requires the Mono JIT gate."
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

function Assert-ZipContainsOnly {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string[]]$ExpectedEntries
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' } | Sort-Object)
        $expected = @($ExpectedEntries | Sort-Object)
        $difference = @(Compare-Object -ReferenceObject $expected -DifferenceObject $entries)
        if ($difference.Count -ne 0) {
            throw "Files-only ZIP contents differ from the expected four files: $($difference | Out-String)"
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
$packageReadme = Join-PathChecked $repoRoot 'release-package\README.txt'
$packageSettingsTemplate = Join-PathChecked $repoRoot 'release-package\patch-settings.ini'

$installerVersion = Read-PubspecVersion $installerPubspec
$originalInstallerVersion = $installerVersion
$originalAppVersion = Read-AppPatchVersion $installerMain

if ($SkipSteamPayloadSync) {
    Write-Warning "Steam payload sync skipped. The release will use Magicka.exe and PolygonHead.dll from $repoRoot."
}
else {
    $resolvedMagickaDir = Resolve-MagickaDirectory $MagickaDir
    Write-Host "Steam Magicka folder: $resolvedMagickaDir" -ForegroundColor Green
    Sync-ReleasePayloadFromMagickaDirectory -GameDir $resolvedMagickaDir -RepoRoot $repoRoot
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $targetSemanticVersion = $Version.Trim()
    $targetFullVersion = Format-FlutterVersion $targetSemanticVersion $BuildNumber
    $oldSemanticVersion = $originalInstallerVersion.Semantic
    if ($oldSemanticVersion -eq $targetSemanticVersion -and $originalAppVersion -ne $targetSemanticVersion) {
        $oldSemanticVersion = $originalAppVersion
    }
    $oldExeVersionForPatch = Resolve-OldExeVersionForRelease `
        -TargetSemanticVersion $targetSemanticVersion `
        -ProjectOldSemanticVersion $oldSemanticVersion

    Write-Host "Setting release version: $($originalInstallerVersion.Full) -> $targetFullVersion" -ForegroundColor Green
    Set-ProjectVersion `
        -SemanticVersion $targetSemanticVersion `
        -FullVersion $targetFullVersion `
        -OldSemanticVersion $oldSemanticVersion `
        -OldFullVersion $originalInstallerVersion.Full `
        -RepoRoot $repoRoot `
        -InstallerProject $installerProject `
        -UpdaterProject $updaterProject `
        -OldExeVersionForPatch $oldExeVersionForPatch

    if (-not $SkipSteamPayloadSync -and -not $SkipExeVersionPatch) {
        Sync-VersionedExeToMagickaDirectory -RepoRoot $repoRoot -GameDir $resolvedMagickaDir
    }
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
$releaseLocale = Resolve-ReleaseLocale $Locale
$releaseSuffix = if ([string]::IsNullOrWhiteSpace($releaseLocale)) { "" } else { "-$releaseLocale" }
$flutterExe = Resolve-Flutter $Flutter
$monoExe = Resolve-Mono $Mono
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-PathChecked $repoRoot 'release'
}
else {
    $OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}

$stageDir = Join-PathChecked $OutputDir "magicka-community-patch-${version}-installer$releaseSuffix"
$zipPath = Join-PathChecked $OutputDir "magicka-community-patch-${version}-installer$releaseSuffix.zip"
$filesOnlyStageDir = Join-PathChecked $OutputDir "magicka-community-patch-${version}-files-only$releaseSuffix"
$filesOnlyZipPath = Join-PathChecked $OutputDir "magicka-community-patch-${version}-files-only$releaseSuffix.zip"

Write-Host "Release version: $version ($($installerVersion.Full))" -ForegroundColor Green
if (-not [string]::IsNullOrWhiteSpace($releaseLocale)) {
    Write-Host "Release locale: $releaseLocale" -ForegroundColor Green
}
Write-Host "Output ZIP: $zipPath" -ForegroundColor Green
Write-Host "Files-only ZIP: $filesOnlyZipPath" -ForegroundColor Green

if ($SkipBuild -and -not [string]::IsNullOrWhiteSpace($Version)) {
    Write-Warning "Version files were updated but -SkipBuild is set. Existing Flutter build artifacts may still contain the old version. Run without -SkipBuild for the final release package."
}

if (-not $SkipBuild) {
    Invoke-Tool $flutterExe @('pub', 'get') $installerProject
    $buildArgs = @('build', 'windows', '--release')
    if (-not [string]::IsNullOrWhiteSpace($releaseLocale)) {
        $buildArgs += "--dart-define=APP_LOCALE=$releaseLocale"
    }
    Invoke-Tool $flutterExe $buildArgs $installerProject
}

$installerRelease = Join-PathChecked $installerProject 'build\windows\x64\runner\Release'
$installerExe = Join-PathChecked $installerRelease 'magicka-community-patch-installer-ui.exe'

Assert-File (Join-PathChecked $repoRoot 'Magicka.exe')
Assert-File (Join-PathChecked $repoRoot 'PolygonHead.dll')
Assert-File (Join-PathChecked $repoRoot 'Magicka.GcDiagnostics.dll')
$payloadValidatorProject = Join-PathChecked $repoRoot 'tools\gc-retention-payload-validator\PayloadValidator.csproj'
Assert-File $payloadValidatorProject
$gcAnalyzerTestsProject = Join-PathChecked $repoRoot 'tools\gc-retention-analyzer-tests\Magicka.GcAnalyzer.Tests.csproj'
Assert-File $gcAnalyzerTestsProject
$dotnetCommand = Get-Command dotnet -ErrorAction Stop
Invoke-Tool $dotnetCommand.Source @(
    'run',
    '--project', $gcAnalyzerTestsProject,
    '--configuration', 'Release'
) $repoRoot
$monoRoot = Split-Path -Parent (Split-Path -Parent $monoExe)
$monoCompiler = Join-PathChecked $monoRoot 'lib\mono\4.5\mcs.exe'
Assert-File $monoCompiler
$monoProbeSource = Join-PathChecked $repoRoot 'tools\mono-startup-probe\Program.cs'
Assert-File $monoProbeSource
$monoProbeDir = Join-PathChecked $repoRoot 'tmp\release-validation'
New-Item -ItemType Directory -Force -Path $monoProbeDir | Out-Null
$monoProbe = Join-PathChecked $monoProbeDir 'MonoStartupProbe.exe'
Invoke-Tool $monoExe @(
    $monoCompiler,
    '-nologo',
    '-sdk:2',
    "-out:$monoProbe",
    $monoProbeSource
) $repoRoot
Invoke-Tool $monoExe @(
    $monoProbe,
    (Join-PathChecked $repoRoot 'Magicka.exe')
) $repoRoot
Invoke-Tool $dotnetCommand.Source @(
    'run',
    '--project', $payloadValidatorProject,
    '--configuration', 'Release',
    '--', $repoRoot
) $repoRoot
$gcDiagnosticsDirectory = Join-PathChecked $repoRoot 'release-package\gc-diagnostics'
Assert-Directory $gcDiagnosticsDirectory
$requiredGcDiagnosticsFiles = @(
    'Magicka.GcAnalyzer.exe',
    'Magicka.GcAnalyzer.exe.config',
    'Microsoft.Diagnostics.Runtime.dll',
    'LICENSE-MIT.txt',
    'THIRD_PARTY_NOTICES.txt'
)
foreach ($fileName in $requiredGcDiagnosticsFiles) {
    Assert-File (Join-PathChecked $gcDiagnosticsDirectory $fileName)
}
Assert-File $packageReadme
Assert-File $packageSettingsTemplate
Assert-File $installerExe
Assert-File (Join-PathChecked $installerRelease 'flutter_windows.dll')
Assert-Directory (Join-PathChecked $installerRelease 'data')

Copy-ReleasePayloadToDirectory -RepoRoot $repoRoot -DestinationDir $installerRelease
Assert-File (Join-PathChecked $installerRelease 'Magicka.exe')
Assert-File (Join-PathChecked $installerRelease 'PolygonHead.dll')
Assert-File (Join-PathChecked $installerRelease 'Magicka.GcDiagnostics.dll')
Assert-Directory (Join-PathChecked $installerRelease 'gc-diagnostics')

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Remove-PathInside $stageDir $OutputDir
Remove-PathInside $filesOnlyStageDir $OutputDir
if (Test-Path -LiteralPath $zipPath) {
    Remove-PathInside $zipPath $OutputDir
}
if (Test-Path -LiteralPath $filesOnlyZipPath) {
    Remove-PathInside $filesOnlyZipPath $OutputDir
}
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path $filesOnlyStageDir | Out-Null

Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'Magicka.exe') -Destination (Join-PathChecked $stageDir 'Magicka.exe')
Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'PolygonHead.dll') -Destination (Join-PathChecked $stageDir 'PolygonHead.dll')
Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'Magicka.GcDiagnostics.dll') -Destination (Join-PathChecked $stageDir 'Magicka.GcDiagnostics.dll')
Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'release-package\gc-diagnostics') -Destination (Join-PathChecked $stageDir 'gc-diagnostics') -Recurse -Force
Copy-Item -LiteralPath $packageReadme -Destination (Join-PathChecked $stageDir 'README.txt')
Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'release-package\optional-languages') -Destination (Join-PathChecked $stageDir 'optional-languages') -Recurse -Force
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
    Copy-Item -LiteralPath $installerExe -Destination (Join-PathChecked $updaterStage 'MagickaPatchAutoUpdater.exe')
    Copy-Item -LiteralPath (Join-PathChecked $installerRelease 'flutter_windows.dll') -Destination (Join-PathChecked $updaterStage 'flutter_windows.dll')
    Copy-Item -LiteralPath (Join-PathChecked $installerRelease 'data') -Destination (Join-PathChecked $updaterStage 'data') -Recurse
}

Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'Magicka.exe') -Destination (Join-PathChecked $filesOnlyStageDir 'Magicka.exe')
Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'PolygonHead.dll') -Destination (Join-PathChecked $filesOnlyStageDir 'PolygonHead.dll')
Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'Magicka.GcDiagnostics.dll') -Destination (Join-PathChecked $filesOnlyStageDir 'Magicka.GcDiagnostics.dll')
Copy-Item -LiteralPath (Join-PathChecked $repoRoot 'release-package\gc-diagnostics') -Destination (Join-PathChecked $filesOnlyStageDir 'gc-diagnostics') -Recurse -Force
Copy-Item -LiteralPath $packageReadme -Destination (Join-PathChecked $filesOnlyStageDir 'README.txt')
$settingsText = Get-Content -LiteralPath $packageSettingsTemplate -Raw
if ($settingsText -notlike '*{{VERSION}}*') {
    throw "Settings template does not contain {{VERSION}}: $packageSettingsTemplate"
}
$settingsText = $settingsText.Replace('{{VERSION}}', $version)
[System.IO.File]::WriteAllText(
    (Join-PathChecked $filesOnlyStageDir 'patch-settings.ini'),
    $settingsText,
    (New-Object System.Text.UTF8Encoding($false)))

Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
Compress-Archive -Path (Join-Path $filesOnlyStageDir '*') -DestinationPath $filesOnlyZipPath -CompressionLevel Optimal -Force

$requiredEntries = @(
    'MagickaPatchInstaller.exe',
    'Magicka.exe',
    'PolygonHead.dll',
    'Magicka.GcDiagnostics.dll',
    'README.txt',
    'optional-languages/zho/UI.loctable.xml',
    'optional-languages/zho/Font/Maiandra14.xnb',
    'optional-languages/zho/Font/MenuTitle.xnb',
    'flutter_windows.dll',
    'data/flutter_assets/AssetManifest.bin',
    'tools/installer/MagickaPatchInstaller.exe',
    'tools/installer/MagickaPatchTool.exe',
    'tools/installer/MagickaPatchUninstaller.exe',
    'tools/installer/flutter_windows.dll',
    'tools/installer/data/flutter_assets/AssetManifest.bin'
)
$requiredEntries += @($requiredGcDiagnosticsFiles | ForEach-Object {
        'gc-diagnostics/' + $_
    })
if (-not $SkipAutoUpdaterUi) {
    $requiredEntries += @(
        'tools/auto-updater/MagickaPatchAutoUpdater.exe',
        'tools/auto-updater/flutter_windows.dll',
        'tools/auto-updater/data/flutter_assets/AssetManifest.bin'
    )
}
Assert-ZipEntries $zipPath $requiredEntries

$filesOnlyEntries = @(
    'Magicka.exe',
    'PolygonHead.dll',
    'Magicka.GcDiagnostics.dll',
    'patch-settings.ini',
    'README.txt'
)
$filesOnlyEntries += @($requiredGcDiagnosticsFiles | ForEach-Object {
        'gc-diagnostics/' + $_
    })
Assert-ZipContainsOnly $filesOnlyZipPath $filesOnlyEntries

$hash = Get-FileHashWithRetry $zipPath
$zipItem = Get-Item -LiteralPath $zipPath
$filesOnlyHash = Get-FileHashWithRetry $filesOnlyZipPath
$filesOnlyZipItem = Get-Item -LiteralPath $filesOnlyZipPath
$fileCount = (Get-ChildItem -LiteralPath $stageDir -Recurse -File | Measure-Object).Count

Write-Host ""
Write-Host "Created release package:" -ForegroundColor Green
Write-Host "  $zipPath"
Write-Host "  Version: $version"
Write-Host "  Size: $($zipItem.Length) bytes"
Write-Host "  Files staged: $fileCount"
Write-Host "  SHA256: $($hash.Hash)"
Write-Host ""
Write-Host "Created files-only package:" -ForegroundColor Green
Write-Host "  $filesOnlyZipPath"
Write-Host "  Version: $version"
Write-Host "  Size: $($filesOnlyZipItem.Length) bytes"
Write-Host "  Files staged: $($filesOnlyEntries.Count)"
Write-Host "  SHA256: $($filesOnlyHash.Hash)"

if ($KeepStage) {
    Write-Host ""
    Write-Host "Stage directory kept for inspection: $stageDir" -ForegroundColor DarkGray
    Write-Host "Files-only stage directory kept for inspection: $filesOnlyStageDir" -ForegroundColor DarkGray
}
else {
    Remove-PathInside $stageDir $OutputDir
    Remove-PathInside $filesOnlyStageDir $OutputDir
    Write-Host ""
    Write-Host "Stage directory removed. Use -KeepStage to keep it for inspection." -ForegroundColor DarkGray
}
