# Assembly semantic inventory

This helper matches existing types, methods and fields by stable signatures.
It compares method bodies twice: exactly, and with branch targets represented
by instruction index and local slots represented by first-use order. The
second comparison identifies recompilation differences that only renumber
locals or move instruction offsets.

The report is supporting evidence for the paired C# review. A
`semantic/compiler-shaped` method still requires its normalized C# diff to be
read as code; IL equivalence in the general case is not decidable by this tool.
