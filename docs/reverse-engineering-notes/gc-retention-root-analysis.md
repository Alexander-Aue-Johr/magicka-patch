# GC retention root analysis

The optional retention diagnostics use weak handles inside Magicka and perform
heap analysis in a separate 32-bit .NET Framework process. The analyzer uses
Microsoft.Diagnostics.Runtime 1.1.46104, which can inspect Magicka's CLR
2.0.50727 runtime on Windows.

After a disposal checkpoint and a later generation-2 collection, the runtime
registry writes eligible watches to a bounded manifest. `Magicka.GcAnalyzer`
attaches to a process snapshot, validates the registry version, resolves the
weak handles, and walks GC roots for objects marked `MustCollect`. Objects
marked `MustDetach` receive a bounded outbound-reference traversal.

Root findings contain expectation, managed type, lifecycle, root category, and
a field-labelled type path. Paths, counts, traversal time, and telemetry text
are capped. The analyzer never sends object addresses or weak-handle values.
Stable failure fields identify the analysis stage, exception type, inner
exception type, and HRESULT without including exception messages or paths.

The release package contains:

- `Magicka.GcAnalyzer.exe`;
- `Magicka.GcAnalyzer.exe.config`;
- `Microsoft.Diagnostics.Runtime.dll`;
- the shared MIT license and third-party notice.

`Magicka.GcDiagnostics.dll` remains in the main payload because both
`Magicka.exe` and `PolygonHead.dll` use the same retention registry. The
external analyzer remains separate so it can inspect a stable process snapshot
without running CLR data-access operations inside the game process.
