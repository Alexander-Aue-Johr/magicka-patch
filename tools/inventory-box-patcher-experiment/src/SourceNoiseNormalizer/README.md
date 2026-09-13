# Source noise normalizer

This tool creates paired review copies of ILSpy C# output. It aligns local
declarations between the original and patched methods before assigning stable
names. It also restores original static field initializers when ILSpy moved an
exactly equivalent assignment into a synthetic type initializer.

Compiler-generated parameter aliases are removed only when the source
parameter and alias are read-only and the alias is captured by a lambda or
anonymous delegate. Mutable struct copies and ordinary local aliases remain in
the review diff. The tool never edits a managed assembly.

The normalizer is invoked by `scripts/audit-manual-payload-noise.ps1`. Added
source files are excluded from its checklist because they have no original
counterpart and therefore cannot contain recompilation noise relative to an
original method body.
