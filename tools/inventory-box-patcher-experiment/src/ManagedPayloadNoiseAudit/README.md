# Managed payload noise audit

This C# tool owns the complete repeatable audit pipeline for the original and
manually patched `Magicka.exe` and `PolygonHead.dll` files. It:

- records all four input paths and SHA-256 hashes;
- creates isolated assembly and reference directories;
- decompiles both pairs as C# 3 projects with the pinned ILSpy tool;
- removes decompiler comments and byte-identical source pairs;
- excludes files added by the patch from the noise checklist;
- creates raw and pair-normalized per-file diffs;
- compares existing methods by stable signatures and canonical IL rather than
  metadata tokens or instruction offsets;
- records true body changes, local-slot/layout-only changes and added members;
- moves diffs containing only `PlayState` field removal, assignment removal and
  equivalent `PlayState.RecentPlayState` reads into
  `feature-diffs/playstate-singleton`, with a `files.txt` manifest;
- writes CSV inventories and `DENOISE_CHECKLIST.md`.

`scripts/audit-manual-payload-noise.ps1` accepts `-GcDiagnosticsMode`:

- `All` keeps the complete normalized manual-payload diff;
- `Exclude` removes `using Magicka.GcDiagnostics` and every expression-statement
  call on `RetentionRegistry` before local and expression normalization;
- `Only` writes a minimal, syntactically valid per-file projection containing
  only that using directive and those calls. The wrapper namespace, type and
  method merely keep the extracted statements parseable and are identical on
  both sides of each comparison.

Every mode also writes `gc-diagnostics-inventory.csv` for each assembly. The
inventory covers the complete patched decompilation, including files added by
the patch that are intentionally absent from the original-versus-patched
comparison. In `Only` mode, diagnostics found in such added files are written
to `diagnostics-only-added-source` so the diagnostics-only result remains
complete without treating an added class as a changed original class.

Removing diagnostics before the remaining normalization is important. It lets
the normalizer collapse temporary locals introduced only so a loaded or newly
created object could be passed to `RetentionRegistry`.

Run it through `scripts/audit-manual-payload-noise.ps1`. The PowerShell file is
only a thin Windows command-line wrapper and contains no audit logic.

The output directory must not already exist. This prevents an audit from
silently mixing results from different input assemblies.

## Static patcher verification

A future static patcher should run this pipeline twice: original versus the
manual reference payload, and the same original versus the generated payload.
Verification must compare the normalized patched source trees, added-source
inventories and assembly semantic inventories. Comparing only rendered diff
text is insufficient because normalization intentionally hides local-slot and
formatting differences. Assembly references, resources and PE/CLI metadata
must also be compared before CLR 2 and Mono JIT validation of every changed
method.
