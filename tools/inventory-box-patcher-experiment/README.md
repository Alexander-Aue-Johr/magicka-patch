# Runtime patcher migration experiment

This project migrates the manually edited Community Patch assembly to a small
CLR-2-compatible Harmony runtime patcher. It currently implements and verifies
twenty changes:

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

The Avatar, AI, BossHealthBar, Helper, InventoryBox, MagickCamera, and PlayState
changes use Harmony prefixes; BossHealthBar additionally uses a constructor
postfix, while HUDManager and one EntityManager change use ordinary postfixes.
The Agent, Machine, Jormungandr, Portal, VersusRuleset, and remaining
EntityManager changes use narrowly checked transpilers for small branches
inside their original methods.

## Run

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File `
  .\tools\inventory-box-patcher-experiment\build.ps1 `
  -SkipSourceAnalysis `
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

Magicka 1.4.16.0 and 1.5.1.0 contain the Agent, Avatar, AIStateAttack,
AIStateMove, BossHealthBar, EntityManager, Helper, InventoryBox, Jormungandr,
MagickCamera, Machine, Portal, and VersusRuleset targets and accept their
runtime patches. All headless-applicable scenarios pass. They contain
neither the later `HUDManager`
implementation nor `WorldSyncMessage` and `PlayState.AddWorldSyncMessage`; both
unavailable patch groups therefore report `NOT_APPLICABLE` without preventing
other patches from loading.

Source comparison stages each executable and its dependency set in isolated
directories. This prevents the executable's original location from changing
ILSpy type resolution or the resulting migration inventory.

See [RUNTIME_PATCHER_REPORT.md](RUNTIME_PATCHER_REPORT.md) for the reading
order, architecture, reference assembly hashes, verification checklist, and
the complete manual-patch source-file migration checklist.
