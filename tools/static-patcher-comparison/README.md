# Static patcher comparison

This experiment applies the same three behavioral changes to an original
Magicka 1.10.4.2 executable using two managed assembly writers:

- `IlStaticPatcher` uses Mono.Cecil and replaces each GameSparks singleton
  getter/call pair with `nop` instructions.
- `StructuralStaticPatcher` uses dnlib and deletes those instruction objects
  from the selected method bodies, like deleting statements in an editor.

Both remove the complete `PlayState.Finalize` method definition and set the PE
`IMAGE_FILE_LARGE_ADDRESS_AWARE` characteristic. Each patcher validates the
expected type, method and call shape before writing an output file.

For a deliberately leaky local stress test, `IlStaticPatcher` accepts
`--retain-scene-content`. This suppresses only the old scene's
`GameScene.UnloadContent()` call in `Level.ChangeScene`; it does not suppress
global or `PlayState.Dispose` cleanup. This option must never be shipped.

The structural variant is intentionally not described as a C# recompilation.
ILSpy's untouched project export of Magicka 1.10.4.2 is not round-trip
compilable, and dnSpy's Edit Method compiler is not exposed as a supported
command-line API. Recompiling the complete decompiled assembly would also
rewrite almost every method and make the experiment unsuitable for a surgical
static patcher.

## Observed comparison

The reference files used for the historical check are Magicka 1.4.16.0 and
1.5.1.0. Neither version defines `PlayState.Finalize`; 1.10.4.2 defines it and
its body calls `Dispose`. Both patchers therefore remove the method definition.

The Mono.Cecil output has three canonically changed existing method bodies:
`Game.Initialize`, `Game.Update(float)` and `Game.EndRun`. The dnlib writer is
configured with `MetadataFlags.PreserveAll`; this setting is required to avoid
unrelated token and heap reordering. Even with that setting, the tested dnlib
4.5 writer changes the physical IL layout of more than 900 existing methods in
this assembly. The normalized C# is equivalent, but the output fails the
repository's surgical-change gate and is retained only as a comparison
artifact. The Mono.Cecil output passes the CLR 2 and Mono JIT gate.

The `IMAGE_FILE_LARGE_ADDRESS_AWARE` bit is changed directly in the PE COFF
header after the managed assembly writer finishes. Neither managed assembly
library used here exposes this one-bit update as a reliably layout-preserving
operation.
