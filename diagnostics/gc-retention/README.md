# Magicka GC Retention Diagnostics

This branch contains an opt-in diagnostic build for finding managed objects that
remain reachable after a level or scene has ended. It is deliberately separate
from the normal community-patch release and must not be distributed as a player
release.

## Why this uses two processes

Magicka is a 32-bit CLR 2.0/XNA 3.1 application. The game-side probe therefore
does only lightweight lifetime bookkeeping:

- it registers selected complex objects through weak `GCHandle` values;
- it marks disposed objects as `MustCollect`;
- it initially treats deactivated entities as `MustCollect`, so an unexpected
  inbound root is not hidden;
- it changes an object to `MustDetach` after a proven insertion into a cache or
  pool, or after that exact instance was acquired from a resident/rotating pool
  (including `Avatar.mMissileCache`);
- resident-pool membership is remembered per instance, so an ordinary `Item` or
  `MissileEntity` does not inherit the state merely because it has the same
  type;
- it starts a fresh active lifetime when a cached object is taken out or
  initialized again;
- it removes records automatically after the weak handle loses its target; and
- it publishes immutable TSV manifests under
  `%LOCALAPPDATA%\MagickaCommunityPatch\gc-retention\retention-<pid>-<start>-<sequence>.tsv`.

Each manifest records the live registry version. The analyzer reads that field
again from its frozen process snapshot and rejects a racing/stale manifest.

Set `MAGICKA_GC_DIAGNOSTICS_DIR` to use a different output directory.

It does not add finalizers, retain strong references, continuously force garbage
collections, or walk the managed heap in-process. Those approaches would change
the lifetime behavior being measured and could introduce new stalls or races.

The separate x86 analyzer uses Microsoft's ClrMD library and a Windows process
snapshot. For `MustCollect` objects it reports root-to-object paths similar to
WinDbg/SOS `!gcroot`. For `MustDetach` pool owners it instead walks outgoing field
paths and reports references to retired objects. That distinction is important:
an object in a static pool is expected to stay alive, but fields such as
`target`, cached abilities, AI targets, scenes, or play states must be detached.

## Instrumented lifecycle points

The reproducible patcher instruments:

- the base `Entity` constructor and every matching `Initialize`,
  `Deinitialize`/`DeInitialize`, and `Dispose` body;
- actual cache insertions (`Add`, `Enqueue`, or `Push`) for both entities and
  non-entity holders such as abilities and spell effects;
- same-type static pool sinks, cache-source returns, and reuse entry points, so
  a reused object starts a new active lifetime;
- the eight resident/rotating entity pools found in this game build, tagging
  only the concrete object returned by a resident acquisition;
- both the static `MissileEntity` pool and `Avatar.GetMissileInstance`, whose
  per-avatar queue deliberately keeps rotating missile objects rooted while
  they are in use;
- `PlayState`, `Level`, `GameScene`, `LevelModel`, `CharacterTemplate`, and
  `PhysicsEntityTemplate` construction/disposal;
- the `GameScene` owned by a disposed `PlayState`;
- `PolygonHead.Models.BiTreeModel` construction/disposal; and
- completion of `PlayState.Dispose` as the unload checkpoint.

Retirement is marked at entry to `Deinitialize`/`Dispose`, so a cleanup that
throws cannot silently escape tracking. Where the IL exposes the collection
operation directly, cache ownership is also marked immediately after the
successful `Add`, `Enqueue`, or `Push`, before later sorting or cleanup code.

The patcher does not add a `GC.Collect`; age and generation-2 counters are used
to suppress objects that have not yet had a fair chance to be collected.

The checked-in diagnostic payload is under `payload\`. The normal root
`Magicka.exe` and `PolygonHead.dll` remain the release versions.

## Build

Requirements:

- the .NET 8 SDK;
- the x86 .NET 8 runtime for the analyzer; and
- network access to restore Mono.Cecil, ClrMD, and .NET Framework 3.5 reference
  assemblies on the first build.

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File `
  .\diagnostics\gc-retention\scripts\build.ps1
```

This rebuilds the CLR-2 runtime helper and x86 analyzer, instruments clean copies
of the repository assemblies, validates the payload, and writes:

```text
diagnostics\gc-retention\build\
  game\
    Magicka.exe
    PolygonHead.dll
    Magicka.GcDiagnostics.dll
  analyzer\
    Magicka.GcAnalyzer.dll
    ...runtime dependencies...
```

Pass `-RefreshPayload` to replace the checked-in diagnostic payload after a
successful build.

Refresh is staged, validated, hashed, and swapped as a directory, so an
interrupted copy cannot leave a silently mixed three-assembly payload.

## Install

Back up the current game files and copy the three diagnostic game assemblies:

```powershell
powershell -ExecutionPolicy Bypass -File `
  .\diagnostics\gc-retention\scripts\install.ps1 `
  -MagickaDir 'D:\SteamLibrary\steamapps\common\Magicka'
```

The script requires the exact Magicka directory, creates a timestamped backup
inside `CommunityPatch\gc-retention-backups`, verifies the payload SHA-256
manifest, and installs only the three game-side files.

Restore the newest pre-diagnostic state (including removal of the helper DLL
when it did not exist before installation) with:

```powershell
powershell -ExecutionPolicy Bypass -File `
  .\diagnostics\gc-retention\scripts\uninstall.ps1 `
  -MagickaDir 'D:\SteamLibrary\steamapps\common\Magicka'
```

Pass `-BackupDir` to select an older backup explicitly. Steam file validation
is still a fallback.

Set `MAGICKA_GC_DIAGNOSTICS=0` before starting the game to disable the probe
without replacing the assemblies.

## Analyze

Play a level, return fully to the main menu, and wait at least ten seconds. The
probe itself never forces a collection.

Then run:

```powershell
powershell -ExecutionPolicy Bypass -File `
  .\diagnostics\gc-retention\scripts\analyze.ps1
```

The script searches the environment override and the default local-app-data
directory, verifies the PID, process start time, and executable path, then
chooses the newest manifest for a still-running process. `-Manifest` can select
a specific file; `-MagickaDir` additionally searches the legacy game-local
directory. The analyzer revalidates the manifest before and after taking its
snapshot and verifies the weak-handle kind and target type. A timestamped
`retention-analysis-*.txt` report is written next to the manifest.

Useful analyzer limits can be forwarded with `-AnalyzerArguments`, for example:

```powershell
-AnalyzerArguments @('--timeout', '90', '--max-root-paths', '5',
  '--detach-depth', '8')
```

By default, `MustCollect` candidates must be at least five seconds old.
`MustDetach` candidates must additionally have survived a later generation-2
collection. `--include-young` bypasses those guards and is useful only for an
immediate diagnostic snapshot.

Treat stack-only root paths as potentially transient and repeat the analysis at
a later checkpoint. Static fields, pools, delegates, timers, render queues, and
paths through cached missiles/abilities/AI state are the high-value findings.
`TRUNCATED` lines are explicit indications that a global time, node, depth, or
path budget was exhausted; they are not clean results.

Likewise, an unresolved, reused, or type-mismatched weak handle leaves the
valid findings in the report but marks the run `INCOMPLETE` and returns a
nonzero exit code instead of silently presenting a partial result as clean.

## Safety and limitations

- A branch of this public GitHub repository is public, and GitHub does not
  permit changing a fork's visibility. Keeping this work private therefore
  requires an independent private repository rather than a fork; this branch
  must never be merged into or released from `main`.
- ClrMD and its DAC must match the target architecture, so the analyzer refuses
  to run as x64.
- Each manifest belongs to exactly one process start. Old manifests and reused
  weak-handle slots are rejected instead of being silently attributed to the
  current game.
- The analyzer uses snapshot-local addresses. Never compare an address from one
  report with a later snapshot as though it were stable.
- Full dumps are not captured automatically because they are large and may
  contain sensitive process data.
- This instrumentation has diagnostic overhead and is not intended for normal
  players or release builds.
- Automated integration tests exercise the registry on x86 CLR 4 and ClrMD
  analysis on x86 CoreCLR. The patcher and payload validator assert CLR-2
  metadata and signatures, but there is not yet an automated live CLR-2 attach
  test. Validate the analyzer against a running Magicka process before treating
  a clean report as conclusive.

## Technical references

- [ClrMD getting started](https://github.com/microsoft/clrmd/blob/main/doc/GettingStarted.md)
- [ClrMD `GCRoot`](https://github.com/microsoft/clrmd/blob/main/src/Microsoft.Diagnostics.Runtime/GCRoot.cs)
- [.NET weak references](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/weak-references)
- [`GCHandleType`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.gchandletype)
- [Induced garbage collections](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/induced)
- [`Object.Finalize`](https://learn.microsoft.com/en-us/dotnet/api/system.object.finalize)
- [GitHub fork visibility](https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/working-with-forks/about-permissions-and-visibility-of-forks)
