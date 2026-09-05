# Single-file patcher experiment

This experiment applies the smallest C# file diff between the original
`Magicka.exe` and the current Community Patch in two different ways. It is a
learning and comparison tool, not a replacement for the Community Patch build.

The source analysis uses ILSpy 9.1.0 with C# 3 output for both assemblies. It
removes C# comments with Roslyn, deletes identical source pairs, creates one
diff per remaining file, and ranks the diffs by added plus deleted lines. The
smallest result is `Magicka/GameLogic/UI/InventoryBox.cs` with one added line:

```diff
@@ -67,6 +67,7 @@ namespace Magicka.GameLogic.UI
 			public void Draw(float iDeltaTime)
 			{
 				Point screenSize = RenderManager.Instance.ScreenSize;
+				mTextBoxEffect.ScreenSize = new Vector2(screenSize.X, screenSize.Y);
 				mPosition.X = (float)screenSize.X * 0.5f;
 				mPosition.Y = (float)screenSize.Y * 0.5f;
 				mTextBoxEffect.Color = Vector4.One;
```

The current patch uses this assignment to keep the Tab inventory panel aligned
with the virtual in-game UI size.

## Run the complete experiment

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\inventory-box-patcher-experiment\build.ps1
```

The default output is `tmp/inventory-box-patcher-run`. The script refuses to
overwrite an existing output directory. Pass a different `-OutputDirectory` or
move the earlier result before running it again.

The complete run:

1. backs up the original input;
2. decompiles and compares both input assemblies;
3. builds the two patchers and the CLR-2 runtime module;
4. creates the static and runtime variants;
5. registers the runtime patch against the real original assembly without
   starting the game, then runs the isolated runtime behavior test;
6. compares every original method body with each generated executable;
7. decompiles the static result and asserts the exact C# diff above;
8. asserts that the successful runtime audit reports the same C# change;
9. writes sizes and SHA-256 hashes to `experiment-summary.txt`.

To repeat only the full source comparison:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\inventory-box-patcher-experiment\analyze.ps1 `
  -OutputDirectory ..\..\tmp\another-inventory-box-analysis
```

For the intentionally minimal static variant, run:

```powershell
powershell -ExecutionPolicy Bypass -File `
  .\tools\inventory-box-patcher-experiment\build-simple-static-variant.ps1
```

This variant only builds the static patcher, patches a fresh executable,
decompiles `InventoryBox` before and after, creates the source diff, and compares
the complete diff hunk with `expected/InventoryBox.cs.diff` as one string. It
does not run the assembly-wide verifier, runtime tests, or JIT suite.

## Option A: runtime patch

The runtime directory contains:

- `Magicka.exe`, based on the original executable;
- `Magicka.InventoryBox.RuntimePatch.dll`;
- `0Harmony.dll` 1.2.0.1 for .NET Framework 3.5.

`RuntimePatchPlan` applies the InventoryBox patch and then delegates the
independent PlayState patches to `PlayStatePatchPlan`. The first PlayState
definition guards `AddWorldSyncMessage` so unusable SpawnNPC handles never
enter the world-sync queue. Its runtime test keeps ordinary messages, accepts a
valid NPC from the same play state, and rejects missing and foreign handles.

The remaining PlayState changes are intentionally not bundled into this group.
The constructor telemetry, `Dispose`, `OnExit`, and their cleanup behavior call
APIs added by other Community Patch changes and need a separate dependent patch
category. The apparent `ReloadFromCheckpoint` and empty checkpoint-buffer
changes compile to behavior already present in the original executable, so the
runtime patcher does not rewrite them.

The loader injector adds one assembly reference and one call at the beginning
of `Magicka.Program.Main`. It does not place the gameplay change in the file.
At startup, the runtime DLL finds `InventoryBox.RenderData.Draw(float)` and asks
Harmony to replace the in-memory implementation with the transpiled method.

The transpiler accepts only the expected unpatched structure. It finds the
single `RenderManager.ScreenSize` local-store anchor, resolves the existing
field, constructor, and setter, inserts the ten IL instructions for the C#
assignment immediately after the anchor, converts the affected context before
and after the insertion to four C# lines, and compares their line diff with the
expected string embedded in `InventoryBoxDrawTranspiler.cs`.
The bootstrap then verifies that Harmony created a replacement and registered
exactly that transpiler under the experiment's owner ID.

A normal decompiler reads the executable on disk, so it cannot show Harmony's
dynamic replacement method. The runtime proof therefore has five parts:

- an exact input-anchor check;
- an exact inserted-instruction check inside the transpiler;
- a Harmony registration check after the replacement is installed;
- a CLR-2 probe that installs the replacement into the supplied original
  `Magicka.exe` without starting its entry point;
- a CLR-2 behavior test that calls the patched method and observes the new
  `ScreenSize` value.

Only after all five succeed does the module write
`inventory-box-runtime-audit.txt`, including the effective C# diff. Dumping the
actual dynamic method as a normal managed assembly would require a profiler or
debugger-level capture and would make this intentionally small experiment much
larger.

## Option B: static patch

The static directory contains one new `Magicka.exe`. The static patcher opens
the original assembly with Mono.Cecil and inserts the same field load, local
loads, conversions, `Vector2` construction, and property setter call directly
into `InventoryBox.RenderData.Draw(float)`. Mono.Cecil recalculates the maximum
stack of the containing type's constructor when resolving the target members.
The writer restores that unchanged method header after serialization so this
incidental rewrite does not become part of the result.

The verifier compares all methods in the generated assembly with the original.
Exactly one method may differ, including its locals, exception handlers, and
maximum stack. It then compares that method instruction by instruction with the
current Community Patch, using stable member signatures instead of metadata
token numbers. Finally, ILSpy decompiles the result and the script asserts both
of these conditions:

- the generated `InventoryBox.cs` equals the current patch's file;
- the original-to-generated hunk equals `expected/InventoryBox.cs.diff`.

This makes the static variant easier to audit after the process has finished:
the patch remains ordinary IL in the output file.

## Code structure and abstraction layers

The number of Single Layer of Abstraction levels is variable. The useful rule
is that one method should describe work at one level; it does not require a
fixed three-level architecture.

This experiment uses five levels where they help:

1. `build.ps1` reads as the complete experiment workflow.
2. The two small command-line entry points describe each patching workflow.
3. `RuntimePatchPlan` lists the runtime patch definitions to apply.
4. `RuntimePatchSession`, `RuntimeLoaderInjection`, and
   `InventoryBoxScreenSizeStaticPatch` express the assembly operations.
5. The lowest helpers identify locals and members, emit IL, normalize method
   bodies, and compare metadata.

The source intentionally avoids explanatory code comments. Names and method
boundaries carry the explanation. This README holds information that cannot be
expressed by naming alone, especially the runtime verification limitation and
the reason for each artifact.

## Choosing between A and B

The runtime variant keeps gameplay edits outside the original method on disk
and can make patches modular. Its cost is the loader edit, two additional DLLs,
startup failure modes, Harmony compatibility, and a less direct after-the-fact
audit because the effective method exists only in memory.

The static variant produces a self-contained executable and supports the
strongest direct assertion: decompile the output and compare it with the
desired C# diff. Its cost is rewriting the managed executable for every patch
set and handling conflicts when several static transformations target the same
method.

For this one-line change, Option B is substantially simpler to distribute and
audit. Option A becomes more attractive when independent modules, runtime
enable/disable behavior, or composition with other runtime mods matters more
than a single-file artifact.
