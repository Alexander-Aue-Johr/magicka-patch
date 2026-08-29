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

After each analyzer run, the registry frees the completed cycle's weak handles
and temporary files, then reopens tracking. The next disposal checkpoint starts
a fresh analysis cycle, so later levels can report a different retained object
graph without carrying candidates forward from an earlier level.

Root findings contain expectation, managed type, lifecycle, root category, and
a field-labelled type path. Paths, counts, traversal time, and telemetry text
are capped. The analyzer never sends object addresses or weak-handle values.
Stable failure fields identify the analysis stage, exception type, inner
exception type, and HRESULT without including exception messages or paths.
Telemetry removes managed namespaces from candidate types, lifecycle methods,
static members, and every type in a root path. Nested types, generic arguments,
array suffixes, member names, and reference labels remain intact. The local
analysis report keeps fully qualified types for debugging.

CLR 2 also exposes GC statics through pinned handle-table storage. The analyzer
enumerates named static roots once and matches them to the ordinary root path.
When a match exists, it removes the handle-table `System.Object[]` prefix and
reports the static field name. Reference-array edges include a bounded element
index. If named static metadata is unavailable, the analyzer keeps the original
root and adds only bounded array length and sibling-type context.

Telemetry findings are grouped by expectation, managed type, and lifecycle.
Identical root paths are serialized once with an occurrence count. The bounded
selection walks the groups in rounds and does not begin a second round until it
has considered the first row of every group. Within the global limit, a repeated
type therefore cannot consume the complete finding budget.
`finding_count` remains the raw number of findings. `finding_group_count` is the
number of groups, `serialized_finding_count` is the number of complete rows,
and `omitted_finding_count` counts raw findings represented only by rows that
did not fit. `telemetry_truncated` reports this serialization loss. The
existing `truncated` field remains reserved for an incomplete heap traversal or
root analysis.

The release package contains:

- `Magicka.GcAnalyzer.exe`;
- `Magicka.GcAnalyzer.exe.config`;
- `Microsoft.Diagnostics.Runtime.dll`;
- the shared MIT license and third-party notice.

`Magicka.GcDiagnostics.dll` remains in the main payload because both
`Magicka.exe` and `PolygonHead.dll` use the same retention registry. The
external analyzer remains separate so it can inspect a stable process snapshot
without running CLR data-access operations inside the game process.
