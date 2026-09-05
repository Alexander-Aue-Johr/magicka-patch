# Runtime patcher migration experiment

This project migrates the manually edited Community Patch assembly to a small
CLR-2-compatible Harmony runtime patcher. It currently implements and verifies
two changes:

- `InventoryBox.RenderData.Draw` updates `TextBoxEffect.ScreenSize` before the
  original method runs.
- `PlayState.AddWorldSyncMessage` rejects unusable SpawnNPC handles before the
  original enqueue method runs.

Both changes use Harmony prefixes because they are shorter and clearer than
their former IL transpilers. Future patches may still use transpilers when a
small insertion inside a large method is easier to express that way.

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

Magicka 1.4.16.0 and 1.5.1.0 contain the InventoryBox target and pass its
runtime scenarios. They do not contain `WorldSyncMessage` or
`PlayState.AddWorldSyncMessage`; the PlayState patch therefore reports
`NOT_APPLICABLE` on those versions without preventing other patches from
loading.

See [RUNTIME_PATCHER_REPORT.md](RUNTIME_PATCHER_REPORT.md) for the reading
order, architecture, reference assembly hashes, verification checklist, and
the complete manual-patch source-file migration checklist.
