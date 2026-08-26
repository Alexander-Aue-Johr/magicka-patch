# Changelog

## [0.0.44] - 2026-08-26

### Fixed

- Add an in-game Controls menu for switching between Magicka's original
  controller scheme and the Community Patch's Magicka 2-style scheme without
  restarting the game.
- Restore empty-queue force push in the Magicka 2-style scheme. Force fields can
  now be strengthened with either left-stick click or D-pad right, while
  right-stick click continues to clear the spell queue.
- Apply tutorial element availability to the Magicka 2-style HUD and element
  buttons. Switching from the controller to mouse and keyboard now preserves
  the elements already unlocked by the tutorial.
- Accept the active cache entries that Magicka deliberately reuses for item,
  elemental, grease, tornado, and dead-NPC spawn packets. The 0.0.42 guard
  still rejects live NPC replacement and incompatible entity types.
- Remove a consumed missile locally when its collision target no longer exists.
  Target-dependent damage remains skipped, but the projectile and its particle
  effect no longer stay in the scene.
- Resolve forced player-status synchronization by player ID and sender, and
  build compact response arrays when player slots contain gaps.
- Continue a server broadcast after queueing a cacheable packet for a syncing
  player, so later live clients receive the same packet.
- Replicate the necromancer staff's undead-summon flag to clients through an
  existing unused `SpawnNPC` field. The packet layout remains unchanged for
  mixed patch versions.
- Reject NPC, luggage, elemental, item, magick, physics, grease, and tornado
  spawn actions on a client when their sender is not the connected server.

### Telemetry

- Add bounded, exponentially rate-limited validation events for recovered
  spawn reuse, missile cleanup, forced status sync, hotjoin broadcasts, and
  undead summons. A repeated event reports how many matching events were
  skipped since the previous send.
- Add diagnostics for active handle reuse and stale damage attackers. These
  record the state needed for the next correction without changing uncertain
  gameplay behavior.
- Record non-server TriggerAction types that are not yet assigned server or
  peer authority. Known host-owned spawn actions are blocked immediately.

### Installer

- Add the Linux desktop runner and a complete Linux installer package alongside
  the Windows installer and files-only package.
- Keep the separate Linux download link unchanged when the Windows release
  script updates versioned README links.

## [0.0.43] - 2026-08-26

### Fixed

- Prevent the campaign transition from blocking for roughly 100 seconds after
  the intro is skipped while legacy Paradox store prices are requested. The
  price refresh now runs on a guarded ThreadPool worker instead of the render
  thread, and the obsolete offers endpoint uses HTTPS. Prices are still applied
  if the background requests eventually succeed.

### Installer

- Fix automatic Magicka discovery in Linux builds by using native path
  separators and searching native Steam, Flatpak, Snap, and Proton client
  locations, including additional libraries from `libraryfolders.vdf`.
- Verify original `Magicka.exe` and `PolygonHead.dll` backups by size and
  SHA-256 before using them for installation or uninstallation. Invalid files
  with backup-looking names are preserved but never treated as originals.
- When verified originals are unavailable, offer to open Magicka's Steam file
  validation with `steam://validate/42910`. The installer waits for the player
  to confirm completion, verifies both restored files, and saves them under
  `CommunityPatch\backup` before continuing.

### Telemetry

- Add an optional startup backup audit to measure whether players already have
  usable original-file backups for a future local patcher. The audit runs only
  on the background telemetry worker when usage sharing is enabled and reports
  status values only; it does not submit paths, file names, contents, or hashes.

### Documentation

- Document the reproduction and staged diagnosis of the Paradox store
  render-thread hang, including the confirmed blocking call path and the final
  asynchronous patch behavior.

## [0.0.42] - 2026-08-10

### Fixed

- Allow client `TriggerActionMessage`s during normal `PlayState` scene
  initialization instead of dropping the entire packet class until
  `Initialized` or `WorldSync` becomes true. This restores fixed NPC spawns and
  related scene/animation triggers used by Dark Power and other scripted
  multiplayer maps.
- Render gameplay GUI with a configurable virtual layout size over the native
  scene when a `PlayState` uses a backbuffer of at least 2560x1440. The GUI is
  rasterized into a native-resolution UI target, so 200% scaling at
  3840x2160 uses a 1920x1080 layout while retaining 4K text and edge detail
  instead of enlarging a lower-resolution UI image.
  The completed scene is copied into the UI target before GUI drawing, and the
  resulting opaque image is copied back without a second alpha blend. This
  preserves the original strength of translucent black menu dimming, dialog
  backgrounds, and shadows instead of making them appear washed out.
  The in-game graphics menu now opens a scrollable `UI Scale` selector with
  `Off` and 25-percent steps from `125%` through `400%`. Selecting a value
  closes the selector and runs the resolution menu's existing UI refresh path,
  preventing mixed old/new virtual coordinates; the value persists in
  `CommunityPatch/ui-scale.ini`. This enlarges the spell HUD, paused in-game
  menu, dialog/subtitle text, and cutscene text. Added
  `InGameUiRenderScale.Begin`, `End`,
  `SetEnabled`, `SetScale`, `GetScaleFactor`, and `ShouldScale`; changed
  `InGameMenuOptionsGraphics..ctor`, `IControllerSelect`,
  `IGetHighlightedButtonName`, `OnEnter`, and `UpdatePositions`; extended
  `InGameMenuOptionsResolution.OnEnter`, `IControllerSelect`,
  `IControllerBack`, and `OnExit` to host and apply the scale list; changed
  `RenderManager.RenderScene` and
  `Game.Draw`; and added virtual-size/mouse handling to `InGameMenu`'s
  constructor, `Show`, `UpdateAllPositions`, `MouseMove`, `MouseDown`,
  `MouseUp`, and `MouseScroll`. `InventoryBox.RenderData.Draw` now also updates
  its textbox effect with the virtual UI size so the Tab weapon/staff panel and
  its contents stay aligned. `TextBox.RenderData.Draw`,
  `CutsceneText.CutSceneRenderData.Draw`, `IconRenderer.RenderData.Draw`,
  `SpellWheel.RenderData.Draw`, and `NotifierButton.RenderData.Draw` convert
  their pre-rendered native projections to virtual UI coordinates, keeping
  dialogue/cutscene text, player spell icons, and contextual prompts such as
  `Pick Up` aligned. The notifier conversion preserves its post-projection
  layout offset so its background, key icon, text, and anchor scale together,
  and restores the cached native position after every draw so repeated GUI
  passes cannot rescale it again or leave duplicate prompts toward the
  upper-left corner.
  `RenderManager.mSize` and `mGUIScale` are no longer mutated during
  `DrawGui`; the changed `RenderManager.get_ScreenSize` and `get_GUIScale`
  accessors now return virtual values only to the active RenderThread UI pass.
  The LogicThread therefore always receives native coordinates, preventing
  interactive cursor icons from flickering and `Mouse.SetPosition` from
  warping the cursor to a half-scaled location.
- Scale physical mouse coordinates back to the selected render resolution in
  borderless fullscreen mode. UI hover/click hit testing and gameplay mouse
  picking now remain aligned with the visible operating-system cursor when the
  game renders below the monitor's native resolution.
- Keep loading character and animated-physics templates when an asset refers to
  an animation clip key that is absent from its skinned model. Invalid clip
  slots are left empty and animation playback falls back safely to `idle` when
  available.
- Avoid a follow-up null-reference crash when the requested animation and the
  `idle` fallback are both unavailable; the animation request is ignored
  instead.
- Prevent network trigger actions from initializing an NPC with a missing
  `CharacterTemplate`, overwriting an already-active spawn slot, or running
  during incomplete play-state initialization outside the WorldSync path.
- Treat an entity handle as usable for normal network actions only while that
  entity is still present in the current play state's `EntityManager`, so late
  action, damage, remove, and missile packets cannot target deinitialized
  pooled entities.
- Let `Character.Initialize` finish safely when a valid template has no usable
  default `idle` animation instead of dereferencing an empty animation slot.

### Telemetry

- Emit `magicka_patch_animation_clip_missing` once per unique asset, clip key,
  animation name, and animation value during a game process. At most 16 such
  events are sent per launch.
- Report rejected lifecycle and SpawnNPC packets through the rate-limited
  `magicka_patch_network_guard_drop` event, including only
  handles, template hashes, lifecycle flags, and entity type information.

### Technical changes

- Changed `AnimationClipAction..ctor`, `CharacterTemplate.Read`, and
  `PhysicsEntityTemplate.Read` to use a non-throwing clip lookup and leave
  invalid animation slots empty.
- Changed `Character.Initialize`, `Character.GoToAnimation`,
  `Character.ForceAnimation`, `AnimatedPhysicsEntity.GoToAnimation`, and
  `AnimatedPhysicsEntity.ForceAnimation` to validate animation-set and
  animation indices and to tolerate a missing default `idle` action.
- Changed `Trigger.NetworkAction(TriggerActionMessage&)` to validate received
  trigger actions before dispatch, including play-state readiness, spawn-slot
  lifecycle, cached templates, and active primary or secondary entity handles.
  This closes the confirmed path where `Trigger.SpawnNPC` could pass a null
  result from `CharacterTemplate.GetCachedTemplate` through
  `NonPlayerCharacter.Initialize` into `Character.Initialize`.
- Changed `NetworkEntityHandleGuard.Resolve` and `ResolveDamageTarget` to route
  normal network references through active-entity validation. A normal handle
  is active only when its entity exists, is not disposed, belongs to the
  current `PlayState`, and is contained in that state's `EntityManager`.
- Changed `PatchTelemetry.SendStartup` to initialize the per-process missing
  animation deduplication state.
- Added `AnimationClipCompatibility.InitializeSession`,
  `TryGetAnimationAction`, and `ReportMissingClip` for bounded missing-clip
  recovery and telemetry.
- Added `NetworkLifecycleCompatibility.ResolveActive` and
  `CanProcessTriggerAction`, with internal validation helpers for active
  entities, reserved spawn slots, cached templates, entity types, and
  rate-limited packet-drop reporting.

## [0.0.41] - 2026-07-28

### Fixed

- Allow custom Equipment and Magick packs to remain selectable in the Tome's
  **Allowed Drops** menu whenever Magicka's existing custom-content policy
  permits them: offline or in an online session without VAC protection.
- Fix the inconsistent license handling that caused custom content to black
  out pack slots. Modified equipment assets fail the official content-hash
  lookup and receive `License.Custom`; the original
  `ItemPack`/`MagickPack` setters and `DrawPacksList` accepted only
  `License.Yes`, so affected packs were forcibly disabled and drawn as locked.
- Use one shared license decision in the pack `License` and `Enabled` setters
  and in the Equipment/Magicks thumbnail checks. `License.Custom` now follows
  the existing offline/non-VAC rule, while `License.No` remains locked so
  missing or unowned DLC is never enabled.

## [0.0.40] - 2026-07-28

### Fixed

- Make character selection respond consistently to the currently used input
  device. A single offline player can leave with controller B or keyboard
  Escape, while players can still be removed individually when changing the
  local keyboard/controller setup.
- Preserve mixed local co-op joining: after a controller player joins, another
  player can still join an available slot with mouse and keyboard, and input
  focus switches seamlessly between keyboard/mouse and controller navigation.
- Make controller B on the root main menu follow the same exit-dialog path as
  keyboard Escape, including the normal menu transition instead of bypassing
  the existing dialog flow.
- Prevent content-unload crashes when a released asset object or its reference
  name is null. Only the invalid dictionary removal is skipped; the remaining
  cleanup continues normally.
- Prevent keyboard/mouse interaction scans from dereferencing missing avatar,
  play-state, level, scene, or trigger state during gameplay teardown.
- Expire an orphaned Grow effect safely when its owner has already been
  removed.
- Allocate a replacement `SpraySpell` when its reusable-object cache is null
  or empty instead of indexing outside the collection.

### Telemetry

- Emit rate-limited diagnostic events when one of the new runtime guards is
  used. Events identify the guard reason, affected collection, and general
  object type. The content-unload event includes the internal asset name only
  when that name is available. Telemetry remains subject to the existing
  usage-sharing opt-out.

## [0.0.39] - 2026-07-26

### Fixed

- Prevent a defeated enemy from awarding its Challenge score twice. A lethal
  direct-damage result could award score in
  `StatisticsManager.InternalDamageEvent`, followed by a second award from the
  normal death processing in `StatisticsManager.AddKillEvent`; delayed status
  kills often reached only one of these paths.
- Route both existing `SurvivalRuleset.AddScore` patch sites through a shared
  per-enemy-life credit guard on `NonPlayerCharacter`. The guard resets in
  `NonPlayerCharacter.Initialize` when a pooled enemy is reused and does not
  depend on entity deactivation. Existing damage, death, and statistics
  processing remains unchanged.

## [0.0.38] - 2026-07-26

### Fixed

- Continue Kahn's defeat sequence when the battlefield kill plane terminates
  him before his scripted animation can start the death dialog. The executable
  now detects only this `#boss_n06` termination state and invokes the level's
  existing dialog trigger without modifying the level XML or its multiplayer
  content hash.

### Telemetry

- Emit `magicka_patch_khan_killplane_fallback` when the targeted Kahn fallback
  actually runs. The event follows the existing usage-sharing opt-out and
  contains no additional gameplay or player fields.

## [0.0.37] - 2026-07-26

### Fixed

- Clear `GiveOrder.sPlayState` in `GiveOrder.DisposeCache()` so the old static
  game state no longer keeps the scene alive across unloads.
- Add `TextBox.ReleaseLevelReferences()`, add
  `DialogManager.ResetForLevelUnload()`, and call the reset from
  `PlayState.Dispose()` so dialog and textbox references are released during
  level unload.
- Add dispose cleanup for `Entity`, `Character`, `NonPlayerCharacter`, and
  `Avatar`, plus `Avatar.ClearCache()`, to break lingering object references
  during unload.

## [0.0.36] - 2026-07-24

### Fixed

- Cache PolygonHead's implicit default `DepthStencilBuffer` wrapper for the
  lifetime of its `GraphicsDevice`, refresh it only after a successful device
  reset, and reuse it from both `RenderManager.RenderScene` and Magicka's sway
  render path. This removes the continuous XNA 3.1
  `GC.ReRegisterForFinalize` path caused by requesting the same native wrapper
  every frame.
- Cache the seven stable `RenderManager` render-target texture wrappers
  alongside their owning `RenderTarget2D` objects. The caches are cleared
  before the old targets are disposed and populated lazily at each target's
  first original, valid `GetTexture()` use after resolve/unbind. This avoids
  both invalid eager retrieval and repeated per-frame wrapper retrievals
  without extending resource lifetimes.
- Subscribe Magicka's graphics reset and device-settings handlers before the
  constructor's initial `ApplyChanges()` call, and request
  `RenderTargetUsage.PreserveContents` for the back buffer during device
  preparation.
- Explicitly clear the preserved implicit back buffer before scene pre-render
  work, including depth and stencil when supported, then retain the existing
  post-pre-render depth-buffer restoration required after Tome shadow
  rendering.
- Replace Tome shadow rendering's `Clear(Color)` overload with an explicit
  target-and-depth clear so XNA does not need to infer the active depth-buffer
  clear behavior.

## [0.0.35] - 2026-07-20

### Fixed

- Re-enable the standard HUD whenever a new gameplay session is initialized.
  This restores charged-element indicators in multiplayer Challenge mode after
  leaving one session and loading another map without restarting the game.

## [0.0.34] - 2026-07-20

### Fixed

- Restore Grease's per-cast removal path so every completed cast stops its
  spray effect and audio cue before returning the reusable ability instance to
  the cache. This prevents grease visuals from remaining active after later
  casts reuse an instance that was previously disposed.

## [0.0.33] - 2026-07-18

### Fixed

- Clear active spell and ability effects before disposing gameplay entities,
  spell-effect caches, and magick singletons when leaving a level. This
  prevents a LoaderThread crash when `SummonDeath.OnRemove()` tries to kill its
  summoned entity after `SummonDeath.Dispose()` has already cleared it.

## [0.0.32] - 2026-07-18

### Added

- Enable the Magicka 2-style XInput gameplay scheme by default in the normal
  Community Patch packages. Magicka's original controller scheme remains
  available with `use_magicka_1_controller_scheme=true` in
  `patch-settings.ini`.
- Let a session with exactly one local player move seamlessly between XInput
  and mouse/keyboard during gameplay. Fresh input takes ownership, while
  remote network players do not block the handoff.
- Preserve local co-op input ownership: when two or more local players are
  connected, the devices assigned in character selection remain fixed and the
  automatic handoff is disabled.
- Reuse the existing bottom-left `KeyboardHUD` for XInput. Controller mode
  arranges the eight existing element icons as four rectangular pairs
  matching the Magicka 2 face-button layout.
- Add aggregate per-session element-selection telemetry to the existing normal
  shutdown and crash events. Separate keyboard/mouse and controller counters
  plus the controller share are emitted as JSON numbers; individual elements,
  their order, and player identities are not recorded by these counters.

### Fixed

- Prevent the installer subtitle from overlapping its status line when the
  header uses wider font metrics or Windows display scaling.
- Remove the release-candidate controller overlay that kept the world-space
  `SpellWheel` expanded above the character. The original `SpellWheel` methods
  are unchanged in the corrected executable.
- Re-evaluate the HUD owner after character selection assigns a player, so the
  first gameplay frame already uses the controller layout without requiring a
  second button press.
- Stop A/B/X/Y element presses from also firing Magicka's legacy gameplay
  actions. An unused LB/L1 tap remains the dedicated interaction, dialog
  advance, and cutscene-skip input; LB/L1 combinations do not trigger it, even
  when the face button was already held before LB/L1. Persistent popup controls
  keep their original A/B/X/Y menu bindings.
- Keep `ParadoxAccountSaveData` alive after leaving the menu while global
  Paradox account requests may still complete. It is now destroyed only during
  application shutdown, after the logic thread has stopped and
  `ParadoxServices` has been disposed, preventing delayed startup callbacks
  from dereferencing a missing scoped singleton.
- Trigger the existing 0.125-second bottom-left element-icon flash for the
  Magicka 2-style controller buttons. The controller path notifies the HUD
  directly, avoiding a new global element cooldown that could interfere with
  mixed keyboard/controller local co-op.

### Changed

- Promote the formerly separate 0.0.31 controller preview into the regular
  installer and files-only packages; separate preview assets are no longer
  needed.
- Preserve `use_magicka_1_controller_scheme` when the installer or auto-updater
  rewrites `CommunityPatch\patch-settings.ini`.
- Neutralize held movement, casting, attacking, special-action, and blocking
  state before an input-device handoff so releases cannot remain stuck on the
  detached controller.
- Ignore takeover attempts while gameplay input is limited or player-locked,
  while the previous controller is temporarily inverted, or when the game is
  unfocused.
- Keep DirectInput controllers on Magicka's legacy assignment path; only
  XInput and mouse/keyboard participate in automatic ownership handoff.

### Affected files and executable symbols

- `Magicka.exe`: new
  `Magicka.CommunityPatch.HybridInputSupport`; existing
  `Magicka.CommunityPatch.Magicka2ControllerSupport` and controller-aware
  `PatchSettings` promoted to the normal release.
- `Magicka.exe`: `Magicka.GameLogic.Controls.ControlManager.HandleInput`,
  `Magicka.GameLogic.Controls.XInputController.GetBoundValuePressed`,
  `Magicka.GameLogic.UI.KeyboardHUD.Update`,
  `KeyboardHUD.UpdateControls`, and
  `KeyboardHUD.RenderData.Draw`/`DrawIcon`. `SpellWheel` is no longer patched.
- `Magicka.exe`: `Magicka.GameLogic.GameStates.MenuState.OnExit` and
  `Magicka.Game.EndRun` move `ParadoxAccountSaveData` cleanup from menu exit to
  process shutdown.
- Controller-preview patch sites retained in the normal executable:
  `Magicka.GameLogic.Controls.XInputController.Update` and
  `Magicka.GameLogic.Entities.Avatar.CommunityPatchClearSpellQueue`.
- Documented injected source snapshots:
  `docs/injected-source/Magicka.CommunityPatch/HybridInputSupport.cs`,
  `Magicka2ControllerSupport.cs`, and `PatchSettings.cs`.
- Settings and package documentation:
  `release-package/patch-settings.ini`, `release-package/README.txt`, root
  `README.md`, and the Flutter installer/updater settings writer.

## [0.0.31] - 2026-07-17

### Fixed

- Prevent rare crashes while leaving or paging away from character selection by
  retaining its textures until neither the live menu nor cached render data can
  still draw them, synchronizing texture unloading with the render thread, and
  skipping avatar drawing when a required texture is unavailable.
- Defer client boss activation until its network initialization has completed,
  including The Machine's network-provided warlock, instead of exposing a
  partially initialized boss to the normal update path.
- Prevent multiplayer packets from dereferencing stale entity handles by
  rejecting missing, disposed, or play-state-detached entities at all 21 client
  and 17 server lookup sites. Damage targets are also rejected after their
  physics body has already been torn down.
- Validate cached character templates before applying network character and
  player-spawn messages. Missing templates now drop the packet before it can
  create a template-less character or partially mutate a player's equipment and
  cached avatar.
- Drop delayed `SpawnNPC` WorldSync messages unless their handle resolves to a
  live non-player character belonging to the receiving play state.
- Treat failed cached-avatar lookups as failed versus revives instead of
  dereferencing a missing or wrong-type entity.
- Ignore input-lock operations for controllers whose player has already been
  detached during dialogue or disconnect teardown.
- Keep missing level-name data in the optional gameplay telemetry block from
  preventing an already requested transition into gameplay.
- Rate-limit every stable network-guard reason independently with bounded
  exponential backoff, preserving useful diagnostics without flooding
  telemetry during repeated packet drops.
- Prevent the rare main-menu `PolygonHead.Text.Append` array overflow when a
  modified or corrupt core-file installation adds the ` (Modified)` suffix to
  the Community Patch version text.
- Resolve `steam_api.dll` relative to the executable and fail with a guided
  startup dialog when it cannot be opened; also reject trailing
  `+connect_lobby`, `+connect`, and `+password` options that have no value.
- Keep the menu usable without legacy Managed DirectInput. Controller scans and
  controller-list refreshes are disabled after the first assembly-load failure,
  and one in-game dialog explains that controllers remain unavailable until the
  bundled DirectX redistributable is installed.
- Clear stale authentication-detail text before showing Community Patch dialogs
  so a previous Paradox error code cannot leak into a DirectInput message.

### Optional controller preview

- Publish separate, freely available experimental installer and files-only
  packages containing the work-in-progress Magicka 2-style XInput gameplay
  scheme. The regular 0.0.31 packages remain fixes-only.
- Map movement to the left stick, aiming/forward casting to the right stick,
  area casting to LT/L2, self casting or the staff ability to RB/R1, weapon
  imbue or melee to RT/R2, and the two element groups to the face buttons with
  LB/L1 as the modifier.
- Let an unused LB/L1 tap interact, retain Back/View inventory and D-pad Magick
  selection, and let R3 clear queued elements in offline games. Multiplayer
  queue clearing remains disabled because Magicka 1 has no compatible network
  action for removing an already replicated sequence.
- Keep Magicka's original controller scheme available through
  `use_magicka_1_controller_scheme=true` in `patch-settings.ini`.

### Affected files and executable symbols

- `Magicka.exe` - character-selection lifetime:
  `Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect.<OnUnload>b__0_0`,
  `SubMenuCharacterSelect.DrawAvatar`,
  `Magicka.GameLogic.UI.Tome.IsMenuReferencedByRenderPipeline`, and
  `Tome.CanStateDrawOldMenu`.
- `Magicka.exe` - deferred boss initialization:
  `Magicka.GameLogic.Entities.Bosses.BossFight.Initialize`, `Start`, `Update`,
  `NetworkInitialize`, `Clear`, `Reset`, `QueuePendingBossInitialization`,
  `RemovePendingBossInitialization`, `TryCompletePendingBossInitialization`,
  `RetryPendingBossInitializations`, and
  `Magicka.GameLogic.Entities.Bosses.Machine.NetworkInitialize`.
- `Magicka.exe` - network entity and template validation:
  `Magicka.Network.NetworkClient.ReadMessage`,
  `NetworkClient.TryProcessSpawnMissileMessage`,
  `Magicka.Network.NetworkServer.ReadMessage`,
  `NetworkServer.TryProcessSpawnMissileMessage`,
  `Magicka.GameLogic.GameStates.PlayState.AddWorldSyncMessage`,
  `Magicka.GameLogic.Entities.Character.ReApplyTemplate`,
  `Character.TryReApplyCachedTemplate`, and
  `Magicka.CommunityPatch.NetworkEntityHandleGuard`.
- `Magicka.exe` - avatar, input, and gameplay-start guards:
  `Magicka.GameLogic.Entities.Avatar.GetFromCache(Player, ushort)`,
  `Magicka.Levels.Versus.VersusRuleset.RevivePlayer`, the `Controller`
  overloads of `Magicka.GameLogic.Controls.ControlManager.LockPlayerInput`,
  `IsPlayerInputLocked`, and `UnlockPlayerInput`, and
  `SubMenuCharacterSelect.Start`.
- `Magicka.exe` - startup, menu, and DirectInput guards:
  `Magicka.Program.Main`, `Magicka.GameLogic.UI.Tome..ctor`,
  `Tome.ControllerMouseAction`, `Magicka.GameLogic.GameStates.Menu.MenuState`,
  the `SubMenuOptionsControls.UpdateControllers` call sites,
  `Magicka.WebTools.Paradox.ParadoxPopupUtils.ShowErrorPopup(string, string)`,
  and `Magicka.CommunityPatch.RuntimeCompatibilityGuards`.
- Documented injected helper sources:
  `docs/injected-source/Magicka.CommunityPatch/NetworkEntityHandleGuard.cs`,
  `docs/injected-source/Magicka.CommunityPatch/NetworkGuardTelemetryBackoff.cs`,
  and
  `docs/injected-source/Magicka.CommunityPatch/RuntimeCompatibilityGuards.cs`.
- Controller-preview `Magicka.exe` only:
  `Magicka.CommunityPatch.Magicka2ControllerSupport`,
  `Magicka.CommunityPatch.PatchSettings`,
  `Magicka.GameLogic.Controls.XInputController.Update`,
  `XInputController.GetBoundValuePressed`, and
  `Magicka.GameLogic.Entities.Avatar.CommunityPatchClearSpellQueue`.

## [0.0.30] - 2026-07-14

### Fixed

- Prevent `DeviceLostException` crashes when Magicka is minimized or loses
  focus during a loading screen. Logical fullscreen now keeps XNA's
  monitor-sized borderless window while the underlying Direct3D 9 device uses
  non-exclusive presentation, so loading can continue creating textures,
  shaders, and other graphics resources after Alt-Tab.
- Keep Alt-Tab target windows visible by disabling XNA's fullscreen `TopMost`
  state whenever Magicka is using the new non-exclusive fullscreen
  presentation.

### Changed

- Fullscreen now operates as borderless windowed presentation internally. The
  existing fullscreen setting and selected render resolution remain in use,
  while presentation follows the desktop refresh mode and switching to other
  applications no longer requires an exclusive-device transition.

## [0.0.29] - 2026-07-14

### Fixed

- Prevent stale mouse-selected interactables from crashing after scene teardown
  by clearing transient keyboard/mouse interaction targets when a scene is
  destroyed and skipping highlight work once its scene or level model is no
  longer available.
- Make scene triggers participate in their existing final-disposal cleanup by
  implementing `IDisposable`.
- Preserve `Agent.mOwner` across `Agent.Reset()` so active AI code such as
  `AddAttackedBy()` cannot encounter the null owner that previously caused
  multiplayer crashes. Reset now releases the leader and its age, last target,
  last spell ability, scripted events and their cursor, delay and loop state,
  and both fuzzy-sort scratch arrays in addition to its existing target,
  ability, priority-target, path, state, and attacked-by cleanup.
- Make `Agent.Disable()` always remove the agent from `AIManager` and clear the
  reusable entity and ability scoring arrays that could otherwise retain up to
  eight entities and eight abilities from an old scene. If the owner is dead,
  also release its target stack and ages, next, busy and last spell abilities,
  priority target, last target, leader, and leader age. Living NPCs temporarily
  cached for a later scene revisit deliberately retain their gameplay state.
- Clear both fuzzy-sort scratch arrays after `Agent.ChooseTarget()` has copied
  its selected outputs, preventing completed target selections from retaining
  unrelated scene entities or abilities.
- Change `NonPlayerCharacter.Deinitialize()` to disable its agent before base
  entity teardown and reset it after equipment deinitialization. This removes
  external AI-manager roots and stale outbound references at the correct
  lifecycle boundaries without weakening the NPC/Agent owner relationship.

### Changed

- Rate-limit repeated `entity_update_ignored_not_ready` and
  `entity_update_unknown_handle` telemetry with independent, bounded
  exponential backoff, preventing thousands of duplicate reports while
  retaining diagnostics.

## [0.0.28] - 2026-07-13

### Fixed

- Restore the original delayed cleanup in `PlayState.OnExit()` so multiplayer
  level transitions no longer wait on remote player states or race with the
  next `PlayState`, which could leave player characters without working
  movement and action updates in the following level.

## [0.0.27] - 2026-07-12

### Fixed

- Restore the original `Entity.GetFromHandle()` lookup behavior so registered
  entity handles are not rejected solely because the entity is marked as
  disposed during multiplayer play-state transitions.

## [0.0.26] - 2026-07-12

### Fixed

- Skip disposed audio cues in `AudioManager.StopAll()` instead of calling
  `Cue.Stop()` on them and triggering an `ArgumentException`.

### Added

- Log client-side network-guard diagnostics when an `EntityUpdate` packet is
  ignored because the client is not ready or because its entity handle is
  unknown, including play-state and join context for investigating multiplayer
  failures between levels.

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
