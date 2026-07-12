# Changelog

## [0.0.25] - 2026-07-12

### Fixed

- Reset an AI agent when a cached non-player character is deinitialized so it
  no longer retains stale owner and runtime state between uses.
- Restore the current non-player character as the agent owner during
  initialization before the agent is enabled again.
- Clear a deinitialized missile's owner, target, and collision-target
  references so expired missiles no longer retain related entities.
- Pin the server authentication-token buffer inside a dedicated helper so
  future dnSpy recompilations keep passing the correct buffer to Steam.

### Added

- Log detailed network-guard diagnostics when an `EntityUpdate` packet refers
  to an unknown entity handle, including the sender's player, avatar, and
  play-state context for investigating multiplayer failures between levels.

## [0.0.24] - 2026-07-12

### Fixed

- Reapply a character's template when a received `CharacterAction` packet
  resolves to a character whose `Template` is null. The template is restored
  with `ReApplyTemplate()` before `NetworkAction` processes the packet.

## [0.0.23] - 2026-07-12

### Fixed

- Avoid disposing connected network-player avatars twice while leaving a
  `PlayState`; `PlayState.Dispose()` remains responsible for avatar cleanup.
- Guard avatar inventory cleanup when the owning play state or inventory has
  already been cleared during teardown.

## [0.0.22] - 2026-07-12

### Fixed

- Restore the correct pinned Steam authentication token-buffer pointer after a
  dnSpy recompilation had passed the address of the local pointer variable and
  prevented multiplayer connections.

### Added

- Add a small files-only ZIP for manual Windows and Linux installation.
- Add independent settings for online release checks and automatic update
  installation.
- Show an available newer patch version in the in-game version text.

### Changed

- Enable telemetry and update checks when `patch-settings.ini` is absent, while
  keeping automatic update installation disabled by default.
- Use the files-only release asset for optional game-side updates.

## [0.0.21] - 2026-07-11

### Fixed

- Harden network play-state teardown and its delayed cleanup paths.
- Expand diagnostics around delayed network-player state transitions and avatar
  disposal failures.

## [0.0.20] - 2026-07-11

### Fixed

- Guard trigger actions that can run after their owning play state is no longer
  available.

## [0.0.19] - 2026-07-10

### Added

- Add localized installer and updater UI text.

## [0.0.18] - 2026-07-10

### Added

- Expand the Windows installer, updater, and uninstaller workflow.
- Add game and installer telemetry disclosure and controls.

### Changed

- Extend release automation and payload packaging for the Flutter tools.

## [0.0.17] - 2026-07-10

### Fixed

- Fix a `TimeWarp` teardown crash.

## [0.0.16] - 2026-07-09

### Changed

- Prepare the versioned release after the preceding stability fixes. The
  historical release commit does not record a narrower user-visible change.

## [0.0.15] - 2026-07-09

### Fixed

- Fix a crash while removing the `Confuse` faction effect.

## [0.0.12 - 0.0.14] - 2026-07-04 to 2026-07-06

The three historical tags reference the same tagged commit. Nearby commits in
the release line record the following work without a reliable one-to-one tag
mapping:

### Added

- Add patch telemetry and documented network-guard diagnostics.
- Add the Flutter installer UI and strengthen network synchronization.

### Fixed

- Harden missile disposal and related network paths.
- Limit cutscene loading to the selected content.

## [0.0.11] - 2026-06-01

### Fixed

- Fix a loading-screen crash caused by clearing with a null depth-stencil
  buffer.

## [0.0.10] - 2026-05-30

### Fixed

- Keep avatar animations alive during deinitialization.

## [0.0.9] - 2026-05-30

### Changed

- Update the patch version displayed in the main menu.

## [0.0.8] - 2026-05-29

### Fixed

- Restore `PlayState`-level loading for assets owned by a level.

## [0.0.6 - 0.0.7] - 2026-05-28

Both tags reference the same fix.

### Fixed

- Clear stale cached weapon items after scene-content disposal.

## [0.0.5] - 2026-05-28

### Changed

- Update the patched `Magicka.exe`. The historical commit does not record a
  narrower user-visible description.

## [0.0.4] - 2026-05-27

### Fixed

- Correct shared-content disposal and ability-template lifetimes.

## [0.0.3] - 2026-05-26

### Fixed

- Correct render re-enable timing during scene transitions and save loading.

## [0.0.2] - 2026-05-25

### Fixed

- Stabilize memory cleanup and disposal behavior.

## [0.0.1] - 2026-05-24

### Added

- Publish the initial tagged Magicka Community Patch release and project
  documentation.
