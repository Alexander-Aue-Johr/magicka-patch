# Managed payload noise audit

This C# tool owns the complete repeatable audit pipeline for the original and
manually patched `Magicka.exe` and `PolygonHead.dll` files. It:

- records all four input paths and SHA-256 hashes;
- creates isolated assembly and reference directories;
- decompiles both pairs as C# 3 projects with the pinned ILSpy tool;
- removes decompiler comments and byte-identical source pairs;
- excludes files added by the patch from the noise checklist;
- creates raw and local-name-normalized per-file diffs;
- writes CSV inventories and `DENOISE_CHECKLIST.md`.

Run it through `scripts/audit-manual-payload-noise.ps1`. The PowerShell file is
only a thin Windows command-line wrapper and contains no audit logic.

The output directory must not already exist. This prevents an audit from
silently mixing results from different input assemblies.
