# Runtime patcher migration experiment

This project migrates the manually edited Community Patch assembly to a small
CLR-2-compatible Harmony runtime patcher. It currently implements and verifies
ninety-one method patches:

- `Avatar.FindInteractable` returns no interaction while its play state or scene
  is detached.
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
- `EntityStateStorage` releases its constructor play-state reference and
  restores saved entities into the current play state.
- `Helper.ArrayEquals` treats every missing byte array as unequal.
- `InventoryBox.RenderData.Draw` updates `TextBoxEffect.ScreenSize` before the
  original method runs.
- `MagickCamera.Update` releases a followed entity whose physics body has
  detached.
- `BossHealthBar` no longer retains the scene supplied to its constructor or
  setter, and its scene getter resolves the current play-state scene.
- `HUDManager.Initialise` re-enables the original HUD after state transitions.
- `Machine.NetworkInitialize` marks the boss as initialized only when its
  referenced warlock entity exists.
- `Jormungandr.UndergroundState.OnUpdate` waits for a live target before
  beginning its emergence sequence.
- `PlayState.AddWorldSyncMessage` rejects unusable SpawnNPC handles before the
  original enqueue method runs.
- `Portal.PortalEntity.Update` skips queued entities that are null or whose
  physics body has already been detached.
- `VersusRuleset.RevivePlayer` returns handle zero when the requested avatar
  cannot be obtained from the cache.
- `ItemPack` and `MagickPack` apply the Community Patch custom-content license
  policy in both their license and enabled setters.
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
- `EntityUpdate` packets carrying the payloadless Character feature marker are
  decoded without aborting the remaining update fields.
- Loading-screen clears restore the managed depth buffer before drawing.
- Controller option discovery tolerates unavailable DirectInput and reports the
  problem after the main menu is ready.
- Image-menu text refreshes its font metrics after a language change, and reused
  Paradox popups clear stale detail text.
- Simplified Chinese uses a fixed Latin display name and accepts the supported
  configuration aliases without changing existing language lookup behavior.
- Dialogue lists and structured element hints restore the line breaks that are
  lost by the original localization layout path.
- `PlayState.Dispose` releases `ShadowBlobs` from the scene being torn down
  without clearing a replacement scene that is already active.

The Avatar, AI, BossHealthBar, Helper, InventoryBox, MagickCamera, and PlayState
changes use Harmony prefixes; BossHealthBar additionally uses a constructor
postfix, while HUDManager and one EntityManager change use ordinary postfixes.
The Agent, AudioManager, ChillyBlast, CompanyState, DeflectionAura, DrainLife, DrinkBlood,
EntityStateStorage, Flash, Machine, Jormungandr, pack, PoisonSpray, Portal,
RandomMine, SpawnSlime, SummonCross, SummonFlamer, SummonSpirit, SummonUndead,
Starfall, VersusRuleset, DialogLayout, ShadowBlobs, and
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
EntityStateStorage, Helper, InventoryBox, ItemPack, Jormungandr, MagickCamera,
MagickPack, Machine, PoisonSpray, Portal, RandomMine, SpawnSlime, SummonCross,
SummonFlamer, SummonSpirit, SummonUndead, Starfall, and VersusRuleset targets and
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
