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
        "payload_contract.compatible_pair",
        "payload_contract.rejects_missing_pair",
        "ui_render.activation",
        "ui_render.projected_positions",
        "ui_render.notifier_restoration",
        "ui_render.screen_size",
        "level_current_state.read_sites",
        "avatar_interactable.missing_play_state",
        "avatar_interactable.missing_level",
        "avatar_interactable.missing_scene",
        "avatar_interactable.missing_triggers",
        "network_pickup.bodyless_pickup",
        "network_pickup.bodyless_pickup_request",
        "ai_attack.bodyless_target",
        "ai_move.enter_bodyless_target",
        "ai_move.execute_bodyless_target",
        "agent_target.bodyless_player",
        "closest_damageable.bodyless_candidate",
        "entity_query.bodyless_entry",
        "entity_query.null_entry",
        "entity_clear.stale_grid",
        "entity_manager_state.retention",
        "entity_manager_state.cache_state",
        "entity_physics_cleanup.deinitialize",
        "entity_physics_cleanup.reuse_fallback",
        "entity_physics_cleanup.final_teardown",
        "damageable_teardown.final_references",
        "animated_physics_lifecycle.deinitialize",
        "animated_physics_lifecycle.final_teardown",
        "npc_teardown.derived_state",
        "npc_lifecycle.deinitialize",
        "character_teardown.final_references",
        "character_grip.missing_target",
        "character_grip.actor_body_missing",
        "character_grip.target_body_missing",
        "character_grip.controller_missing",
        "typing_text.truncated_plain",
        "typing_text.truncated_markup",
        "typing_text.empty",
        "late_udp.empty_client_list",
        "late_udp.negative_client_index",
        "enter_sync.unknown_sender",
        "ruleset_update.detached_play_state",
        "missile_event.uninitialized_state",
        "missile_event.missing_collision_target",
        "missile_event.missing_collision_target_cleanup",
        "hotjoin_broadcast.two_syncing_players",
        "forced_sync.invalid_sender",
        "forced_sync.sparse_players",
        "forced_sync.missing_avatar",
        "trigger_authority.peer_spawn",
        "trigger_lifecycle.missing_item_slot",
        "trigger_lifecycle.wrong_item_slot_type",
        "trigger_lifecycle.active_living_npc",
        "trigger_lifecycle.missing_active_target",
        "entity_state_storage.constructor_release",
        "entity_state_storage.current_restore",
        "icon_renderer.constructor_state_release",
        "icon_renderer.initialize_state_release",
        "icon_renderer.current_game_type",
        "damageable_deinitialize.gib_release",
        "damageable_deinitialize.resistance_release",
        "damageable_deinitialize.cache_order",
        "summon_death.owner_state",
        "summon_death.vector_state",
        "summon_death.spawn_state",
        "death_entity.constructor_state",
        "death_entity.initialize_state",
        "death_entity.update_state",
        "death_entity.deinitialize_state",
        "summon_death.level_cleanup",
        "helper_array_equals.left_null",
        "helper_array_equals.right_null",
        "helper_array_equals.both_null",
        "inventory.initial_screen_size",
        "inventory.changed_screen_size",
        "widescreen.horizontal_ultrawide",
        "widescreen.horizontal_16_9",
        "widescreen.right_ultrawide",
        "widescreen.right_16_9",
        "tutorial_play_state.initialize_release",
        "tutorial_play_state.current_update",
        "ethereal_clone.play_state_release",
        "ethereal_clone.current_nav_mesh",
        "break_barriers.play_state_release",
        "break_barriers.current_entity_manager",
        "tesla_field.initialized_pool_release",
        "tesla_field.empty_pool_allocation_release",
        "generic_health_bar.current_scene",
        "grease_trail.play_state_release",
        "grease_trail.current_play_state",
        "grease.play_state_release",
        "grease.current_play_state",
        "grease.cache_cleanup",
        "grease.cache_cleanup_idempotent",
        "grease_lump.current_play_state",
        "underground_attack.play_state_release",
        "underground_attack.initialize_current_play_state",
        "underground_attack.update_current_play_state",
        "arcane_blade.initialize_current_scene",
        "arcane_blade.update_current_scene",
        "conflagration.vector_state",
        "conflagration.direction_state",
        "conflagration.owner_state",
        "conflagration.update_state",
        "wave.vector_state",
        "wave.direction_state",
        "wave.owner_state",
        "wave.update_state",
        "spell_mine.animated_part_release",
        "polymorph.owner_state",
        "polymorph.vector_state",
        "polymorph.target_state",
        "polymorph.remove_state",
        "floor_stomp.execute_state",
        "floor_stomp.update_state",
        "revive.execute_state",
        "revive.update_state",
        "portal.execute_state_release",
        "portal.vector_initialize_current_state",
        "portal.message_initialize_current_state",
        "portal.update_current_state",
        "portal.level_cleanup",
        "grow.orphaned_owner",
        "spell_effect.initialize_release",
        "lightning_spell.cached_current_play_state",
        "lightning_spell.empty_cache_current_play_state",
        "lightning_spell.cast_current_play_state",
        "push_spell.get_from_cache_current_play_state",
        "push_spell.return_to_cache_current_play_state",
        "spray_spell.get_from_cache_current_play_state",
        "spray_spell.return_to_cache_current_play_state",
        "spray_spell.cast_update_current_play_state",
        "projectile_spell.get_from_cache_current_play_state",
        "projectile_spell.return_to_cache_current_play_state",
        "railgun_spell.get_from_cache_current_play_state",
        "railgun_spell.return_to_cache_current_play_state",
        "railgun_spell.cast_self_current_play_state",
        "railgun_spell.cast_weapon_current_play_state",
        "railgun_spell.deinitialize_current_play_state",
        "pool_expansion.avatar",
        "pool_expansion.generic_boss",
        "pool_expansion.damageable_physics_entity",
        "pool_expansion.gib",
        "pool_expansion.projectile_spell",
        "pool_expansion.spray_spell",
        "pool_expansion.railgun_spell",
        "pool_expansion.shield_spell",
        "projectile_spawn.null_owner",
        "projectile_spell.empty_condition_cache",
        "projectile_spell.null_missile_result",
        "projectile_spell.detached_missile_state",
        "game_scene.current_play_state",
        "game_scene.menu_controller_reset",
        "game_scene.light_update_current_state",
        "game_scene.dispose_complete",
        "game_scene.dispose_idempotent",
        "game_scene.dispose_releases_kill_plane_tag",
        "telemetry_backoff.repeat",
        "telemetry_backoff.independent_key",
        "telemetry_backoff.category_cap",
        "telemetry_context.navigation",
        "telemetry_context.display",
        "telemetry_context.language",
        "telemetry_context.navigation_bound",
        "telemetry_context.payload",
        "warlord_ability.diagnostic_before_cast",
        "prop_boss.level_teardown",
        "fairy_teardown.listed",
        "fairy_teardown.avatar_owned",
        "fairy_teardown.npc_owned",
        "barrier_teardown.active_instance",
        "barrier_teardown.cached_instance",
        "barrier_teardown.hit_list_cache",
        "shield.global_content_owner",
        "gib_teardown.active_instance",
        "gib_teardown.cached_instance",
        "effect_manager.duplicate_name",
        "time_warp.start_current_state",
        "time_warp.update_current_state",
        "time_warp.remove_current_state",
        "time_warp_staff.start_current_state",
        "time_warp_staff.update_current_state",
        "time_warp_staff.remove_current_state",
        "spell_wheel.initialize_release",
        "spell_wheel.current_scene",
        "earthquake.execute_current_scene",
        "earthquake.current_camera",
        "earthquake.current_entity_manager",
        "arrow_rain.vector_release",
        "arrow_rain.owner_release",
        "arrow_rain.update_current_play_state",
        "arrow_rain.remove_current_scene",
        "healing_rain.vector_current_play_state",
        "healing_rain.owner_current_play_state",
        "healing_rain.update_current_play_state",
        "healing_rain.remove_releases_references",
        "healing_rain.remove_without_scene",
        "in_game_menu.initialize_release",
        "in_game_menu.current_scene",
        "in_game_menu_stack.initialized_dispose",
        "magicks_language.negative_selection",
        "magicks_language.past_end_selection",
        "camera_follow.bodyless_target",
        "boss_health_bar.constructor_release",
        "boss_health_bar.current_scene",
        "boss_health_bar.setter_release",
        "loading_screen.managed_restore_order",
        "menu_image_text.literal_font_change",
        "menu_image_text.localized_font_change",
        "paradox_popup.plain_clears_extra",
        "language_manager.simplified_chinese_name",
        "language_manager.simplified_chinese_aliases",
        "dialog_layout.list_breaks",
        "dialog_layout.element_sections",
        "shadow_blobs.matching_scene",
        "player_controller_avatar.matching_release",
        "player_text_box.active_release",
        "player_notifier.active_release",
        "chant_spell_cleanup.initialized_dispose",
        "hud_manager.disabled_original_hud",
        "machine.missing_warlock",
        "boss_fight.setup_state_release",
        "boss_fight.initialize_current_state",
        "boss_fight.reset_current_state",
        "boss_fight.update_current_state",
        "boss_fight.pending_initialize",
        "boss_fight.pending_start",
        "boss_fight.pending_clear",
        "jormungandr.missing_target",
        "give_order_khan.terminated",
        "challenge_score.duplicate_paths",
        "challenge_score.pooled_reuse",
        "character_select_widget.null_texture",
        "character_select_widget.disposed_texture",
        "ambient_audio.invalid_locator",
        "static_list.add_full",
        "static_list.insert_full",
        "static_list.entity_add_full",
        "static_list.spell_add_full",
        "static_weak_list.add_full",
        "static_weak_list.insert_full",
        "static_weak_list.character_add_full",
        "railgun.parent_cycle_candidate",
        "railgun.parent_check_limit",
        "railgun.lock_cycle",
        "animation_clip.lookup_missing",
        "animation_clip.invalid_slot",
        "animation_clip.null_set",
        "animation_clip.out_of_range",
        "animation_clip.missing_idle",
        "character_spell_usage.detached_gamer",
        "graphics_error.xna_argument",
        "graphics_error.no_suitable_device",
        "level_hash.missing_file",
        "mouse_resolution.lower",
        "mouse_resolution.higher",
        "mouse_resolution.negative",
        "mouse_resolution.upper_bound",
        "borderless.initial_handler_order",
        "borderless.preserve_backbuffer",
        "borderless.logical_fullscreen",
        "borderless.clear_topmost",
        "process_thread.null",
        "system_library_preload.startup",
        "system_library_preload.windows_paths",
        "program_arguments.truncated_connect_lobby",
        "program_arguments.truncated_connect",
        "program_arguments.truncated_password",
        "keyboard_mouse_clear.seeded",
        "keyboard_mouse_interactable.missing_avatar",
        "keyboard_mouse_interactable.missing_play_state",
        "keyboard_mouse_interactable.missing_level",
        "keyboard_mouse_interactable.missing_scene",
        "keyboard_mouse_interactable.missing_triggers",
        "radial_blur.level_content_release",
        "radial_blur.current_scene",
        "radial_blur.cache_clear",
        "summon_phoenix.vector_state",
        "summon_phoenix.owner_state",
        "summon_phoenix.update_state",
        "vlad.constructor_state_release",
        "vlad.current_state_initialize",
        "napalm.execute_state_release",
        "napalm.current_state_update",
        "thunderbolt.vector_state_release",
        "thunderbolt.owner_state_release",
        "thunderbolt.current_state_cast",
        "entanglement.shared_effect",
        "entanglement.initialize_without_effect_update",
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
        "direct_input.options_load_failure",
        "direct_input.discovery_load_failure",
        "direct_input.warning_once",
        "interactable_highlight.missing_scene",
        "interactable_highlight.missing_level_model",
        "audio_stop_all.disposed_cue",
        "deflection_aura.play_state_release",
        "flash.scene_release",
        "flash.current_scene",
        "spawn_slime.play_state_release",
        "spawn_slime_overkill.play_state_release",
        "spawn_slime.current_nav_mesh",
        "spawn_slime.spawn_slimes_current_nav_mesh",
        "poison_spray.play_state_release",
        "poison_spray.current_query_manager",
        "chilly_blast.play_state_release",
        "chilly_blast.current_query_manager",
        "star_gaze.detached_victim",
        "confuse_who.detached_victim",
        "confuse.detached_target",
        "action_lifecycle.clear_references",
        "action_lifecycle.state_reset_tag",
        "dialog_manager.level_reference_release",
        "dialog_manager.empty_additional_list",
        "homing_charge.execute_release",
        "stop_charge.execute_release",
        "homing_charge.current_query_manager",
        "stop_charge.current_play_state",
        "charge_abilities.level_dispose",
        "active_buff_cache.level_dispose",
        "entity_update.character_only",
        "entity_update.character_damageable",
        "summon_flamer.vector_release",
        "summon_flamer.owner_release",
        "summon_spirit.vector_release",
        "summon_spirit.owner_release",
        "summon_flamer.current_play_state",
        "summon_spirit.current_play_state",
        "summon_undead.vector_release",
        "summon_undead.owner_release",
        "summon_undead.current_play_state",
        "summon_undead.level_dispose",
        "undead_network.host_marker",
        "undead_network.client_marked",
        "summon_zombie.vector_release",
        "summon_zombie.owner_release",
        "summon_zombie.update_current_play_state",
        "summon_templates.level_dispose",
        "ability_template_cache.level_dispose",
        "summon_cross.vector_release",
        "summon_cross.owner_release",
        "summon_cross.current_play_state",
        "summon_cross.level_dispose",
        "static_level_pools.level_dispose",
        "lightning_bolt_cache.level_dispose",
        "elemental_egg_cache.level_dispose",
        "elemental_egg_teardown.retained_graph",
        "item_pickable_cache.level_dispose",
        "physics_entity_template_cache.level_dispose",
        "character_template_cache.shared_template",
        "judgement_spray.empty_condition_cache",
        "blizzard_cleanup.active_release",
        "blizzard_cleanup.stop_failure_release",
        "blizzard.vector_current_play_state",
        "blizzard.owner_current_play_state",
        "blizzard.update_current_play_state",
        "rain.vector_current_play_state",
        "rain.owner_current_play_state",
        "rain.update_current_play_state",
        "rain.remove_releases_scene",
        "thunderstorm.vector_current_play_state",
        "thunderstorm.owner_current_play_state",
        "thunderstorm.update_current_play_state",
        "thunderstorm.remove_current_play_state",
        "animated_level_part.detached_entity",
        "animated_level_part.missing_entity",
        "animated_level_part.dispose_idempotent",
        "animated_level_part.dispose_children",
        "animated_level_part.dispose_liquid",
        "dynamic_light_cache.level_dispose",
        "meteor_shower.vector_current_play_state",
        "meteor_shower.owner_current_play_state",
        "meteor_shower.active_release",
        "meteor_shower.stop_failure_release",
        "meteor_shower.already_stopping_release"
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
        "payload_contract.compatible_pair",
        "payload_contract.rejects_missing_pair",
        "ui_render.activation",
        "ui_render.projected_positions",
        "ui_render.notifier_restoration",
        "ui_render.screen_size",
        "entity_manager_state.retention",
        "entity_manager_state.cache_state",
        "damageable_deinitialize.gib_release",
        "damageable_deinitialize.resistance_release",
        "damageable_deinitialize.cache_order",
        "boss_health_bar.constructor_release",
        "hud_manager.disabled_original_hud",
        "hud_manager.enabled_original_hud",
        "versus_revive.missing_avatar",
        "versus_revive.missing_requested_avatar",
        "versus_revive.available_avatar",
        "sub_menu_main.gamepad_back",
        "sub_menu_main.keyboard_back",
        "chilly_blast.play_state_release",
        "chilly_blast.execute_behavior",
        "chilly_blast.current_query_manager",
        "direct_input.warning_once",
        "paradox_popup.plain_clears_extra",
        "paradox_popup.with_extra_unchanged",
        "character_select_widget.null_texture",
        "character_select_widget.disposed_texture",
        "character_select_widget.live_texture",
        "character_select_widget.non_image",
        "hotjoin_broadcast.two_syncing_players",
        "pool_expansion.generic_boss",
        "pool_expansion.damageable_physics_entity",
        "physics_entity_template_cache.level_dispose",
        "physics_entity_template_cache.uninitialized_dispose",
        "character_template_cache.shared_template",
        "character_template_cache.empty",
        "spell_mine.animated_part_release"
    )
    $matrix = New-Object System.Collections.Generic.List[string]

    Test-BehaviorProfile "current-original" $originalPath "unpatched" $patchFailures @() $matrix
    Test-BehaviorProfile "current-manual-patch" $currentPatchPath "unpatched" `
        @("undead_network.host_marker", "undead_network.client_marked", "healing_rain.remove_releases_references", "healing_rain.remove_without_scene", "late_udp.empty_client_list", "late_udp.negative_client_index", "character_template_cache.shared_template", "confuse.attached_target", "game_scene.dispose_releases_kill_plane_tag", "telemetry_backoff.category_cap") @() $matrix
    Test-BehaviorProfile "current-runtime-patch" $originalPath "runtime" @() @() $matrix
    Test-BehaviorProfile "1.4.16.0-original" $version14Path "unpatched" `
        @("avatar_interactable.missing_play_state", "avatar_interactable.missing_level", "avatar_interactable.missing_scene", "avatar_interactable.missing_triggers", "ai_attack.bodyless_target", "ai_move.enter_bodyless_target", "ai_move.execute_bodyless_target", "agent_target.bodyless_player", "closest_damageable.bodyless_candidate", "entity_query.bodyless_entry", "entity_query.null_entry", "entity_clear.stale_grid", "entity_state_storage.constructor_release", "entity_state_storage.current_restore", "helper_array_equals.left_null", "helper_array_equals.right_null", "helper_array_equals.both_null", "inventory.initial_screen_size", "inventory.changed_screen_size", "camera_follow.bodyless_target", "boss_health_bar.current_scene", "boss_health_bar.setter_release", "machine.missing_warlock", "jormungandr.missing_target", "portal_queue.null_then_bodyless", "portal_queue.bodyless_then_null", "pack_license.custom_offline_license", "pack_license.custom_offline_enabled", "pack_license.custom_insecure_license", "pack_license.custom_insecure_enabled", "drink_blood.play_state_release", "random_mine.play_state_release", "starfall.play_state_release", "starfall.current_play_state", "drain_life.play_state_release", "sub_menu_main.gamepad_back", "company_state.exit_cleanup_order", "control_manager.null_controller", "control_manager.playerless_controller", "interactable_highlight.missing_scene", "interactable_highlight.missing_level_model", "audio_stop_all.disposed_cue", "deflection_aura.play_state_release", "flash.scene_release", "flash.current_scene", "spawn_slime.play_state_release", "spawn_slime_overkill.play_state_release", "spawn_slime.current_nav_mesh", "spawn_slime.spawn_slimes_current_nav_mesh", "poison_spray.play_state_release", "poison_spray.current_query_manager", "summon_flamer.vector_release", "summon_flamer.owner_release", "summon_spirit.vector_release", "summon_spirit.owner_release", "summon_flamer.current_play_state", "summon_spirit.current_play_state", "summon_templates.level_dispose", "summon_cross.vector_release", "summon_cross.owner_release", "summon_cross.current_play_state", "summon_cross.level_dispose", "star_gaze.detached_victim", "confuse_who.detached_victim", "homing_charge.execute_release", "stop_charge.execute_release", "homing_charge.current_query_manager", "stop_charge.current_play_state", "charge_abilities.level_dispose", "active_buff_cache.level_dispose", "entity_update.character_only", "entity_update.character_damageable", "ability_template_cache.level_dispose", "loading_screen.managed_restore_order", "static_level_pools.level_dispose", "judgement_spray.empty_condition_cache", "blizzard_cleanup.active_release", "blizzard_cleanup.stop_failure_release", "animated_level_part.detached_entity", "animated_level_part.missing_entity", "dynamic_light_cache.level_dispose") `
        $legacyNotAvailable $matrix
    Test-BehaviorProfile "1.4.16.0-runtime-patch" $version14Path "runtime" `
        @() $legacyNotAvailable $matrix
    Test-BehaviorProfile "1.5.1.0-original" $version15Path "unpatched" `
        @("avatar_interactable.missing_play_state", "avatar_interactable.missing_level", "avatar_interactable.missing_scene", "avatar_interactable.missing_triggers", "ai_attack.bodyless_target", "ai_move.enter_bodyless_target", "ai_move.execute_bodyless_target", "agent_target.bodyless_player", "closest_damageable.bodyless_candidate", "entity_query.bodyless_entry", "entity_query.null_entry", "entity_clear.stale_grid", "entity_state_storage.constructor_release", "entity_state_storage.current_restore", "helper_array_equals.left_null", "helper_array_equals.right_null", "helper_array_equals.both_null", "inventory.initial_screen_size", "inventory.changed_screen_size", "camera_follow.bodyless_target", "boss_health_bar.current_scene", "boss_health_bar.setter_release", "machine.missing_warlock", "jormungandr.missing_target", "portal_queue.null_then_bodyless", "portal_queue.bodyless_then_null", "pack_license.custom_offline_license", "pack_license.custom_offline_enabled", "pack_license.custom_insecure_license", "pack_license.custom_insecure_enabled", "drink_blood.play_state_release", "random_mine.play_state_release", "starfall.play_state_release", "starfall.current_play_state", "drain_life.play_state_release", "sub_menu_main.gamepad_back", "company_state.exit_cleanup_order", "control_manager.null_controller", "control_manager.playerless_controller", "interactable_highlight.missing_scene", "interactable_highlight.missing_level_model", "audio_stop_all.disposed_cue", "deflection_aura.play_state_release", "flash.scene_release", "flash.current_scene", "spawn_slime.play_state_release", "spawn_slime_overkill.play_state_release", "spawn_slime.current_nav_mesh", "spawn_slime.spawn_slimes_current_nav_mesh", "poison_spray.play_state_release", "poison_spray.current_query_manager", "summon_flamer.vector_release", "summon_flamer.owner_release", "summon_spirit.vector_release", "summon_spirit.owner_release", "summon_flamer.current_play_state", "summon_spirit.current_play_state", "summon_templates.level_dispose", "summon_cross.vector_release", "summon_cross.owner_release", "summon_cross.current_play_state", "summon_cross.level_dispose", "star_gaze.detached_victim", "confuse_who.detached_victim", "homing_charge.execute_release", "stop_charge.execute_release", "homing_charge.current_query_manager", "stop_charge.current_play_state", "charge_abilities.level_dispose", "active_buff_cache.level_dispose", "entity_update.character_only", "entity_update.character_damageable", "ability_template_cache.level_dispose", "loading_screen.managed_restore_order", "static_level_pools.level_dispose", "judgement_spray.empty_condition_cache", "blizzard_cleanup.active_release", "blizzard_cleanup.stop_failure_release", "animated_level_part.detached_entity", "animated_level_part.missing_entity", "dynamic_light_cache.level_dispose") `
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
    if ($profile -eq "1.4.16.0-original" -or
        $profile -eq "1.5.1.0-original") {
        $expectedFailures = @($expectedFailures) + @(
            "level_current_state.read_sites",
            "game_scene.current_play_state",
            "game_scene.menu_controller_reset",
            "game_scene.light_update_current_state",
            "game_scene.dispose_complete",
            "game_scene.dispose_idempotent",
            "game_scene.dispose_releases_kill_plane_tag",
            "telemetry_backoff.repeat",
            "telemetry_backoff.independent_key",
            "telemetry_backoff.category_cap",
            "telemetry_context.navigation",
            "telemetry_context.display",
            "telemetry_context.language",
            "telemetry_context.navigation_bound",
            "telemetry_context.payload",
            "warlord_ability.diagnostic_before_cast",
            "boss_fight.setup_state_release",
            "boss_fight.initialize_current_state",
            "boss_fight.reset_current_state",
            "boss_fight.update_current_state",
            "boss_fight.pending_initialize",
            "boss_fight.pending_start",
            "boss_fight.pending_clear",
            "summon_death.owner_state",
            "summon_death.vector_state",
            "summon_death.spawn_state",
            "death_entity.constructor_state",
            "death_entity.initialize_state",
            "death_entity.update_state",
            "death_entity.deinitialize_state",
            "summon_death.level_cleanup",
            "icon_renderer.constructor_state_release",
            "icon_renderer.initialize_state_release",
            "icon_renderer.current_game_type",
            "direct_input.options_load_failure",
            "direct_input.discovery_load_failure",
            "give_order_khan.terminated",
            "challenge_score.duplicate_paths",
            "challenge_score.pooled_reuse",
            "ambient_audio.invalid_locator",
            "static_list.add_full",
            "static_list.insert_full",
            "static_list.entity_add_full",
            "static_list.spell_add_full",
            "static_weak_list.add_full",
            "static_weak_list.insert_full",
            "static_weak_list.character_add_full",
            "railgun.parent_cycle_candidate",
            "railgun.parent_check_limit",
            "railgun.lock_cycle",
            "animation_clip.lookup_missing",
            "animation_clip.invalid_slot",
            "animation_clip.null_set",
            "animation_clip.out_of_range",
            "animation_clip.missing_idle",
            "character_spell_usage.detached_gamer",
            "graphics_error.xna_argument",
            "graphics_error.no_suitable_device",
            "level_hash.missing_file",
            "mouse_resolution.lower",
            "mouse_resolution.higher",
            "mouse_resolution.negative",
            "mouse_resolution.upper_bound",
            "borderless.initial_handler_order",
            "borderless.preserve_backbuffer",
            "borderless.logical_fullscreen",
            "borderless.clear_topmost",
            "process_thread.null",
            "system_library_preload.startup",
            "system_library_preload.windows_paths",
            "program_arguments.truncated_connect_lobby",
            "program_arguments.truncated_connect",
            "program_arguments.truncated_password",
            "keyboard_mouse_clear.seeded",
            "keyboard_mouse_interactable.missing_avatar",
            "keyboard_mouse_interactable.missing_play_state",
            "keyboard_mouse_interactable.missing_level",
            "keyboard_mouse_interactable.missing_scene",
            "keyboard_mouse_interactable.missing_triggers",
            "animated_level_part.dispose_idempotent",
            "animated_level_part.dispose_children",
            "animated_level_part.dispose_liquid",
            "radial_blur.level_content_release",
            "radial_blur.current_scene",
            "radial_blur.cache_clear",
            "action_lifecycle.clear_references",
            "action_lifecycle.state_reset_tag",
            "dialog_manager.level_reference_release",
            "dialog_manager.empty_additional_list",
            "entity_physics_cleanup.deinitialize",
            "entity_physics_cleanup.reuse_fallback",
            "entity_physics_cleanup.final_teardown",
            "damageable_teardown.final_references",
            "animated_physics_lifecycle.deinitialize",
            "animated_physics_lifecycle.final_teardown",
            "npc_teardown.derived_state",
            "npc_lifecycle.deinitialize",
            "character_teardown.final_references",
            "character_grip.missing_target",
            "character_grip.actor_body_missing",
            "character_grip.target_body_missing",
            "character_grip.controller_missing",
            "typing_text.truncated_plain",
            "typing_text.truncated_markup",
            "typing_text.empty",
            "late_udp.empty_client_list",
            "late_udp.negative_client_index",
            "enter_sync.unknown_sender",
            "ruleset_update.detached_play_state",
            "missile_event.uninitialized_state",
            "missile_event.missing_collision_target",
            "missile_event.missing_collision_target_cleanup",
            "forced_sync.invalid_sender",
            "forced_sync.sparse_players",
            "forced_sync.missing_avatar",
            "trigger_authority.peer_spawn",
            "trigger_lifecycle.missing_item_slot",
            "trigger_lifecycle.wrong_item_slot_type",
            "trigger_lifecycle.active_living_npc",
            "trigger_lifecycle.missing_active_target",
            "network_pickup.bodyless_pickup",
            "network_pickup.bodyless_pickup_request",
            "summon_phoenix.vector_state",
            "summon_phoenix.owner_state",
            "summon_phoenix.update_state",
            "vlad.constructor_state_release",
            "vlad.current_state_initialize",
            "napalm.execute_state_release",
            "napalm.current_state_update",
            "thunderbolt.vector_state_release",
            "thunderbolt.owner_state_release",
            "thunderbolt.current_state_cast",
            "entanglement.shared_effect",
            "entanglement.initialize_without_effect_update",
            "menu_image_text.literal_font_change",
            "menu_image_text.localized_font_change",
            "language_manager.simplified_chinese_name",
            "language_manager.simplified_chinese_aliases",
            "dialog_layout.list_breaks",
            "dialog_layout.element_sections",
            "shadow_blobs.matching_scene",
            "player_controller_avatar.matching_release",
            "player_text_box.active_release",
            "player_notifier.active_release",
            "chant_spell_cleanup.initialized_dispose",
            "summon_undead.vector_release",
            "summon_undead.owner_release",
            "summon_undead.current_play_state",
            "summon_undead.level_dispose",
            "undead_network.host_marker",
            "undead_network.client_marked",
            "summon_zombie.vector_release",
            "summon_zombie.owner_release",
            "summon_zombie.update_current_play_state",
            "meteor_shower.vector_current_play_state",
            "meteor_shower.owner_current_play_state",
            "meteor_shower.active_release",
            "meteor_shower.stop_failure_release",
            "meteor_shower.already_stopping_release",
            "blizzard.vector_current_play_state",
            "blizzard.owner_current_play_state",
            "blizzard.update_current_play_state",
            "widescreen.horizontal_ultrawide",
            "widescreen.horizontal_16_9",
            "widescreen.right_ultrawide",
            "widescreen.right_16_9",
            "tutorial_play_state.initialize_release",
            "tutorial_play_state.current_update",
            "ethereal_clone.play_state_release",
            "ethereal_clone.current_nav_mesh",
            "break_barriers.play_state_release",
            "break_barriers.current_entity_manager",
            "tesla_field.initialized_pool_release",
            "tesla_field.empty_pool_allocation_release",
            "generic_health_bar.current_scene",
            "grease_trail.play_state_release",
            "grease_trail.current_play_state",
            "grease.play_state_release",
            "grease.current_play_state",
            "grease.cache_cleanup",
            "grease.cache_cleanup_idempotent",
            "grease_lump.current_play_state",
            "underground_attack.play_state_release",
            "underground_attack.initialize_current_play_state",
            "underground_attack.update_current_play_state",
            "arcane_blade.initialize_current_scene",
            "arcane_blade.update_current_scene",
            "conflagration.vector_state",
            "conflagration.direction_state",
            "conflagration.owner_state",
            "conflagration.update_state",
            "wave.vector_state",
            "wave.direction_state",
            "wave.owner_state",
            "wave.update_state",
            "spell_mine.animated_part_release",
            "polymorph.owner_state",
            "polymorph.vector_state",
            "polymorph.target_state",
            "polymorph.remove_state",
            "floor_stomp.execute_state",
            "floor_stomp.update_state",
            "revive.execute_state",
            "revive.update_state",
            "portal.execute_state_release",
            "portal.vector_initialize_current_state",
            "portal.message_initialize_current_state",
            "portal.update_current_state",
            "portal.level_cleanup",
            "grow.orphaned_owner",
            "confuse.detached_target",
            "spell_effect.initialize_release",
            "lightning_spell.cached_current_play_state",
            "lightning_spell.empty_cache_current_play_state",
            "lightning_spell.cast_current_play_state",
            "push_spell.get_from_cache_current_play_state",
            "push_spell.return_to_cache_current_play_state",
            "spray_spell.get_from_cache_current_play_state",
            "spray_spell.return_to_cache_current_play_state",
            "spray_spell.cast_update_current_play_state",
            "projectile_spell.get_from_cache_current_play_state",
            "projectile_spell.return_to_cache_current_play_state",
            "railgun_spell.get_from_cache_current_play_state",
            "railgun_spell.return_to_cache_current_play_state",
            "railgun_spell.cast_self_current_play_state",
            "railgun_spell.cast_weapon_current_play_state",
            "railgun_spell.deinitialize_current_play_state",
            "pool_expansion.avatar",
            "pool_expansion.gib",
            "pool_expansion.projectile_spell",
            "pool_expansion.spray_spell",
            "pool_expansion.railgun_spell",
            "pool_expansion.shield_spell",
            "projectile_spawn.null_owner",
            "projectile_spell.empty_condition_cache",
            "projectile_spell.null_missile_result",
            "projectile_spell.detached_missile_state",
            "prop_boss.level_teardown",
            "fairy_teardown.listed",
            "fairy_teardown.avatar_owned",
            "fairy_teardown.npc_owned",
            "barrier_teardown.active_instance",
            "barrier_teardown.cached_instance",
            "barrier_teardown.hit_list_cache",
            "shield.global_content_owner",
            "gib_teardown.active_instance",
            "gib_teardown.cached_instance",
            "effect_manager.duplicate_name",
            "time_warp.start_current_state",
            "time_warp.update_current_state",
            "time_warp.remove_current_state",
            "time_warp_staff.start_current_state",
            "time_warp_staff.update_current_state",
            "time_warp_staff.remove_current_state",
            "spell_wheel.initialize_release",
            "spell_wheel.current_scene",
            "earthquake.execute_current_scene",
            "earthquake.current_camera",
            "earthquake.current_entity_manager",
            "arrow_rain.vector_release",
            "arrow_rain.owner_release",
            "arrow_rain.update_current_play_state",
            "arrow_rain.remove_current_scene",
            "healing_rain.vector_current_play_state",
            "healing_rain.owner_current_play_state",
            "healing_rain.update_current_play_state",
            "healing_rain.remove_releases_references",
            "healing_rain.remove_without_scene",
            "in_game_menu.initialize_release",
            "in_game_menu.current_scene",
            "in_game_menu_stack.initialized_dispose",
            "magicks_language.negative_selection",
            "magicks_language.past_end_selection",
            "rain.vector_current_play_state",
            "rain.owner_current_play_state",
            "rain.update_current_play_state",
            "rain.remove_releases_scene",
            "thunderstorm.vector_current_play_state",
            "thunderstorm.owner_current_play_state",
            "thunderstorm.update_current_play_state",
            "thunderstorm.remove_current_play_state",
            "lightning_bolt_cache.level_dispose",
            "elemental_egg_cache.level_dispose",
            "elemental_egg_teardown.retained_graph",
            "item_pickable_cache.level_dispose")
    }
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
        "level_current_state.read_sites",
        "level_current_state.read_count_preserved",
        "ui_render.activation",
        "ui_render.projected_positions",
        "ui_render.notifier_restoration",
        "ui_render.screen_size",
        "avatar_interactable.missing_play_state",
        "avatar_interactable.missing_level",
        "avatar_interactable.missing_scene",
        "avatar_interactable.missing_triggers",
        "avatar_interactable.empty_scene",
        "network_pickup.missing_target",
        "network_pickup.bodyless_pickup",
        "network_pickup.bodyless_pickup_request",
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
        "entity_manager_state.retention",
        "entity_manager_state.cache_state",
        "entity_state_storage.constructor_release",
        "entity_state_storage.current_restore",
        "entity_state_storage.empty_restore",
        "icon_renderer.constructor_state_release",
        "icon_renderer.initialize_state_release",
        "icon_renderer.current_game_type",
        "icon_renderer.selection_shape",
        "damageable_deinitialize.gib_release",
        "damageable_deinitialize.resistance_release",
        "damageable_deinitialize.cache_order",
        "summon_death.owner_state",
        "summon_death.vector_state",
        "summon_death.spawn_state",
        "summon_death.spawn_shape",
        "death_entity.constructor_state",
        "death_entity.initialize_state",
        "death_entity.update_state",
        "death_entity.deinitialize_state",
        "summon_death.level_cleanup",
        "helper_array_equals.equal",
        "helper_array_equals.different",
        "helper_array_equals.left_null",
        "helper_array_equals.right_null",
        "helper_array_equals.both_null",
        "inventory.initial_screen_size",
        "inventory.changed_screen_size",
        "widescreen.horizontal_ultrawide",
        "widescreen.horizontal_16_9",
        "widescreen.right_ultrawide",
        "widescreen.right_16_9",
        "tutorial_play_state.initialize_release",
        "tutorial_play_state.current_update",
        "ethereal_clone.play_state_release",
        "ethereal_clone.current_nav_mesh",
        "break_barriers.play_state_release",
        "break_barriers.current_entity_manager",
        "tesla_field.initialized_pool_release",
        "tesla_field.empty_pool_allocation_release",
        "generic_health_bar.current_scene",
        "grease_trail.play_state_release",
        "grease_trail.current_play_state",
        "grease.play_state_release",
        "grease.current_play_state",
        "grease.cache_cleanup",
        "grease.cache_cleanup_idempotent",
        "grease_lump.current_play_state",
        "underground_attack.play_state_release",
        "underground_attack.initialize_current_play_state",
        "underground_attack.update_current_play_state",
        "arcane_blade.initialize_current_scene",
        "arcane_blade.update_current_scene",
        "conflagration.vector_state",
        "conflagration.direction_state",
        "conflagration.owner_state",
        "conflagration.update_state",
        "wave.vector_state",
        "wave.direction_state",
        "wave.owner_state",
        "wave.update_state",
        "spell_mine.animated_part_release",
        "polymorph.owner_state",
        "polymorph.vector_state",
        "polymorph.target_state",
        "polymorph.remove_state",
        "floor_stomp.execute_state",
        "floor_stomp.update_state",
        "revive.execute_state",
        "revive.update_state",
        "portal.execute_state_release",
        "portal.vector_initialize_current_state",
        "portal.message_initialize_current_state",
        "portal.update_current_state",
        "portal.level_cleanup",
        "grow.orphaned_owner",
        "grow.owner_present",
        "spell_effect.initialize_release",
        "lightning_spell.cached_current_play_state",
        "lightning_spell.empty_cache_current_play_state",
        "lightning_spell.cast_current_play_state",
        "push_spell.get_from_cache_current_play_state",
        "push_spell.return_to_cache_current_play_state",
        "spray_spell.get_from_cache_current_play_state",
        "spray_spell.return_to_cache_current_play_state",
        "spray_spell.cast_update_current_play_state",
        "projectile_spell.get_from_cache_current_play_state",
        "projectile_spell.return_to_cache_current_play_state",
        "railgun_spell.get_from_cache_current_play_state",
        "railgun_spell.return_to_cache_current_play_state",
        "railgun_spell.cast_self_current_play_state",
        "railgun_spell.cast_weapon_current_play_state",
        "railgun_spell.deinitialize_current_play_state",
        "pool_expansion.avatar",
        "pool_expansion.generic_boss",
        "pool_expansion.damageable_physics_entity",
        "pool_expansion.gib",
        "pool_expansion.projectile_spell",
        "pool_expansion.spray_spell",
        "pool_expansion.railgun_spell",
        "pool_expansion.shield_spell",
        "projectile_spawn.null_owner",
        "projectile_spawn.complete_owner",
        "projectile_spell.empty_condition_cache",
        "projectile_spell.cached_condition_identity",
        "projectile_spell.null_missile_result",
        "projectile_spell.detached_missile_state",
        "projectile_spell.usable_missile_state",
        "game_scene.current_play_state",
        "game_scene.same_play_state",
        "game_scene.menu_controller_reset",
        "game_scene.menu_controller_already_clear",
        "game_scene.light_update_current_state",
        "game_scene.light_update_call_preserved",
        "game_scene.dispose_complete",
        "game_scene.dispose_idempotent",
        "game_scene.dispose_releases_kill_plane_tag",
        "telemetry_backoff.repeat",
        "telemetry_backoff.independent_key",
        "telemetry_backoff.category_cap",
        "telemetry_context.navigation",
        "telemetry_context.display",
        "telemetry_context.language",
        "telemetry_context.navigation_bound",
        "telemetry_context.payload",
        "warlord_ability.diagnostic_before_cast",
        "prop_boss.level_teardown",
        "fairy_teardown.listed",
        "fairy_teardown.avatar_owned",
        "fairy_teardown.npc_owned",
        "barrier_teardown.active_instance",
        "barrier_teardown.cached_instance",
        "barrier_teardown.hit_list_cache",
        "shield.global_content_owner",
        "shield.graphics_load_shape",
        "gib_teardown.active_instance",
        "gib_teardown.cached_instance",
        "effect_manager.duplicate_name",
        "effect_manager.unique_names",
        "time_warp.start_current_state",
        "time_warp.update_current_state",
        "time_warp.remove_current_state",
        "time_warp_staff.start_current_state",
        "time_warp_staff.update_current_state",
        "time_warp_staff.remove_current_state",
        "spell_wheel.initialize_release",
        "spell_wheel.current_scene",
        "earthquake.execute_current_scene",
        "earthquake.current_camera",
        "earthquake.current_entity_manager",
        "arrow_rain.vector_release",
        "arrow_rain.owner_release",
        "arrow_rain.update_current_play_state",
        "arrow_rain.remove_current_scene",
        "healing_rain.vector_current_play_state",
        "healing_rain.owner_current_play_state",
        "healing_rain.update_current_play_state",
        "healing_rain.remove_releases_references",
        "healing_rain.remove_without_scene",
        "in_game_menu.initialize_release",
        "in_game_menu.current_scene",
        "in_game_menu_stack.initialized_dispose",
        "in_game_menu_stack.uninitialized_dispose",
        "magicks_language.negative_selection",
        "magicks_language.past_end_selection",
        "magicks_language.valid_selection",
        "camera_follow.bodyless_target",
        "camera_follow.missing_target",
        "camera_follow.other_behavior",
        "boss_health_bar.constructor_release",
        "boss_health_bar.current_scene",
        "boss_health_bar.setter_release",
        "loading_screen.managed_restore_order",
        "loading_screen.unmanaged_no_restore",
        "menu_image_text.literal_font_change",
        "menu_image_text.localized_font_change",
        "menu_image_text.localized_unchanged_font",
        "paradox_popup.plain_clears_extra",
        "paradox_popup.with_extra_unchanged",
        "language_manager.simplified_chinese_name",
        "language_manager.simplified_chinese_aliases",
        "language_manager.existing_lookup",
        "dialog_layout.list_breaks",
        "dialog_layout.dramatic_aside",
        "dialog_layout.existing_list_break",
        "dialog_layout.element_sections",
        "dialog_layout.ordinary_hint",
        "dialog_layout.existing_hint_breaks",
        "shadow_blobs.matching_scene",
        "shadow_blobs.replacement_scene",
        "player_controller_avatar.matching_release",
        "player_controller_avatar.replacement_retained",
        "player_controller_avatar.non_null_assignment",
        "player_text_box.active_release",
        "player_text_box.empty_release",
        "player_text_box.missing_text_box",
        "player_notifier.active_release",
        "player_notifier.empty_release",
        "player_notifier.missing_notifier",
        "chant_spell_cleanup.uninitialized_dispose",
        "chant_spell_cleanup.initialized_dispose",
        "hud_manager.disabled_original_hud",
        "hud_manager.enabled_original_hud",
        "machine.missing_warlock",
        "machine.valid_warlock",
        "machine.other_message",
        "boss_fight.setup_state_release",
        "boss_fight.initialize_current_state",
        "boss_fight.reset_current_state",
        "boss_fight.update_current_state",
        "boss_fight.pending_initialize",
        "boss_fight.pending_start",
        "boss_fight.pending_clear",
        "boss_fight.original_shape",
        "jormungandr.missing_target",
        "jormungandr.before_warning",
        "give_order_khan.terminated",
        "give_order_khan.live",
        "give_order_khan.other_order",
        "give_order_khan.zero_trigger",
        "challenge_score.duplicate_paths",
        "challenge_score.pooled_reuse",
        "challenge_score.distinct_enemies",
        "character_select_widget.null_texture",
        "character_select_widget.disposed_texture",
        "character_select_widget.live_texture",
        "character_select_widget.non_image",
        "ambient_audio.invalid_locator",
        "ambient_audio.valid_locator",
        "ambient_audio.other_exception",
        "static_list.add_full",
        "static_list.insert_full",
        "static_list.below_capacity",
        "static_list.entity_add_full",
        "static_list.spell_add_full",
        "static_weak_list.add_full",
        "static_weak_list.insert_full",
        "static_weak_list.below_capacity",
        "static_weak_list.character_add_full",
        "railgun.parent_cycle_candidate",
        "railgun.acyclic_candidate",
        "railgun.parent_check_limit",
        "railgun.lock_cycle",
        "railgun.lock_acyclic",
        "animation_clip.lookup_missing",
        "animation_clip.lookup_present",
        "animation_clip.invalid_slot",
        "animation_clip.valid_slot",
        "animation_clip.null_set",
        "animation_clip.out_of_range",
        "animation_clip.missing_idle",
        "character_spell_usage.detached_gamer",
        "character_spell_usage.local_gamer",
        "character_spell_usage.network_gamer",
        "graphics_error.xna_argument",
        "graphics_error.other_argument",
        "graphics_error.no_suitable_device",
        "graphics_error.other_exception",
        "level_hash.missing_file",
        "level_hash.other_io",
        "mouse_resolution.lower",
        "mouse_resolution.higher",
        "mouse_resolution.negative",
        "mouse_resolution.upper_bound",
        "mouse_resolution.equal",
        "borderless.initial_handler_order",
        "borderless.preserve_backbuffer",
        "borderless.logical_fullscreen",
        "borderless.windowed",
        "borderless.clear_topmost",
        "borderless.keep_topmost",
        "process_thread.null",
        "process_thread.matching",
        "process_thread.nonmatching",
        "system_library_preload.startup",
        "system_library_preload.windows_paths",
        "system_library_preload.non_windows",
        "program_arguments.truncated_connect_lobby",
        "program_arguments.truncated_connect",
        "program_arguments.truncated_password",
        "program_arguments.valid_values",
        "program_arguments.unrelated",
        "keyboard_mouse_clear.seeded",
        "keyboard_mouse_clear.empty",
        "keyboard_mouse_interactable.missing_avatar",
        "keyboard_mouse_interactable.missing_play_state",
        "keyboard_mouse_interactable.missing_level",
        "keyboard_mouse_interactable.missing_scene",
        "keyboard_mouse_interactable.missing_triggers",
        "keyboard_mouse_interactable.empty_scene",
        "radial_blur.level_content_release",
        "radial_blur.current_scene",
        "radial_blur.same_scene",
        "radial_blur.cache_clear",
        "radial_blur.empty_cache",
        "summon_phoenix.vector_state",
        "summon_phoenix.owner_state",
        "summon_phoenix.update_state",
        "vlad.constructor_state_release",
        "vlad.current_state_initialize",
        "napalm.execute_state_release",
        "napalm.current_state_update",
        "thunderbolt.vector_state_release",
        "thunderbolt.owner_state_release",
        "thunderbolt.current_state_cast",
        "entanglement.shared_effect",
        "entanglement.initialize_without_effect_update",
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
        "direct_input.options_load_failure",
        "direct_input.discovery_load_failure",
        "direct_input.unrelated_failure",
        "direct_input.available",
        "direct_input.warning_once",
        "interactable_highlight.missing_scene",
        "interactable_highlight.missing_level_model",
        "interactable_highlight.empty",
        "audio_stop_all.disposed_cue",
        "audio_stop_all.empty",
        "deflection_aura.play_state_release",
        "deflection_aura.execute_behavior",
        "flash.scene_release",
        "flash.current_scene",
        "spawn_slime.play_state_release",
        "spawn_slime_overkill.play_state_release",
        "spawn_slime.current_nav_mesh",
        "spawn_slime.spawn_slimes_current_nav_mesh",
        "poison_spray.play_state_release",
        "poison_spray.execute_behavior",
        "poison_spray.current_query_manager",
        "chilly_blast.play_state_release",
        "chilly_blast.execute_behavior",
        "chilly_blast.current_query_manager",
        "star_gaze.detached_victim",
        "star_gaze.empty",
        "confuse_who.detached_victim",
        "confuse_who.empty",
        "confuse.detached_target",
        "confuse.attached_target",
        "action_lifecycle.clear_references",
        "action_lifecycle.state_reset_tag",
        "action_lifecycle.empty_clear",
        "dialog_manager.level_reference_release",
        "dialog_manager.empty_additional_list",
        "entity_physics_cleanup.deinitialize",
        "entity_physics_cleanup.reuse_fallback",
        "entity_physics_cleanup.final_teardown",
        "damageable_teardown.final_references",
        "animated_physics_lifecycle.deinitialize",
        "animated_physics_lifecycle.final_teardown",
        "npc_teardown.derived_state",
        "npc_lifecycle.deinitialize",
        "character_teardown.final_references",
        "character_grip.missing_target",
        "character_grip.actor_body_missing",
        "character_grip.target_body_missing",
        "character_grip.controller_missing",
        "character_grip.non_grip",
        "typing_text.normal_character",
        "typing_text.punctuation",
        "typing_text.pause_markup",
        "typing_text.truncated_plain",
        "typing_text.truncated_markup",
        "typing_text.empty",
        "typing_text.syntax_error",
        "late_udp.empty_client_list",
        "late_udp.negative_client_index",
        "late_udp.valid_client_identity",
        "enter_sync.unknown_sender",
        "enter_sync.connected_sender",
        "ruleset_update.detached_play_state",
        "missile_event.uninitialized_state",
        "missile_event.missing_collision_target",
        "missile_event.valid_targetless_event",
        "missile_event.missing_collision_target_cleanup",
        "hotjoin_broadcast.two_syncing_players",
        "forced_sync.invalid_sender",
        "forced_sync.sparse_players",
        "forced_sync.missing_avatar",
        "trigger_authority.peer_spawn",
        "trigger_authority.server_spawn",
        "trigger_authority.peer_nonspawn",
        "trigger_lifecycle.missing_item_slot",
        "trigger_lifecycle.wrong_item_slot_type",
        "trigger_lifecycle.active_item_reuse",
        "trigger_lifecycle.inactive_item_slot",
        "trigger_lifecycle.active_living_npc",
        "trigger_lifecycle.active_dead_npc_reuse",
        "trigger_lifecycle.missing_active_target",
        "trigger_lifecycle.active_target",
        "homing_charge.execute_release",
        "stop_charge.execute_release",
        "homing_charge.current_query_manager",
        "stop_charge.current_play_state",
        "stop_charge.non_triggering_update",
        "charge_abilities.level_dispose",
        "active_buff_cache.level_dispose",
        "active_buff_cache.uninitialized_dispose",
        "entity_update.character_only",
        "entity_update.character_damageable",
        "entity_update.no_features",
        "summon_flamer.vector_release",
        "summon_flamer.owner_release",
        "summon_spirit.vector_release",
        "summon_spirit.owner_release",
        "summon_flamer.current_play_state",
        "summon_spirit.current_play_state",
        "summon_undead.current_play_state",
        "summon_undead.vector_release",
        "summon_undead.owner_release",
        "summon_undead.level_dispose",
        "summon_undead.uninitialized_dispose",
        "undead_network.host_marker",
        "undead_network.client_marked",
        "undead_network.client_normal",
        "undead_network.wire_marker_roundtrip",
        "summon_zombie.vector_release",
        "summon_zombie.owner_release",
        "summon_zombie.update_current_play_state",
        "summon_zombie.client_no_spawn",
        "summon_templates.level_dispose",
        "ability_template_cache.level_dispose",
        "ability_template_cache.empty_dispose",
        "summon_cross.vector_release",
        "summon_cross.owner_release",
        "summon_cross.current_play_state",
        "summon_cross.level_dispose",
        "static_level_pools.level_dispose",
        "static_level_pools.uninitialized_dispose",
        "lightning_bolt_cache.level_dispose",
        "lightning_bolt_cache.uninitialized_dispose",
        "elemental_egg_cache.level_dispose",
        "elemental_egg_cache.uninitialized_dispose",
        "elemental_egg_teardown.retained_graph",
        "elemental_egg_teardown.empty",
        "item_pickable_cache.level_dispose",
        "item_pickable_cache.uninitialized_dispose",
        "physics_entity_template_cache.level_dispose",
        "physics_entity_template_cache.uninitialized_dispose",
        "character_template_cache.shared_template",
        "character_template_cache.empty",
        "judgement_spray.empty_condition_cache",
        "judgement_spray.cached_condition_identity",
        "blizzard_cleanup.active_release",
        "blizzard_cleanup.stop_failure_release",
        "blizzard_cleanup.empty",
        "blizzard.vector_current_play_state",
        "blizzard.owner_current_play_state",
        "blizzard.update_current_play_state",
        "rain.vector_current_play_state",
        "rain.owner_current_play_state",
        "rain.update_current_play_state",
        "rain.remove_releases_scene",
        "thunderstorm.vector_current_play_state",
        "thunderstorm.owner_current_play_state",
        "thunderstorm.update_current_play_state",
        "thunderstorm.remove_current_play_state",
        "animated_level_part.detached_entity",
        "animated_level_part.missing_entity",
        "animated_level_part.expired_valid_entity",
        "animated_level_part.dispose_idempotent",
        "animated_level_part.dispose_children",
        "animated_level_part.dispose_liquid",
        "dynamic_light_cache.level_dispose",
        "meteor_shower.vector_current_play_state",
        "meteor_shower.owner_current_play_state",
        "meteor_shower.active_release",
        "meteor_shower.stop_failure_release",
        "meteor_shower.already_stopping_release"
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
        $auditLines -notcontains "patch_end=Agent owner refresh" -or
        $auditLines -notcontains "patch_end=Agent reusable state reset" -or
        $auditLines -notcontains "patch_end=Agent disabled state cleanup" -or
        $auditLines -notcontains "patch_end=Agent target scratch cleanup" -or
        $auditLines -notcontains "patch_end=Agent final teardown" -or
        $auditLines -notcontains "patch_end=Avatar detached interaction guard" -or
        $auditLines -notcontains "patch_end=EntityManager detached damageable guard" -or
        $auditLines -notcontains "patch_end=EntityManager detached spatial entry guard" -or
        $auditLines -notcontains "patch_end=EntityManager scene-transition grid cleanup" -or
        $auditLines -notcontains "patch_end=EntityStateStorage constructor play-state release" -or
        $auditLines -notcontains "patch_end=EntityStateStorage current play-state restore" -or
        $auditLines -notcontains "patch_end=Helper null-safe array equality" -or
        $auditLines -notcontains "patch_end=InventoryBox screen size" -or
        $auditLines -notcontains "patch_end=Keyboard HUD ultrawide safe area" -or
        $auditLines -notcontains "patch_end=Tutorial prompt ultrawide safe area" -or
        $auditLines -notcontains "patch_end=TutorialManager play-state release" -or
        $auditLines -notcontains "patch_end=TutorialManager current play-state resolution update" -or
        $auditLines -notcontains "patch_end=TutorialManager current play-state update" -or
        $auditLines -notcontains "patch_end=EtherealClone play-state release" -or
        $auditLines -notcontains "patch_end=EtherealClone current NavMesh" -or
        $auditLines -notcontains "patch_end=BreakBarriers play-state release" -or
        $auditLines -notcontains "patch_end=BreakBarriers current entity manager" -or
        $auditLines -notcontains "patch_end=MagickCamera detached follow target guard" -or
        $auditLines -notcontains "patch_end=BossHealthBar constructor scene release" -or
        $auditLines -notcontains "patch_end=BossHealthBar current scene getter" -or
        $auditLines -notcontains "patch_end=BossHealthBar legacy scene setter release" -or
        $auditLines -notcontains "patch_end=LoadingScreen depth-buffer restore order" -or
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
        $auditLines -notcontains "patch_end=Character-select custom pack display" -or
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
        $auditLines -notcontains "patch_end=Controller options constructor DirectInput guard" -or
        $auditLines -notcontains "patch_end=Controller options entry DirectInput guard" -or
        $auditLines -notcontains "patch_end=Menu controller scan DirectInput guard" -or
        $auditLines -notcontains "patch_end=Deferred DirectInput warning" -or
        $auditLines -notcontains "patch_end=Paradox account menu-exit lifetime" -or
        $auditLines -notcontains "patch_end=Paradox account shutdown cleanup" -or
        $auditLines -notcontains "patch_end=Interactable detached scene highlight guard" -or
        $auditLines -notcontains "patch_end=AudioManager disposed cue guard" -or
        $auditLines -notcontains "patch_end=DeflectionAura unused play-state release" -or
        $auditLines -notcontains "patch_end=MenuImageTextItem language font refresh" -or
        $auditLines -notcontains "patch_end=Simplified Chinese display name" -or
        $auditLines -notcontains "patch_end=Simplified Chinese language aliases" -or
        $auditLines -notcontains "patch_end=Paradox popup stale extra-message cleanup" -or
        $auditLines -notcontains "patch_end=Flash scene reference release" -or
        $auditLines -notcontains "patch_end=Flash current scene update" -or
        $auditLines -notcontains "patch_end=SpawnSlime play-state reference release" -or
        $auditLines -notcontains "patch_end=SpawnSlimeOverkill play-state reference release" -or
        $auditLines -notcontains "patch_end=SpawnSlime current NavMesh" -or
        $auditLines -notcontains "patch_end=SpawnSlime SpawnSlimes current NavMesh" -or
        $auditLines -notcontains "patch_end=PoisonSpray play-state reference release" -or
        $auditLines -notcontains "patch_end=PoisonSpray current entity query" -or
        $auditLines -notcontains "patch_end=ChillyBlast play-state reference release" -or
        $auditLines -notcontains "patch_end=ChillyBlast current entity query" -or
        $auditLines -notcontains "patch_end=StarGaze detached victim faction cleanup" -or
        $auditLines -notcontains "patch_end=HomingCharge play-state reference release" -or
        $auditLines -notcontains "patch_end=HomingCharge current entity query" -or
        $auditLines -notcontains "patch_end=StopCharge play-state reference release" -or
        $auditLines -notcontains "patch_end=StopCharge current GreaseSplash state" -or
        $auditLines -notcontains "patch_end=Charge ability level cache cleanup" -or
        $auditLines -notcontains "patch_end=Haste and Shrink level cache cleanup" -or
        $auditLines -notcontains "patch_end=Active chant-spell level cleanup" -or
        $auditLines -notcontains "patch_end=Static level pool cleanup" -or
        $auditLines -notcontains "patch_end=Lightning bolt cache cleanup" -or
        $auditLines -notcontains "patch_end=Elemental egg cache cleanup" -or
        $auditLines -notcontains "patch_end=ElementalEgg final teardown" -or
        $auditLines -notcontains "patch_end=Pickable item cache release" -or
        $auditLines -notcontains "patch_end=Physics entity template cache cleanup" -or
        $auditLines -notcontains "patch_end=Character template shared-asset cache cleanup" -or
        $auditLines -notcontains "patch_end=JudgementSpray empty condition-cache recovery" -or
        $auditLines -notcontains "patch_end=Blizzard vector play-state release" -or
        $auditLines -notcontains "patch_end=Blizzard owner play-state release" -or
        $auditLines -notcontains "patch_end=Blizzard current execute play state" -or
        $auditLines -notcontains "patch_end=Blizzard current update play state" -or
        $auditLines -notcontains "patch_end=Blizzard singleton reference cleanup" -or
        $auditLines -notcontains "patch_end=Rain vector play-state release" -or
        $auditLines -notcontains "patch_end=Rain owner play-state release" -or
        $auditLines -notcontains "patch_end=Rain current execute play state" -or
        $auditLines -notcontains "patch_end=Rain current update play state" -or
        $auditLines -notcontains "patch_end=Rain scene and caster cleanup" -or
        $auditLines -notcontains "patch_end=Thunderstorm vector play-state release" -or
        $auditLines -notcontains "patch_end=Thunderstorm owner play-state release" -or
        $auditLines -notcontains "patch_end=Thunderstorm current update play state" -or
        $auditLines -notcontains "patch_end=Thunderstorm remove reference cleanup" -or
        $auditLines -notcontains "patch_end=AnimatedLevelPart detached entity cleanup" -or
        $auditLines -notcontains "patch_end=Water liquid effect ownership" -or
        $auditLines -notcontains "patch_end=Lava liquid effect ownership" -or
        $auditLines -notcontains "patch_end=AnimatedLevelPart resource disposal" -or
        $auditLines -notcontains "patch_end=DynamicLight cache release" -or
        $auditLines -notcontains "patch_end=MeteorShower vector play-state release" -or
        $auditLines -notcontains "patch_end=MeteorShower owner play-state release" -or
        $auditLines -notcontains "patch_end=MeteorShower current scene selection" -or
        $auditLines -notcontains "patch_end=MeteorShower current missile play state" -or
        $auditLines -notcontains "patch_end=MeteorShower singleton reference cleanup" -or
        $auditLines -notcontains "patch_end=NetworkServer EntityUpdate Character marker decode" -or
        $auditLines -notcontains "patch_end=NetworkClient EntityUpdate Character marker decode" -or
        $auditLines -notcontains "patch_end=SummonFlamer vector play-state release" -or
        $auditLines -notcontains "patch_end=SummonFlamer owner play-state release" -or
        $auditLines -notcontains "patch_end=SummonSpirit vector play-state release" -or
        $auditLines -notcontains "patch_end=SummonSpirit owner play-state release" -or
        $auditLines -notcontains "patch_end=SummonFlamer current play-state spawn" -or
        $auditLines -notcontains "patch_end=SummonSpirit current play-state spawn" -or
        $auditLines -notcontains "patch_end=SummonUndead vector play-state release" -or
        $auditLines -notcontains "patch_end=SummonUndead owner play-state release" -or
        $auditLines -notcontains "patch_end=SummonUndead current play-state spawn" -or
        $auditLines -notcontains "patch_end=SummonZombie vector play-state release" -or
        $auditLines -notcontains "patch_end=SummonZombie owner play-state release" -or
        $auditLines -notcontains "patch_end=SummonZombie current start play state" -or
        $auditLines -notcontains "patch_end=SummonZombie current update play state" -or
        $auditLines -notcontains "patch_end=SummonUndead network state marker" -or
        $auditLines -notcontains "patch_end=SpawnNPC undead state application" -or
        $auditLines -notcontains "patch_end=SummonCross vector play-state release" -or
        $auditLines -notcontains "patch_end=SummonCross owner play-state release" -or
        $auditLines -notcontains "patch_end=SummonCross current play-state spawn" -or
        $auditLines -notcontains "patch_end=Summon ability template cleanup" -or
        $auditLines -notcontains "patch_end=Dialog list line breaks" -or
        $auditLines -notcontains "patch_end=Element hint line breaks" -or
        $auditLines -notcontains "patch_end=ShadowBlobs scene release" -or
        $auditLines -notcontains "patch_end=Player controller avatar release" -or
        $auditLines -notcontains "patch_end=Player obtained text-box level release" -or
        $auditLines -notcontains "patch_end=Player notifier level release" -or
        $auditLines -notcontains "patch_end=Telemetry play-state navigation context" -or
        $auditLines -notcontains "patch_end=Telemetry scene navigation context" -or
        $auditLines -notcontains "patch_end=Telemetry restored-scene navigation context" -or
        $auditLines -notcontains "patch_end=Telemetry menu navigation context" -or
        $auditLines -notcontains "patch_end=Telemetry cached resolution context" -or
        $auditLines -notcontains "patch_end=Telemetry cached language context" -or
        $auditLines -notcontains "patch_end=Normal-close telemetry context" -or
        $auditLines -notcontains "patch_end=Crash telemetry context" -or
        $auditLines -notcontains "patch_end=Warlord primary-ability diagnostic" -or
        $auditLines -notcontains "patch_end=TeslaField play-state release" -or
        $auditLines -notcontains "patch_end=GenericHealthBar current scene" -or
        $auditLines -notcontains "patch_end=GreaseTrail play-state release" -or
        $auditLines -notcontains "patch_end=GreaseTrail current play state" -or
        $auditLines -notcontains "patch_end=Grease play-state release" -or
        $auditLines -notcontains "patch_end=Grease current play state" -or
        $auditLines -notcontains "patch_end=GreaseField constructor play-state release" -or
        $auditLines -notcontains "patch_end=GreaseField current play state" -or
        $auditLines -notcontains "patch_end=Grease level cache cleanup" -or
        $auditLines -notcontains "patch_end=GreaseLump play-state release" -or
        $auditLines -notcontains "patch_end=GreaseLump current play state" -or
        $auditLines -notcontains "patch_end=UnderGroundAttack play-state release" -or
        $auditLines -notcontains "patch_end=UnderGroundAttack current initialize play state" -or
        $auditLines -notcontains "patch_end=UnderGroundAttack current update play state" -or
        $auditLines -notcontains "patch_end=ArcaneBlade play-state release" -or
        $auditLines -notcontains "patch_end=ArcaneBlade current render scene" -or
        $auditLines -notcontains "patch_end=Conflagration vector play-state release" -or
        $auditLines -notcontains "patch_end=Conflagration direction play-state release" -or
        $auditLines -notcontains "patch_end=Conflagration owner play-state release" -or
        $auditLines -notcontains "patch_end=Conflagration current play state" -or
        $auditLines -notcontains "patch_end=Wave vector play-state release" -or
        $auditLines -notcontains "patch_end=Wave direction play-state release" -or
        $auditLines -notcontains "patch_end=Wave owner play-state release" -or
        $auditLines -notcontains "patch_end=Wave current play state" -or
        $auditLines -notcontains "patch_end=SpellMine animated level-part release" -or
        $auditLines -notcontains "patch_end=Polymorph owner play-state release" -or
        $auditLines -notcontains "patch_end=Polymorph vector play-state release" -or
        $auditLines -notcontains "patch_end=Polymorph target play-state release" -or
        $auditLines -notcontains "patch_end=Polymorph current removal state" -or
        $auditLines -notcontains "patch_end=FloorStomp play-state release" -or
        $auditLines -notcontains "patch_end=FloorStomp current play state" -or
        $auditLines -notcontains "patch_end=Revive play-state release" -or
        $auditLines -notcontains "patch_end=Revive current play state" -or
        $auditLines -notcontains "patch_end=Grow orphaned owner guard" -or
        $auditLines -notcontains "patch_end=SpellEffect play-state release" -or
        $auditLines -notcontains "patch_end=LightningSpell current cache play state" -or
        $auditLines -notcontains "patch_end=LightningSpell current cast play state" -or
        $auditLines -notcontains "patch_end=EffectManager duplicate asset guard" -or
        $auditLines -notcontains "patch_end=TimeWarp vector play-state release" -or
        $auditLines -notcontains "patch_end=TimeWarp owner play-state release" -or
        $auditLines -notcontains "patch_end=TimeWarp current update play state" -or
        $auditLines -notcontains "patch_end=TimeWarp current removal play state" -or
        $auditLines -notcontains "patch_end=TimeWarpStaff play-state release" -or
        $auditLines -notcontains "patch_end=TimeWarpStaff current update play state" -or
        $auditLines -notcontains "patch_end=TimeWarpStaff current removal play state" -or
        $auditLines -notcontains "patch_end=SpellWheel play-state release" -or
        $auditLines -notcontains "patch_end=SpellWheel current render scene" -or
        $auditLines -notcontains "patch_end=EarthQuake play-state release" -or
        $auditLines -notcontains "patch_end=EarthQuake current camera" -or
        $auditLines -notcontains "patch_end=EarthQuake current entity manager" -or
        $auditLines -notcontains "patch_end=ArrowRain vector play-state release" -or
        $auditLines -notcontains "patch_end=ArrowRain owner play-state release" -or
        $auditLines -notcontains "patch_end=ArrowRain scene reference release" -or
        $auditLines -notcontains "patch_end=ArrowRain current launch play state" -or
        $auditLines -notcontains "patch_end=ArrowRain current update play state" -or
        $auditLines -notcontains "patch_end=ArrowRain current removal scene" -or
        $auditLines -notcontains "patch_end=HealingRain vector play-state release" -or
        $auditLines -notcontains "patch_end=HealingRain owner play-state release" -or
        $auditLines -notcontains "patch_end=HealingRain current execute play state" -or
        $auditLines -notcontains "patch_end=HealingRain current update play state" -or
        $auditLines -notcontains "patch_end=HealingRain scene and caster cleanup" -or
        $auditLines -notcontains "patch_end=InGameMenu play-state release" -or
        $auditLines -notcontains "patch_end=InGameMenu deferred current-state patch installation" -or
        $auditLines -notcontains "patch_end=In-game menu stack cleanup" -or
        $auditLines -notcontains "patch_end=Magicks menu language selection guard" -or
        $auditLines -notcontains "patch_end=Challenge score direct-damage guard" -or
        $auditLines -notcontains "patch_end=Challenge score kill-event guard" -or
        $auditLines -notcontains "patch_end=Challenge score pooled-enemy reset" -or
        $auditLines -notcontains "patch_end=Character-select disposed widget texture guard" -or
        $auditLines -notcontains "patch_end=Ambient audio invalid locator recovery" -or
        $auditLines -notcontains "patch_end=StaticList Int32 stable Add" -or
        $auditLines -notcontains "patch_end=StaticList Spell stable Insert" -or
        $auditLines -notcontains "patch_end=EntityManager StaticList growth" -or
        $auditLines -notcontains "patch_end=TriggerArea StaticWeakList growth" -or
        $auditLines -notcontains "patch_end=Railgun parent-cycle prevention" -or
        $auditLines -notcontains "patch_end=Railgun cycle-safe lock traversal" -or
        $auditLines -notcontains "patch_end=AnimationClipAction missing-key recovery" -or
        $auditLines -notcontains "patch_end=CharacterTemplate invalid animation-slot filter" -or
        $auditLines -notcontains "patch_end=PhysicsEntityTemplate invalid animation-slot filter" -or
        $auditLines -notcontains "patch_end=Character missing-idle initialization recovery" -or
        $auditLines -notcontains "patch_end=Character missing-idle crossfade recovery" -or
        $auditLines -notcontains "patch_end=Character missing-idle force recovery" -or
        $auditLines -notcontains "patch_end=AnimatedPhysicsEntity idle crossfade fallback" -or
        $auditLines -notcontains "patch_end=AnimatedPhysicsEntity idle force fallback" -or
        $auditLines -notcontains "patch_end=Character detached-Gamer spell statistics guard" -or
        $auditLines -notcontains "patch_end=Graphics startup error guidance" -or
        $auditLines -notcontains "patch_end=Missing level hash file handling" -or
        $auditLines -notcontains "patch_end=Borderless mouse coordinate scaling" -or
        $auditLines -notcontains "patch_end=Initial graphics handler ordering" -or
        $auditLines -notcontains "patch_end=Borderless presentation device settings" -or
        $auditLines -notcontains "patch_end=Borderless fullscreen topmost normalization" -or
        $auditLines -notcontains "patch_end=Unavailable process thread guard" -or
        $auditLines -notcontains "patch_end=Keyboard mouse stale target cleanup" -or
        $auditLines -notcontains "patch_end=Keyboard mouse detached interaction guard" -or
        $auditLines -notcontains "patch_end=RadialBlur global content lifetime" -or
        $auditLines -notcontains "patch_end=RadialBlur scene retention removal" -or
        $auditLines -notcontains "patch_end=RadialBlur current-scene rendering" -or
        $auditLines -notcontains "patch_end=RadialBlur disposed-cache release" -or
        $auditLines -notcontains "patch_end=SummonPhoenix vector play-state lifetime" -or
        $auditLines -notcontains "patch_end=SummonPhoenix owner play-state lifetime" -or
        $auditLines -notcontains "patch_end=SummonPhoenix current play-state update" -or
        $auditLines -notcontains "patch_end=Vlad play-state reference release" -or
        $auditLines -notcontains "patch_end=Vlad current play-state initialization" -or
        $auditLines -notcontains "patch_end=Napalm play-state reference release" -or
        $auditLines -notcontains "patch_end=Napalm current play-state update" -or
        $auditLines -notcontains "patch_end=Thunderbolt vector play-state release" -or
        $auditLines -notcontains "patch_end=Thunderbolt owner play-state release" -or
        $auditLines -notcontains "patch_end=Thunderbolt current play-state cast" -or
        $auditLines -notcontains "patch_end=Entanglement shared render effect" -or
        $auditLines -notcontains "patch_end=Entanglement redundant effect update removal" -or
        $auditLines -notcontains "patch_end=ConfuseWho detached victim faction cleanup" -or
        $auditLines -notcontains "patch_end=Confuse detached target faction cleanup" -or
        $auditLines -notcontains "patch_end=Action instance reference cleanup" -or
        $auditLines -notcontains "patch_end=Action state tag cleanup" -or
        $auditLines -notcontains "patch_end=Action cleanup at play-state disposal" -or
        $auditLines -notcontains "patch_end=DialogManager level reference cleanup" -or
        $auditLines -notcontains "patch_end=PhysicsEntity stale physics replacement cleanup" -or
        $auditLines -notcontains "patch_end=PhysicsEntity deinitialization cleanup" -or
        $auditLines -notcontains "patch_end=Entity level teardown cleanup" -or
        $auditLines -notcontains "patch_end=TypingText malformed-state recovery" -or
        $auditLines -notcontains "patch_end=NetworkServer late UDP client guard: Magicka.Network.EntityRemoveMessage" -or
        $auditLines -notcontains "patch_end=Avatar late network pickup guard" -or
        $auditLines -notcontains "patch_end=Character late grip packet guard" -or
        $auditLines -notcontains "patch_end=NetworkServer EnterSync client guard" -or
        $auditLines -notcontains "patch_end=NetworkClient detached RulesetUpdate guard" -or
        $auditLines -notcontains "patch_end=NetworkClient TriggerAction server authority" -or
        $auditLines -notcontains "patch_end=TriggerAction lifecycle validation" -or
        $auditLines -notcontains "patch_end=MissileEntity invalid network event guard" -or
        $auditLines -notcontains "patch_end=NetworkServer hotjoin broadcast continuation: Magicka.Network.EntityRemoveMessage" -or
        $auditLines -notcontains "patch_end=NetworkServer forced sync player and sender resolution" -or
        $auditLines -notcontains "patch_end=NetworkServer forced sync response builder" -or
        $auditLines -notcontains "patch_end=IconRenderer constructor play-state release" -or
        $auditLines -notcontains "patch_end=IconRenderer initialization play-state release" -or
        $auditLines -notcontains "patch_end=IconRenderer current play-state magick selection" -or
        $auditLines -notcontains "patch_end=DamageablePhysicsEntity inactive template release" -or
        $auditLines -notcontains "patch_end=SummonDeath owner play-state release" -or
        $auditLines -notcontains "patch_end=SummonDeath vector play-state release" -or
        $auditLines -notcontains "patch_end=SummonDeath current play-state spawn" -or
        $auditLines -notcontains "patch_end=SummonDeath entity constructor play-state release" -or
        $auditLines -notcontains "patch_end=SummonDeath entity current play-state initialization" -or
        $auditLines -notcontains "patch_end=SummonDeath entity current play-state update" -or
        $auditLines -notcontains "patch_end=SummonDeath entity current play-state removal" -or
        $auditLines -notcontains "patch_end=SummonDeath singleton entity cleanup" -or
        $auditLines -notcontains "patch_end=BossFight play-state setup release" -or
        $auditLines -notcontains "patch_end=BossFight deferred client initialization" -or
        $auditLines -notcontains "patch_end=BossFight current initialization play state" -or
        $auditLines -notcontains "patch_end=BossFight deferred client start" -or
        $auditLines -notcontains "patch_end=BossFight pending-state clear" -or
        $auditLines -notcontains "patch_end=BossFight pending-state reset" -or
        $auditLines -notcontains "patch_end=BossFight current reset play state" -or
        $auditLines -notcontains "patch_end=BossFight pending initialization retry" -or
        $auditLines -notcontains "patch_end=BossFight current update play state" -or
        $auditLines -notcontains "patch_end=BossFight pending packet completion" -or
        $auditLines -notcontains "patch_end=PushSpell current cache insertion play state" -or
        $auditLines -notcontains "patch_end=PushSpell current cache return play state" -or
        $auditLines -notcontains "patch_end=SpraySpell current cache insertion play state" -or
        $auditLines -notcontains "patch_end=SpraySpell current cache return play state" -or
        $auditLines -notcontains "patch_end=SpraySpell current geometry play state" -or
        $auditLines -notcontains "patch_end=ProjectileSpell current cache insertion play state" -or
        $auditLines -notcontains "patch_end=ProjectileSpell current cache return play state" -or
        $auditLines -notcontains "patch_end=RailGunSpell current cache insertion play state" -or
        $auditLines -notcontains "patch_end=RailGunSpell current cache return play state" -or
        $auditLines -notcontains "patch_end=RailGunSpell current self-cast play state" -or
        $auditLines -notcontains "patch_end=RailGunSpell current weapon-cast play state" -or
        $auditLines -notcontains "patch_end=RailGunSpell current removal play state" -or
        $auditLines -notcontains "patch_end=Avatar exhausted pool recovery" -or
        $auditLines -notcontains "patch_end=GenericBoss exhausted pool recovery" -or
        $auditLines -notcontains "patch_end=DamageablePhysicsEntity exhausted pool recovery" -or
        $auditLines -notcontains "patch_end=Gib exhausted pool recovery" -or
        $auditLines -notcontains "patch_end=ProjectileSpell exhausted pool recovery" -or
        $auditLines -notcontains "patch_end=SpraySpell exhausted pool recovery" -or
        $auditLines -notcontains "patch_end=RailGunSpell exhausted pool recovery" -or
        $auditLines -notcontains "patch_end=ShieldSpell exhausted pool recovery" -or
        $auditLines -notcontains "patch_end=PropBoss level teardown" -or
        $auditLines -notcontains "patch_end=Fairy level teardown" -or
        $auditLines -notcontains "patch_end=Character final teardown" -or
        $auditLines -notcontains "patch_end=NonPlayerCharacter final teardown" -or
        $auditLines -notcontains "patch_end=NonPlayerCharacter reusable teardown begin" -or
        $auditLines -notcontains "patch_end=NonPlayerCharacter reusable teardown finish" -or
        $auditLines -notcontains "patch_end=Barrier level teardown" -or
        $auditLines -notcontains "patch_end=Gib level teardown" -or
        $auditLines -notcontains "patch_end=ForceField play-state release" -or
        $auditLines -notcontains "patch_end=ForceField current play state" -or
        $auditLines -notcontains "patch_end=LevelModel complete teardown" -or
        $auditLines -notcontains "patch_end=Portal play-state release" -or
        $auditLines -notcontains "patch_end=Portal current vector initialization play state" -or
        $auditLines -notcontains "patch_end=Portal current message initialization play state" -or
        $auditLines -notcontains "patch_end=Portal current update play state" -or
        $auditLines -notcontains "patch_end=Portal level teardown" -or
        $auditLines -notcontains "patch_end=Shield global graphics content lifetime" -or
        $auditLines -notcontains "patch_end=ProjectileSpell detached owner guard" -or
        $auditLines -notcontains "patch_end=ProjectileSpell empty condition-cache recovery" -or
        $auditLines -notcontains "patch_end=ProjectileSpell incomplete missile guard" -or
        $auditLines -notcontains "patch_end=GameScene current play-state getter" -or
        $auditLines -notcontains "patch_end=GameScene menu-controller reset" -or
        $auditLines -notcontains "patch_end=GameScene current light-update play state" -or
        $auditLines -notcontains "patch_end=GameScene complete teardown" -or
        $auditLines -notcontains "patch_end=EntityManager play-state release" -or
        $auditLines -notcontains "patch_end=High-resolution UI render activation" -or
        $auditLines -notcontains "patch_end=TextBox projected UI scaling" -or
        $auditLines -notcontains "patch_end=Cutscene text projected UI scaling" -or
        $auditLines -notcontains "patch_end=IconRenderer projected UI scaling" -or
        $auditLines -notcontains "patch_end=SpellWheel projected UI scaling" -or
        $auditLines -notcontains "patch_end=Notifier projected UI scaling" -or
        $auditLines -notcontains "patch_end=Notifier projected position restoration" -or
        $auditLines -notcontains "patch_end=Level state restore current play state" -or
        $auditLines -notcontains "patch_end=Level current play-state getter" -or
        $auditLines -notcontains "patch_end=Level update current play state" -or
        $auditLines -notcontains "patch_end=Level scene change current play state" -or
        $auditLines -notcontains "patch_end=Level transition clear current play state" -or
        @($auditLines | Where-Object { $_ -eq "patch_kind=prefix" }).Count -ne 85 -or
        @($auditLines | Where-Object { $_ -eq "patch_kind=postfix" }).Count -ne 21 -or
        @($auditLines | Where-Object { $_ -eq "patch_kind=transpiler" }).Count -ne 429) {
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
    $summary.Add("implemented_patches=535")
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
