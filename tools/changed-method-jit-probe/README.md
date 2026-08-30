# Changed-method JIT validation

Run the validator from a 32-bit Windows environment with .NET 2.0/3.5 enabled
and a complete Magicka installation:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-changed-method-jit.ps1 `
  -PreviousAssembly path\to\previous\Magicka.exe `
  -CurrentAssembly .\Magicka.exe `
  -DependencyDirectory path\to\Steam\steamapps\common\Magicka `
  -Mono 'C:\Program Files\Mono\bin\mono.exe'
```

The manifest generator matches methods by stable declaring type, name,
parameter types, and return type. Added methods and methods whose IL, locals,
maximum stack, initialization flag, or exception clauses changed are included.
The CLR-2-compatible probe loads the current assembly from a staging directory
containing the installed game dependencies and calls
`RuntimeHelpers.PrepareMethod` for every concrete changed method. Generic
methods are tested using call-site instantiations or a representative type that
satisfies their declared constraints.

Abstract, runtime-provided, and P/Invoke methods have no managed body to JIT and
are reported as explicit skips. Any other unresolved open generic method fails
validation. Supplying `-Mono` runs the identical manifest through Mono after
the Microsoft CLR 2 pass.
