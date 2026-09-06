param(
    [string]$OriginalExe = "..\..\Magicka_orig.exe",
    [string]$CurrentPatchExe = "..\..\Magicka.exe",
    [string]$OldVersionsDirectory = "..\..\tmp\old_versions",
    [string]$OutputDirectory = "..\..\tmp\inventory-box-patcher-run",
    [string]$GameDirectory = "",
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
$oldVersionsPath = Resolve-ArgumentPath $OldVersionsDirectory
$version14Path = Join-Path $oldVersionsPath "Magicka 1.4.16.0\Magicka.exe"
$version15Path = Join-Path $oldVersionsPath "Magicka 1.5.1.0\Magicka.exe"
$outputRoot = Resolve-ArgumentPath $OutputDirectory
$gameDirectoryPath = if ([string]::IsNullOrWhiteSpace($GameDirectory)) {
    $null
}
else {
    Resolve-ArgumentPath $GameDirectory
}
$backupDirectory = Join-Path $outputRoot "backup"
$runtimeDirectory = Join-Path $outputRoot "runtime"
$auditDirectory = Join-Path $outputRoot "audit"
$toolBuildDirectory = Join-Path $outputRoot "tool-build"
$sourceAnalysisDirectory = Join-Path $outputRoot "source-analysis"
$verifiedAssembliesPath = Join-Path $experimentRoot "reference\verified-assemblies.txt"

function Invoke-Experiment {
    Assert-ExperimentInputs
    Prepare-FreshExperimentDirectory
    Backup-OriginalExecutable
    Restore-ExperimentTools
    Build-ExperimentTools
    if (-not $SkipSourceAnalysis) {
        Analyze-SourceDifference
    }
    Create-RuntimePatchVariant
    Test-RuntimePatchRegistrationAgainstOriginal
    Test-BehaviorMatrix
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
    if (-not (Test-Path -LiteralPath $version14Path -PathType Leaf)) {
        throw "Magicka 1.4.16.0 does not exist: $version14Path"
    }
    if (-not (Test-Path -LiteralPath $version15Path -PathType Leaf)) {
        throw "Magicka 1.5.1.0 does not exist: $version15Path"
    }
    if ($gameDirectoryPath -ne $null -and
        -not (Test-Path -LiteralPath $gameDirectoryPath -PathType Container)) {
        throw "Game directory does not exist: $gameDirectoryPath"
    }
    Assert-FileHash $originalPath (Read-ReferenceValue "original_sha256") "original Magicka"
    Assert-FileHash $currentPatchPath (Read-ReferenceValue "manual_patch_sha256") "manual patch"
    Assert-FileHash $version14Path (Read-ReferenceValue "magicka_1.4.16.0_sha256") "Magicka 1.4.16.0"
    Assert-FileHash $version15Path (Read-ReferenceValue "magicka_1.5.1.0_sha256") "Magicka 1.5.1.0"
}

function Read-ReferenceValue([string]$name) {
    $prefix = $name + "="
    $matches = @(Get-Content -LiteralPath $verifiedAssembliesPath |
        Where-Object { $_.StartsWith($prefix) })
    if ($matches.Count -ne 1) {
        throw "Expected one $name entry in $verifiedAssembliesPath."
    }
    return $matches[0].Substring($prefix.Length)
}

function Assert-FileHash([string]$path, [string]$expected, [string]$label) {
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -cne $expected) {
        throw "$label hash changed. Expected $expected, found $actual. Update the reference and coverage report intentionally."
    }
}

function Prepare-FreshExperimentDirectory {
    if (Test-Path -LiteralPath $outputRoot) {
        throw "Refusing to overwrite existing experiment directory: $outputRoot"
    }

    New-Item -ItemType Directory -Path `
        $backupDirectory, `
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
    Build-Project "src\RuntimeLoaderInjector\RuntimeLoaderInjector.csproj" "runtime-loader-injector"
    Build-Project "src\BehaviorProbe\BehaviorProbe.csproj" "behavior-probe"
    Build-Project "src\RuntimeRegistrationProbe\RuntimeRegistrationProbe.csproj" "runtime-registration-probe"
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
    $rankingPath = Join-Path $sourceAnalysisDirectory "file-diff-ranking.csv"
    $actualCount = @(Import-Csv -LiteralPath $rankingPath).Count
    $expectedCount = [int](Read-ReferenceValue "source_diff_files")
    if ($actualCount -ne $expectedCount) {
        throw "Manual patch source inventory changed from $expectedCount to $actualCount files. Update the coverage report intentionally."
    }
}

function Create-RuntimePatchVariant {
    $injector = Join-Path $toolBuildDirectory "runtime-loader-injector\RuntimeLoaderInjector.dll"
    $hostOutput = Join-Path $runtimeDirectory "Magicka.exe"
    Create-RuntimeHost $injector $originalPath $hostOutput
    Create-RuntimeHost $injector $version14Path `
        (Join-Path $runtimeDirectory "compatibility\1.4.16.0\Magicka.exe")
    Create-RuntimeHost $injector $version15Path `
        (Join-Path $runtimeDirectory "compatibility\1.5.1.0\Magicka.exe")

    $runtimeBuild = Join-Path $toolBuildDirectory "behavior-probe"
    Copy-Item -LiteralPath `
        (Join-Path $runtimeBuild "Magicka.CommunityPatch.Runtime.dll"), `
        (Join-Path $runtimeBuild "0Harmony.dll") `
        -Destination $runtimeDirectory
}

function Create-RuntimeHost(
    [string]$injector,
    [string]$inputPath,
    [string]$outputPath) {
    & dotnet $injector $inputPath $outputPath
    Assert-LastExitCode "runtime loader injection for $inputPath"
}

function Test-BehaviorMatrix {
    $patchFailures = @(
        "avatar_interactable.missing_play_state",
        "avatar_interactable.missing_level",
        "avatar_interactable.missing_scene",
        "avatar_interactable.missing_triggers",
        "ai_attack.bodyless_target",
        "ai_move.enter_bodyless_target",
        "ai_move.execute_bodyless_target",
        "agent_target.bodyless_player",
        "closest_damageable.bodyless_candidate",
        "entity_query.bodyless_entry",
        "entity_query.null_entry",
        "entity_clear.stale_grid",
        "entity_state_storage.constructor_release",
        "entity_state_storage.current_restore",
        "helper_array_equals.left_null",
        "helper_array_equals.right_null",
        "helper_array_equals.both_null",
        "inventory.initial_screen_size",
        "inventory.changed_screen_size",
        "camera_follow.bodyless_target",
        "boss_health_bar.constructor_release",
        "boss_health_bar.current_scene",
        "boss_health_bar.setter_release",
        "hud_manager.disabled_original_hud",
        "machine.missing_warlock",
        "jormungandr.missing_target",
        "play_state.missing_spawn",
        "play_state.non_npc_spawn",
        "play_state.foreign_state_spawn",
        "portal_queue.null_then_bodyless",
        "portal_queue.bodyless_then_null",
        "versus_revive.missing_avatar",
        "versus_revive.missing_requested_avatar",
        "pack_license.custom_offline_license",
        "pack_license.custom_offline_enabled",
        "pack_license.custom_insecure_license",
        "pack_license.custom_insecure_enabled",
        "drink_blood.play_state_release",
        "random_mine.play_state_release",
        "starfall.play_state_release",
        "starfall.current_play_state",
        "drain_life.play_state_release",
        "sub_menu_main.gamepad_back",
        "company_state.exit_cleanup_order",
        "control_manager.null_controller",
        "control_manager.playerless_controller",
        "interactable_highlight.missing_scene",
        "interactable_highlight.missing_level_model",
        "audio_stop_all.disposed_cue",
        "deflection_aura.play_state_release",
        "flash.scene_release",
        "flash.current_scene"
    )
    $playStateNotAvailable = @(
        "play_state.ordinary_message",
        "play_state.other_action",
        "play_state.missing_spawn",
        "play_state.non_npc_spawn",
        "play_state.same_state_spawn",
        "play_state.foreign_state_spawn"
    )
    $legacyNotAvailable = @($playStateNotAvailable) + @(
        "boss_health_bar.constructor_release",
        "hud_manager.disabled_original_hud",
        "hud_manager.enabled_original_hud",
        "versus_revive.missing_avatar",
        "versus_revive.missing_requested_avatar",
        "versus_revive.available_avatar",
        "sub_menu_main.gamepad_back",
        "sub_menu_main.keyboard_back"
    )
    $matrix = New-Object System.Collections.Generic.List[string]

    Test-BehaviorProfile "current-original" $originalPath "unpatched" $patchFailures @() $matrix
    Test-BehaviorProfile "current-manual-patch" $currentPatchPath "unpatched" @() @() $matrix
    Test-BehaviorProfile "current-runtime-patch" $originalPath "runtime" @() @() $matrix
    Test-BehaviorProfile "1.4.16.0-original" $version14Path "unpatched" `
        @("avatar_interactable.missing_play_state", "avatar_interactable.missing_level", "avatar_interactable.missing_scene", "avatar_interactable.missing_triggers", "ai_attack.bodyless_target", "ai_move.enter_bodyless_target", "ai_move.execute_bodyless_target", "agent_target.bodyless_player", "closest_damageable.bodyless_candidate", "entity_query.bodyless_entry", "entity_query.null_entry", "entity_clear.stale_grid", "entity_state_storage.constructor_release", "entity_state_storage.current_restore", "helper_array_equals.left_null", "helper_array_equals.right_null", "helper_array_equals.both_null", "inventory.initial_screen_size", "inventory.changed_screen_size", "camera_follow.bodyless_target", "boss_health_bar.current_scene", "boss_health_bar.setter_release", "machine.missing_warlock", "jormungandr.missing_target", "portal_queue.null_then_bodyless", "portal_queue.bodyless_then_null", "pack_license.custom_offline_license", "pack_license.custom_offline_enabled", "pack_license.custom_insecure_license", "pack_license.custom_insecure_enabled", "drink_blood.play_state_release", "random_mine.play_state_release", "starfall.play_state_release", "starfall.current_play_state", "drain_life.play_state_release", "sub_menu_main.gamepad_back", "company_state.exit_cleanup_order", "control_manager.null_controller", "control_manager.playerless_controller", "interactable_highlight.missing_scene", "interactable_highlight.missing_level_model", "audio_stop_all.disposed_cue", "deflection_aura.play_state_release", "flash.scene_release", "flash.current_scene") `
        $legacyNotAvailable $matrix
    Test-BehaviorProfile "1.4.16.0-runtime-patch" $version14Path "runtime" `
        @() $legacyNotAvailable $matrix
    Test-BehaviorProfile "1.5.1.0-original" $version15Path "unpatched" `
        @("avatar_interactable.missing_play_state", "avatar_interactable.missing_level", "avatar_interactable.missing_scene", "avatar_interactable.missing_triggers", "ai_attack.bodyless_target", "ai_move.enter_bodyless_target", "ai_move.execute_bodyless_target", "agent_target.bodyless_player", "closest_damageable.bodyless_candidate", "entity_query.bodyless_entry", "entity_query.null_entry", "entity_clear.stale_grid", "entity_state_storage.constructor_release", "entity_state_storage.current_restore", "helper_array_equals.left_null", "helper_array_equals.right_null", "helper_array_equals.both_null", "inventory.initial_screen_size", "inventory.changed_screen_size", "camera_follow.bodyless_target", "boss_health_bar.current_scene", "boss_health_bar.setter_release", "machine.missing_warlock", "jormungandr.missing_target", "portal_queue.null_then_bodyless", "portal_queue.bodyless_then_null", "pack_license.custom_offline_license", "pack_license.custom_offline_enabled", "pack_license.custom_insecure_license", "pack_license.custom_insecure_enabled", "drink_blood.play_state_release", "random_mine.play_state_release", "starfall.play_state_release", "starfall.current_play_state", "drain_life.play_state_release", "sub_menu_main.gamepad_back", "company_state.exit_cleanup_order", "control_manager.null_controller", "control_manager.playerless_controller", "interactable_highlight.missing_scene", "interactable_highlight.missing_level_model", "audio_stop_all.disposed_cue", "deflection_aura.play_state_release", "flash.scene_release", "flash.current_scene") `
        $legacyNotAvailable $matrix
    Test-BehaviorProfile "1.5.1.0-runtime-patch" $version15Path "runtime" `
        @() $legacyNotAvailable $matrix

    $matrix.Insert(0, "result=PASS")
    [System.IO.File]::WriteAllLines(
        (Join-Path $auditDirectory "behavior-matrix.txt"),
        $matrix.ToArray())
}

function Test-BehaviorProfile(
    [string]$profile,
    [string]$targetPath,
    [string]$mode,
    [string[]]$expectedFailures,
    [string[]]$notApplicable,
    [System.Collections.Generic.List[string]]$matrix) {
    $probeDirectory = Join-Path $toolBuildDirectory "behavior-probe"
    $probe = Join-Path $probeDirectory "BehaviorProbe.exe"
    $runtimeAudit = Join-Path $probeDirectory "magicka-runtime-patch-audit.txt"
    if (Test-Path -LiteralPath $runtimeAudit) {
        Remove-Item -LiteralPath $runtimeAudit
    }

    $standardOutput = Join-Path $auditDirectory ($profile + ".stdout.txt")
    $standardError = Join-Path $auditDirectory ($profile + ".stderr.txt")
    $targetDirectory = Split-Path -Parent $targetPath
    $workingDirectory = if (Test-Path -LiteralPath `
        (Join-Path $targetDirectory "content") -PathType Container) {
        $targetDirectory
    }
    elseif ($gameDirectoryPath -ne $null) {
        $gameDirectoryPath
    }
    else {
        $targetDirectory
    }
    $process = Start-Process `
        -FilePath $probe `
        -ArgumentList @('"' + $targetPath + '"', $mode) `
        -WorkingDirectory $workingDirectory `
        -NoNewWindow `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $standardOutput `
        -RedirectStandardError $standardError
    $output = @(Get-Content -LiteralPath $standardOutput)
    $errorOutput = @(Get-Content -LiteralPath $standardError)
    if ($process.ExitCode -ne 0) {
        throw "behavior profile $profile failed with exit code $($process.ExitCode): $($errorOutput -join [Environment]::NewLine)"
    }
    if ($errorOutput.Count -gt 0) {
        $output += $errorOutput | ForEach-Object { "stderr=$_" }
    }
    [System.IO.File]::WriteAllLines(
        (Join-Path $auditDirectory ($profile + ".txt")),
        [string[]]$output)

    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($targetPath).FullName
    $sha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
    $matrix.Add("profile=$profile|assembly=$assemblyName|sha256=$sha256|mode=$mode")

    $scenarioNames = @(
        "avatar_interactable.missing_play_state",
        "avatar_interactable.missing_level",
        "avatar_interactable.missing_scene",
        "avatar_interactable.missing_triggers",
        "avatar_interactable.empty_scene",
        "ai_attack.bodyless_target",
        "ai_attack.missing_target",
        "ai_attack.invalid_owner",
        "ai_move.enter_bodyless_target",
        "ai_move.enter_missing_target",
        "ai_move.execute_bodyless_target",
        "ai_move.execute_missing_target",
        "agent_target.bodyless_player",
        "agent_target.no_player",
        "closest_damageable.bodyless_candidate",
        "closest_damageable.null_candidate",
        "closest_damageable.empty_grid",
        "entity_query.bodyless_entry",
        "entity_query.null_entry",
        "entity_query.empty_grid",
        "entity_clear.stale_grid",
        "entity_clear.empty_grid",
        "entity_state_storage.constructor_release",
        "entity_state_storage.current_restore",
        "entity_state_storage.empty_restore",
        "helper_array_equals.equal",
        "helper_array_equals.different",
        "helper_array_equals.left_null",
        "helper_array_equals.right_null",
        "helper_array_equals.both_null",
        "inventory.initial_screen_size",
        "inventory.changed_screen_size",
        "camera_follow.bodyless_target",
        "camera_follow.missing_target",
        "camera_follow.other_behavior",
        "boss_health_bar.constructor_release",
        "boss_health_bar.current_scene",
        "boss_health_bar.setter_release",
        "hud_manager.disabled_original_hud",
        "hud_manager.enabled_original_hud",
        "machine.missing_warlock",
        "machine.valid_warlock",
        "machine.other_message",
        "jormungandr.missing_target",
        "jormungandr.before_warning",
        "play_state.ordinary_message",
        "play_state.other_action",
        "play_state.missing_spawn",
        "play_state.non_npc_spawn",
        "play_state.same_state_spawn",
        "play_state.foreign_state_spawn",
        "portal_queue.null_then_bodyless",
        "portal_queue.bodyless_then_null",
        "portal_queue.empty",
        "versus_revive.missing_avatar",
        "versus_revive.missing_requested_avatar",
        "versus_revive.available_avatar",
        "pack_license.custom_offline_license",
        "pack_license.custom_offline_enabled",
        "pack_license.custom_insecure_license",
        "pack_license.custom_insecure_enabled",
        "pack_license.custom_secure_license",
        "pack_license.custom_secure_enabled",
        "pack_license.yes_license",
        "pack_license.no_license",
        "drink_blood.play_state_release",
        "drink_blood.execute_behavior",
        "random_mine.play_state_release",
        "random_mine.offline_damage",
        "random_mine.client_no_damage",
        "starfall.play_state_release",
        "starfall.current_play_state",
        "starfall.no_damage_queue",
        "drain_life.play_state_release",
        "drain_life.execute_behavior",
        "sub_menu_main.gamepad_back",
        "sub_menu_main.keyboard_back",
        "company_state.exit_cleanup_order",
        "control_manager.null_controller",
        "control_manager.playerless_controller",
        "control_manager.valid_controller",
        "interactable_highlight.missing_scene",
        "interactable_highlight.missing_level_model",
        "interactable_highlight.empty",
        "audio_stop_all.disposed_cue",
        "audio_stop_all.empty",
        "deflection_aura.play_state_release",
        "deflection_aura.execute_behavior",
        "flash.scene_release",
        "flash.current_scene"
    )
    foreach ($scenarioName in $scenarioNames) {
        $prefix = "scenario.$scenarioName="
        $matches = @($output | Where-Object { $_.StartsWith($prefix) })
        if ($matches.Count -ne 1) {
            throw "Behavior profile $profile produced $($matches.Count) results for $scenarioName."
        }

        $expectedStatus = if ($notApplicable -contains $scenarioName) {
            "NOT_APPLICABLE"
        }
        elseif ($expectedFailures -contains $scenarioName) {
            "FAIL"
        }
        else {
            "PASS"
        }
        $actualStatus = $matches[0].Substring($prefix.Length)
        if ($actualStatus -cne $expectedStatus) {
            throw "Behavior profile $profile expected $scenarioName=$expectedStatus, found $actualStatus."
        }
        $matrix.Add("scenario=$profile|$scenarioName|$actualStatus")
    }

    if ($mode -eq "runtime") {
        if (-not (Test-Path -LiteralPath $runtimeAudit -PathType Leaf)) {
            throw "Behavior profile $profile did not create a runtime patch audit."
        }
        Copy-Item -LiteralPath $runtimeAudit `
            -Destination (Join-Path $auditDirectory ($profile + "-runtime-audit.txt"))
    }
}

function Test-RuntimePatchRegistrationAgainstOriginal {
    $probeDirectory = Join-Path $toolBuildDirectory "runtime-registration-probe"
    Push-Location $probeDirectory
    try {
        & ".\RuntimeRegistrationProbe.exe" $originalPath 2>&1 |
            Tee-Object -FilePath (Join-Path $auditDirectory "runtime-original-registration.txt")
        Assert-LastExitCode "runtime registration against the original Magicka assembly"
        Copy-Item -LiteralPath ".\magicka-runtime-patch-audit.txt" `
            -Destination (Join-Path $auditDirectory "runtime-original-registration-audit.txt")
    }
    finally {
        Pop-Location
    }
}

function Verify-RuntimeEffectiveDiff {
    $runtimeAuditPath = Join-Path $auditDirectory "runtime-original-registration-audit.txt"
    $auditLines = @(Get-Content -LiteralPath $runtimeAuditPath)
    if ($auditLines -notcontains "result=PASS" -or
        $auditLines -notcontains "patch_end=AI attack detached target guard" -or
        $auditLines -notcontains "patch_end=AI move detached target entry guard" -or
        $auditLines -notcontains "patch_end=AI move detached target execution guard" -or
        $auditLines -notcontains "patch_end=Agent detached target candidate guard" -or
        $auditLines -notcontains "patch_end=Avatar detached interaction guard" -or
        $auditLines -notcontains "patch_end=EntityManager detached damageable guard" -or
        $auditLines -notcontains "patch_end=EntityManager detached spatial entry guard" -or
        $auditLines -notcontains "patch_end=EntityManager scene-transition grid cleanup" -or
        $auditLines -notcontains "patch_end=EntityStateStorage constructor play-state release" -or
        $auditLines -notcontains "patch_end=EntityStateStorage current play-state restore" -or
        $auditLines -notcontains "patch_end=Helper null-safe array equality" -or
        $auditLines -notcontains "patch_end=InventoryBox screen size" -or
        $auditLines -notcontains "patch_end=MagickCamera detached follow target guard" -or
        $auditLines -notcontains "patch_end=BossHealthBar constructor scene release" -or
        $auditLines -notcontains "patch_end=BossHealthBar current scene getter" -or
        $auditLines -notcontains "patch_end=BossHealthBar legacy scene setter release" -or
        $auditLines -notcontains "patch_end=HUDManager original HUD enable" -or
        $auditLines -notcontains "patch_end=Machine network initialization" -or
        $auditLines -notcontains "patch_end=Jormungandr missing underground target guard" -or
        $auditLines -notcontains "patch_end=PlayState SpawnNPC WorldSync guard" -or
        $auditLines -notcontains "patch_end=Portal detached teleport entry guard" -or
        $auditLines -notcontains "patch_end=VersusRuleset missing revive avatar guard" -or
        $auditLines -notcontains "patch_end=ItemPack custom license assignment" -or
        $auditLines -notcontains "patch_end=ItemPack custom license enable" -or
        $auditLines -notcontains "patch_end=MagickPack custom license assignment" -or
        $auditLines -notcontains "patch_end=MagickPack custom license enable" -or
        $auditLines -notcontains "patch_end=DrinkBlood unused play-state release" -or
        $auditLines -notcontains "patch_end=RandomMine unused play-state release" -or
        $auditLines -notcontains "patch_end=Starfall unused play-state release" -or
        $auditLines -notcontains "patch_end=Starfall current play-state update" -or
        $auditLines -notcontains "patch_end=DrainLife unused play-state release" -or
        $auditLines -notcontains "patch_end=SubMenuMain controller exit confirmation" -or
        $auditLines -notcontains "patch_end=CompanyState deferred content disposal" -or
        $auditLines -notcontains "patch_end=ControlManager lock detached controller guard" -or
        $auditLines -notcontains "patch_end=ControlManager query detached controller guard" -or
        $auditLines -notcontains "patch_end=ControlManager unlock detached controller guard" -or
        $auditLines -notcontains "patch_end=Interactable detached scene highlight guard" -or
        $auditLines -notcontains "patch_end=AudioManager disposed cue guard" -or
        $auditLines -notcontains "patch_end=DeflectionAura unused play-state release" -or
        $auditLines -notcontains "patch_end=Flash scene reference release" -or
        $auditLines -notcontains "patch_end=Flash current scene update" -or
        @($auditLines | Where-Object { $_ -eq "patch_kind=prefix" }).Count -ne 13 -or
        @($auditLines | Where-Object { $_ -eq "patch_kind=postfix" }).Count -ne 4 -or
        @($auditLines | Where-Object { $_ -eq "patch_kind=transpiler" }).Count -ne 24) {
        throw "The runtime audit does not contain all registered Harmony patches."
    }
}

function Write-ExperimentSummary {
    $artifactPaths = @(
        (Join-Path $runtimeDirectory "Magicka.exe"),
        (Join-Path $runtimeDirectory "compatibility\1.4.16.0\Magicka.exe"),
        (Join-Path $runtimeDirectory "compatibility\1.5.1.0\Magicka.exe"),
        (Join-Path $runtimeDirectory "Magicka.CommunityPatch.Runtime.dll"),
        (Join-Path $runtimeDirectory "0Harmony.dll")
    )
    $summary = New-Object System.Collections.Generic.List[string]
    $summary.Add("result=PASS")
    $summary.Add("implemented_patches=41")
    $summary.Add("runtime_registration=PASS")
    $summary.Add("runtime_original_assembly_probe=PASS")
    $summary.Add("runtime_behavior=PASS")
    $summary.Add("three_way_behavior=PASS")
    $summary.Add("compatibility_1.4.16.0=PASS")
    $summary.Add("compatibility_1.5.1.0=PASS")

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
