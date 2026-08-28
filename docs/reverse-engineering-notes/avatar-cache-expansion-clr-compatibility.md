# Avatar cache expansion CLR compatibility

Community Patch 0.0.45 changed `Avatar.GetFromCache(Player)` so an exhausted
cache allocates another Avatar instead of falling through with a null result.
The added fallback preserved the existing lookup, locking, and player
assignment behavior.

## Windows failure

The new fallback increased the distance from an existing branch at `IL_0107`
to the common return path from 84 bytes to 140 bytes. The recompiled method kept
the original `br.s` opcode, whose signed one-byte displacement can represent
only -128 through 127 bytes. Encoding 140 as `0x8C` therefore produced a
displacement of -116 and targeted the middle of another instruction.

Wine Mono ran the method, but Microsoft's CLR 2 JIT rejected it during player
creation. Depending on the JIT path, Windows reported either that the runtime
detected an invalid program or that the JIT encountered an internal limitation.
Removing the later GC-retention return hook did not help because that hook was
not the cause.

## Patch behavior

The overflowing `br.s` is now a four-byte-displacement `br` targeting the same
common exit as the other successful cache-reuse branch. The pool-exhaustion
allocation and its bounded telemetry remain unchanged. The GC-retention return
hook also remains enabled.

The retention instrumentation tool repairs this known malformed input branch
before writing the assembly. The payload validator rejects every short or long
branch whose target does not resolve to an instruction, preventing another
out-of-range branch from shipping unnoticed.
