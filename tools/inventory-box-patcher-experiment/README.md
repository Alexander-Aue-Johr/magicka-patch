# Runtime patcher migration experiment

This project migrates the manually edited Community Patch assembly to a small
CLR-2-compatible Harmony runtime patcher. It currently implements and verifies
three hundred eighty-four method patches:

- `Avatar.FindInteractable` returns no interaction while its play state or scene
  is detached.
- `Avatar.NetworkAction` drops late pickup actions after the referenced item has
  already lost its physics body.
- `AIStateAttack.OnExecute` releases a target whose physics body has already
  been detached.
- `AIStateMove.OnEnter` omits the target-relative waypoint when that target's
  physics body has already been detached.
- `AIStateMove.OnExecute` leaves the move state before reading a detached
  target's position.
- `Agent.ChooseTarget` excludes candidates whose physics body has already
  detached.
- `EntityManager.GetClosestIDamageable` skips candidates whose physics body has
  already been detached.
- `EntityManager.GetEntities` skips null and bodyless spatial entries.
- `EntityManager.ClearAndStore` rebuilds the QuadGrid immediately after scene
  teardown.
- `PhysicsEntity` detaches replaceable JigLibX bodies and collision skins when
  it deinitializes and before reuse. Final entity-handle cleanup also releases
  body tags, skin tags, owners, collision lists, both callback delegates, and
  stale play-state references for every registered entity.
- `TypingText.Update` finishes malformed or truncated text after an
  out-of-range parser read instead of aborting the update path.
- Every closed `NetworkServer.QueueUDPMessage<T>` instantiation drops a late
  reply after its client index has been removed. The check runs inside the
  existing client-list lock.
- `NetworkServer.ReadMessage` drops `EnterSync` packets whose sender is no
  longer present while preserving the valid sync-point update.
- `NetworkClient.ReadMessage` drops a late `RulesetUpdate` after its play-state
  scene chain has detached.
- `NetworkClient.ReadMessage` accepts world-spawn `TriggerAction` messages only
  from the connected server while preserving peer-authoritative action types.
- `Trigger.NetworkAction` rejects missing, disposed, foreign-state and
  wrong-type entity handles while preserving the original active-slot reuse
  rules for items, elementals, grease, tornadoes and dead NPCs.
- `MissileEntity.NetworkEventMessage` drops events for incomplete missiles and
  collision or hit events whose required target has already disappeared. A
  consumed collision missile is killed locally so ordinary cleanup removes it.
- Every closed `NetworkServer.SendMessage<T>` broadcast continues with the next
  client after queueing a cacheable packet for a syncing player.
- Forced player-status requests resolve a Player ID owned by the packet sender,
  and their responses include every network player with an active Avatar.
- `EntityStateStorage` releases its constructor play-state reference and
  restores saved entities into the current play state.
- `Helper.ArrayEquals` treats every missing byte array as unequal.
- `InventoryBox.RenderData.Draw` updates `TextBoxEffect.ScreenSize` before the
  original method runs.
- The classic keyboard HUD and right-aligned tutorial prompts stay within a
  centred 16:9 safe area on ultrawide displays while 16:9 layout is unchanged.
- `TutorialManager` no longer retains the last disposed play state and resolves
  its active level state only when updating tutorial content.
- `EtherealClone` no longer retains its last play state and resolves the current
  NavMesh when placing a clone.
- `BreakBarriers` no longer retains its activation play state and uses the
  current entity manager for both sides of its spatial query.
- Cached `TeslaField` instances no longer retain the play state supplied when
  their pool entries were constructed.
- `GenericHealthBar.Update` submits its render data to the current scene after
  an in-level scene transition.
- `GreaseTrail` no longer retains its activation play state and uses the
  current play state when creating and registering grease fields.
- `SpellEffect` no longer retains the level state used to initialize its
  caches, and `LightningSpell` resolves cache and cast work through the current
  play state.
- `EffectManager` keeps the first visual-effect definition when multiple XML
  files produce the same filename hash instead of aborting initialization.
- `TimeWarp` and `TimeWarpStaff` release the play state supplied when the
  effect starts and apply timing and saturation changes to the current play
  state.
- `SpellWheel` no longer retains the state supplied during initialization and
  submits its GUI render data to the current scene.
- `EarthQuake` no longer retains its activation play state and resolves scene
  intersections, camera shake and entity queries through the current state.
- `ArrowRain` no longer retains its activation state or scene and resolves
  missile, lightning, camera and removal work through the current state.
- `HealingRain` resolves active work through the current state and releases its
  scene and caster after stopping its audio and visual effect.
- The in-game menu no longer retains the PlayState supplied during
  initialization. Menu actions and rendering resolve the current state when
  used; the larger menu methods are patched only after menu initialization so
  their JIT cannot run before `Game.Instance` exists.
- `PlayState.Dispose` clears the static in-game menu stack after the existing
  boss-fight cleanup, releasing menu state owned by the level being removed.
- Changing language while no magick is marked clears the description instead
  of indexing the descriptions array with an invalid selection.
- `MagickCamera.Update` releases a followed entity whose physics body has
  detached.
- `BossHealthBar` no longer retains the scene supplied to its constructor or
  setter, and its scene getter resolves the current play-state scene.
- `HUDManager.Initialise` re-enables the original HUD after state transitions.
- `Machine.NetworkInitialize` marks the boss as initialized only when its
  referenced warlock entity exists.
- `Jormungandr.UndergroundState.OnUpdate` waits for a live target before
  beginning its emergence sequence.
- `GiveOrder.Exec` invokes Kahn's existing defeat trigger when the battlefield
  kill plane terminates him before his scripted animation event can run.
- Challenge scoring credits each non-player character once per pooled life,
  whether lethal damage reaches the direct-damage path, the kill-event path,
  or both.
- `PlayState.AddWorldSyncMessage` rejects unusable SpawnNPC handles before the
  original enqueue method runs.
- `Portal.PortalEntity.Update` skips queued entities that are null or whose
  physics body has already been detached.
- `VersusRuleset.RevivePlayer` returns handle zero when the requested avatar
  cannot be obtained from the cache.
- `ItemPack` and `MagickPack` apply the Community Patch custom-content license
  policy in both their license and enabled setters. The character-select pack
  list uses the same policy for thumbnails and unused-pack markers.
- Character selection skips only image widgets whose texture is missing or
  already disposed, before changing either GUI effect state.
- Scene audio removes one invalid XACT locator after an internal cue-index
  failure and continues updating the remaining locators.
- Every `StaticList` and `StaticWeakList` insertion path used by the game grows
  full backing arrays under a stable CLR-2-compatible instance lock.
- Railgun intersections reject indirect ancestors before linking them as
  children, and the defensive lock traversal terminates on an existing cycle.
- Missing animation clips no longer abort content loading. Invalid animation
  slots are left empty, and character animation paths fall back to idle only
  when that clip is available.
- `RadialBlur` uses the process-wide content manager, does not retain the scene
  supplied during initialization, renders into the current scene, and releases
  disposed cache entries.
- `SummonPhoenix` no longer retains the level that started it and resolves all
  later scene, camera, damage, navigation and revive work through the current
  play state.
- `Vlad` no longer retains its construction play state and uses the current
  state for collision placement and entity registration.
- `Napalm` no longer retains its casting play state and resolves collision,
  liquid, camera, damage, lighting and rendering work through the current
  state.
- `Thunderbolt` no longer retains either supplied casting play state and
  resolves all later scene, entity, camera, damage, effect and achievement
  work through the current state.
- `Entanglement` reuses the process-wide registered render effect instead of
  allocating and retaining another XNA effect for every entangled character.
- `ConfuseWho` restores an expired victim from its current faction instead of
  dereferencing a character template that may already be detached.
- Level-action cleanup now detaches scene and trigger references immediately,
  clears restored action tags, and also runs when an initialized play state is
  disposed.
- Play-state disposal fully resets the process-wide dialog manager and releases
  scene and owner references from its fixed, cutscene, and additional text boxes.
- `DrinkBlood.Execute` no longer stores an unused strong reference to the play
  state that created the effect.
- `RandomMine.Execute` no longer stores the last play state on its process-wide
  singleton.
- `Starfall` releases its static legacy play-state reference and processes
  queued strikes against the current play state.
- `DrainLife.Execute` no longer stores an unused strong reference to the play
  state after a successful target selection.
- Gamepad Back/B opens the existing exit confirmation directly from the main
  menu while keyboard and mouse keep the original cursor behavior.
- `CompanyState.OnExit` clears controller and Tome state before disposing the
  company-screen content manager.
- Player-input lock operations ignore a controller that has already detached
  from its player; valid controllers retain the original lock behavior.
- `Interactable.Highlight` omits a highlight after its scene or level model has
  already been detached.
- `AudioManager.StopAll` skips XNA cues that have already been disposed.
- `DeflectionAura.Execute` no longer stores an unused strong reference to its
  creating play state.
- `Flash.Execute` no longer retains the scene that triggered the singleton,
  and `Flash.Update` submits rendering to the current play-state scene.
- `SpawnSlime` and `SpawnSlimeOverkill` no longer retain their last play state;
  both slime-spawn helpers resolve the current play state's NavMesh when used.
- `PoisonSpray` no longer retains its creating play state and borrows its
  temporary entity-query list from the current play state.
- `ChillyBlast` no longer retains its creating play state and borrows its
  temporary entity-query list from the current play state.
- `SummonFlamer` and `SummonSpirit` no longer retain their supplied play states,
  resolve the active play state while spawning, and release their level-loaded
  templates during `PlayState.Dispose`.
- `SummonUndead` no longer retains its supplied play state, resolves the active
  play state while spawning, and releases its level-loaded template array
  during `PlayState.Dispose`.
- Necromancer-staff summons carry their undead state in an existing serialized
  zero-valued field without changing the `SpawnNPC` packet length. Patched
  clients restore the flag; ordinary NPC spawns remain unchanged.
- `SummonZombie` no longer retains its supplied play state and resolves the
  active play state while starting and spawning zombies.
- `SummonCross` no longer retains its supplied play state, resolves the active
  play state while spawning, and releases its pooled instances and template
  during `PlayState.Dispose`.
- The remaining level-owned summon templates, charge-ability pools, and active
  Haste and Shrink pools are released during `PlayState.Dispose`.
- Active chant spells run their existing stop lifecycle during level disposal.
- Static ability, spell, spell-effect, and lightweight entity pools are emptied
  after the original entity-manager cleanup during level disposal.
- The same cleanup releases the static `GiveOrder` action list and its owning
  play state instead of retaining the completed level until another order loads.
- `JudgementSpray.SpawnProjectile` allocates a replacement condition collection
  when the shared pool is temporarily empty and otherwise reuses the original
  cached object.
- `Blizzard` no longer retains its supplied play state, resolves scene, camera,
  entity, liquid, and post-effect work through the current play state, and
  releases its scene, caster, and ambience cue before the existing cue-stop
  operation can fail.
- `Rain` and `Thunderstorm` no longer retain their supplied play states and
  resolve weather work through the current play state. Rain releases its scene
  and caster on removal; Thunderstorm releases its per-cast owner after the
  active ambience cue stops.
- `AnimatedLevelPart.Update` removes collision registrations whose entity or
  physics body has already been detached.
- `DynamicLight.DisposeCache` releases each cached shadow map and then drops
  the static light-cache entries for the unloaded level.
- `MeteorShower` no longer retains its supplied play state, resolves scene and
  missile work through the current play state, and releases its scene, owner,
  and rumble cue before the existing cue-stop operation can fail.
- `EntityUpdate` packets carrying the payloadless Character feature marker are
  decoded without aborting the remaining update fields.
- Loading-screen clears restore the managed depth buffer before drawing.
- Controller option discovery tolerates unavailable DirectInput and reports the
  problem after the main menu is ready.
- Paradox account save data remains available to delayed account callbacks
  after leaving the menu and is released once during final game shutdown.
- Image-menu text refreshes its font metrics after a language change, and reused
  Paradox popups clear stale detail text.
- Simplified Chinese uses a fixed Latin display name and accepts the supported
  configuration aliases without changing existing language lookup behavior.
- Dialogue lists and structured element hints restore the line breaks that are
  lost by the original localization layout path.
- `PlayState.Dispose` releases `ShadowBlobs` from the scene being torn down
  without clearing a replacement scene that is already active.
- Clearing `Player.Avatar` releases the matching strong controller reference
  without erasing a newer controller assignment.
- `Player.DeinitializeGame` releases the obtained text box's level-scoped
  owner and scene references while retaining the reusable text-box object.
- The same teardown independently clears the notifier's owner, attached text
  box, and visible alpha state while retaining its reusable graphics objects.

The Avatar, AI, Blizzard, BossHealthBar, character-select widget, Entity,
PhysicsEntity, Helper,
InventoryBox, KeyboardHUD, MagickCamera, MeteorShower, Player, PlayState, and Rain
changes use Harmony prefixes; BossHealthBar additionally uses a constructor
postfix, while HUDManager, NonPlayerCharacter, PhysicsEntity, and one
EntityManager change use ordinary postfixes.
The Agent, AnimatedLevelPart, AudioManager, Blizzard, BreakBarriers, ChillyBlast, CompanyState, DeflectionAura, DrainLife, DrinkBlood, DynamicLight, EtherealClone, GameScene, GenericHealthBar, GreaseTrail, LightningSpell, MeteorShower, Rain, SpellEffect, TeslaField,
EntityStateStorage, Flash, GiveOrder, Machine, Jormungandr, pack, PoisonSpray, Portal,
RandomMine, SpawnSlime, SummonCross, SummonFlamer, SummonSpirit, SummonUndead,
Starfall, StatisticsManager, Thunderstorm, TutorialManager, VersusRuleset,
JudgementSpray, DialogLayout, ShadowBlobs, and
remaining EntityManager changes use narrowly checked transpilers for small
branches inside their original methods. ControlManager, Interactable, and
SubMenuMain use conditional prefixes.
EntityStateStorage also uses a constructor postfix.

## Run

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File `
  .\tools\inventory-box-patcher-experiment\build.ps1 `
  -SkipSourceAnalysis `
  -GameDirectory 'C:\path\to\Magicka' `
  -OutputDirectory ..\..\tmp\runtime-patcher-run
```

Omit `-SkipSourceAnalysis` to decompile the original and manual patch, remove
identical files, and regenerate the complete file-diff inventory.

The build:

1. backs up the original executable;
2. builds the runtime loader, runtime patch, and behavior probe;
3. creates runtime-enabled hosts for Magicka 1.10.4.2, 1.4.16.0, and 1.5.1.0;
4. verifies Harmony registration against the original 1.10.4.2 assembly;
5. runs the behavior matrix in isolated CLR-2 x86 processes;
6. records assembly identities, SHA-256 hashes, scenarios, and results.

The build stops when an input hash differs from
`reference/verified-assemblies.txt`. Updating a manual patch therefore requires
an intentional reference and coverage-checklist update.

The distributable experiment files are written below `runtime/`. Verification
evidence is written below `audit/`, especially `behavior-matrix.txt`.

## Verification model

The same scenarios run against:

- the original `Magicka_orig.exe`, where patch-specific scenarios must fail;
- the manual `Magicka.exe`, where every scenario must pass;
- the original `Magicka_orig.exe` with Harmony patches, where every scenario
  must pass.

Control scenarios must pass in all three profiles. This verifies observable
behavior instead of claiming that a Harmony wrapper has the same C# or IL shape
as a manually rewritten method.

Magicka 1.4.16.0 and 1.5.1.0 contain the Agent, AudioManager, Avatar,
AIStateAttack, DeflectionAura,
AIStateMove, BossHealthBar, CompanyState, ControlManager, DrainLife, DrinkBlood,
EntityManager, Flash, Interactable,
EntityStateStorage, GiveOrder, Helper, InventoryBox, ItemPack, Jormungandr, MagickCamera,
MagickPack, Machine, PoisonSpray, Portal, RandomMine, SpawnSlime, SummonCross,
SummonFlamer, SummonSpirit, SummonUndead, Starfall, StatisticsManager,
NonPlayerCharacter, and VersusRuleset targets and
accept their runtime patches. All
headless-applicable scenarios pass. They contain
neither the later `HUDManager`
implementation nor `WorldSyncMessage` and `PlayState.AddWorldSyncMessage`; both
unavailable patch groups therefore report `NOT_APPLICABLE` without preventing
other patches from loading. They also predate the `SubMenuMain.ControllerB`
override and the later `ChillyBlast` ability, so both patch groups and their
scenarios are `NOT_APPLICABLE` there.

Source comparison stages each executable and its dependency set in isolated
directories. This prevents the executable's original location from changing
ILSpy type resolution or the resulting migration inventory.

See [RUNTIME_PATCHER_REPORT.md](RUNTIME_PATCHER_REPORT.md) for the reading
order, architecture, reference assembly hashes, verification checklist, and
the complete manual-patch source-file migration checklist.
