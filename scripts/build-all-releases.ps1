[CmdletBinding()]
param(
    [string]$MinVersion = "0.0.12",
    [string]$MaxVersion = "",
    [string]$Flutter = "",
    [string]$OutputDir = "",
    [string]$WorktreeRoot = "",
    [string]$MagickaDir = "",
    [switch]$SkipAutoUpdaterUi,
    [switch]$SkipSteamPayloadSync,
    [switch]$KeepStage,
    [switch]$KeepWorktrees
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

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )

    $output = & git -C $WorkingDirectory @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
    return $output
}

function Invoke-GitOptional {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & git -C $WorkingDirectory @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $null
        }
        return $output
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Get-CommitVersion {
    param([Parameter(Mandatory = $true)][string]$Commit)

    $pubspec = Invoke-GitOptional @("show", "${Commit}:magicka-patch-installer-ui/pubspec.yaml")
    if ($null -eq $pubspec) {
        return ""
    }

    foreach ($line in $pubspec) {
        if ($line -match '^\s*version:\s*(?<version>[^\s#]+)') {
            return ($Matches["version"] -split '\+')[0]
        }
    }
    return ""
}

function Convert-Version {
    param([Parameter(Mandatory = $true)][string]$Version)

    try {
        return [version]$Version
    }
    catch {
        throw "Unsupported version '$Version'. Use a numeric version like 0.0.14."
    }
}

function Get-ReleaseCommits {
    $min = Convert-Version $MinVersion
    $max = if ([string]::IsNullOrWhiteSpace($MaxVersion)) { $null } else { Convert-Version $MaxVersion }
    $commits = Invoke-Git @("rev-list", "--first-parent", "--reverse", "HEAD")
    $selected = New-Object System.Collections.Generic.List[object]
    $seen = @{}

    foreach ($commit in $commits) {
        $version = Get-CommitVersion $commit
        if ([string]::IsNullOrWhiteSpace($version)) {
            continue
        }

        $parsed = Convert-Version $version
        if ($parsed -lt $min) {
            continue
        }
        if ($null -ne $max -and $parsed -gt $max) {
            continue
        }

        if ($seen.ContainsKey($version)) {
            $selected[$seen[$version]] = [pscustomobject]@{
                Version = $version
                Parsed = $parsed
                Commit = $commit
            }
        }
        else {
            $seen[$version] = $selected.Count
            $selected.Add([pscustomobject]@{
                Version = $version
                Parsed = $parsed
                Commit = $commit
            })
        }
    }

    return @($selected | Sort-Object Parsed)
}

function Remove-PathInside {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $AllowedRoot).Path)
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside worktree root: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

function New-ReleaseWorktree {
    param(
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $suffix = ($Version -replace '^0\.0\.', '')
    $path = Join-PathChecked $WorktreeRoot "v$suffix"
    Invoke-GitOptional @("worktree", "remove", "--force", $path) | Out-Null
    Invoke-GitOptional @("worktree", "prune") | Out-Null
    Remove-PathInside $path $WorktreeRoot
    Invoke-Git @("worktree", "add", "--detach", $path, $Commit) | Out-Host

    $scriptsDir = Join-PathChecked $path "scripts"
    New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null
    Copy-Item -LiteralPath (Join-PathChecked $scriptDir "build-release.ps1") -Destination (Join-PathChecked $scriptsDir "build-release.ps1") -Force

    $releasePackageDir = Join-PathChecked $path "release-package"
    New-Item -ItemType Directory -Force -Path $releasePackageDir | Out-Null
    Copy-Item -LiteralPath (Join-PathChecked $repoRoot "release-package\README.txt") -Destination (Join-PathChecked $releasePackageDir "README.txt") -Force
    Copy-Item -LiteralPath (Join-PathChecked $repoRoot "release-package\patch-settings.ini") -Destination (Join-PathChecked $releasePackageDir "patch-settings.ini") -Force

    return $path
}

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir ".."))

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-PathChecked $repoRoot "release"
}
else {
    $OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}

if ([string]::IsNullOrWhiteSpace($WorktreeRoot)) {
    $WorktreeRoot = Join-PathChecked $repoRoot "wr"
}
else {
    $WorktreeRoot = [System.IO.Path]::GetFullPath($WorktreeRoot)
}

New-Item -ItemType Directory -Force -Path $WorktreeRoot | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$releases = Get-ReleaseCommits
if ($releases.Count -eq 0) {
    throw "No release commits found from $MinVersion to $(if ([string]::IsNullOrWhiteSpace($MaxVersion)) { 'HEAD' } else { $MaxVersion })."
}

Write-Host "Release builds:" -ForegroundColor Green
foreach ($release in $releases) {
    Write-Host "  $($release.Version) $($release.Commit.Substring(0, 7))"
}

foreach ($release in $releases) {
    Write-Host ""
    Write-Host "== Building $($release.Version) ==" -ForegroundColor Green
    $worktree = New-ReleaseWorktree $release.Commit $release.Version
    $buildScript = Join-PathChecked $worktree "scripts\build-release.ps1"
    $arguments = @{
        OutputDir = $OutputDir
    }
    if (-not [string]::IsNullOrWhiteSpace($Flutter)) {
        $arguments["Flutter"] = $Flutter
    }
    if (-not [string]::IsNullOrWhiteSpace($MagickaDir)) {
        $arguments["MagickaDir"] = $MagickaDir
    }
    if ($SkipAutoUpdaterUi) {
        $arguments["SkipAutoUpdaterUi"] = $true
    }
    if ($SkipSteamPayloadSync) {
        $arguments["SkipSteamPayloadSync"] = $true
    }
    if ($KeepStage) {
        $arguments["KeepStage"] = $true
    }

    & $buildScript @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $($release.Version)"
    }

    if (-not $KeepWorktrees) {
        Invoke-Git @("worktree", "remove", "--force", $worktree) | Out-Null
    }
}

Write-Host ""
Write-Host "All release ZIPs are in $OutputDir" -ForegroundColor Green
