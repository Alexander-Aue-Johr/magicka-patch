# Source noise normalizer

This tool creates paired review copies of ILSpy C# output. It replaces local
variable names with deterministic names inside each method. It never edits a
managed assembly.

The first implementation deliberately reports methods whose declaration order
changed instead of treating them as clean. A changed declaration order can
shift every later deterministic name even when the corresponding IL locals are
unchanged. Those files receive the
`normalizer-needs-paired-alignment` status and must not be accepted as a
semantic-only diff yet.

The normalizer is invoked by `scripts/audit-manual-payload-noise.ps1`. Added
source files are excluded from its checklist because they have no original
counterpart and therefore cannot contain recompilation noise relative to an
original method body.
