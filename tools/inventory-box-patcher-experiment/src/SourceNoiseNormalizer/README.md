# Source noise normalizer

This tool creates paired review copies of ILSpy C# output. It aligns local
declarations between the original and patched methods and uses the original
variable name for each safely matched pair. Renaming is bound to individual
Roslyn local symbols, so repeated names in separate blocks are not mixed.
Original-only and patch-only locals retain the names emitted by ILSpy. If an
original name would collide in the patched scope, the existing patch name is
retained. The tool also restores original static field initializers when ILSpy moved an
exactly equivalent assignment into a synthetic type initializer.

Compiler-generated parameter aliases are removed only when the source
parameter and alias are read-only and the alias is captured by a lambda or
anonymous delegate. Mutable struct copies and ordinary local aliases remain in
the review diff. The tool never edits a managed assembly.

The normalizer is invoked by `scripts/audit-manual-payload-noise.ps1`. Added
source files are excluded from its checklist because they have no original
counterpart and therefore cannot contain recompilation noise relative to an
original method body.
