using Mono.Cecil;
using Mono.Cecil.Cil;

namespace InventoryBoxPatcherExperiment;

public static class RuntimeLoaderInjection
{
    public static void Apply(ModuleDefinition module)
    {
        MethodDefinition mainMethod = PatchTarget.FindMainMethod(module);
        AssertLoaderIsAbsent(module, mainMethod);
        MethodReference bootstrap = AddRuntimePatchReference(module);
        mainMethod.Body.GetILProcessor().InsertBefore(
            mainMethod.Body.Instructions[0],
            Instruction.Create(OpCodes.Call, bootstrap));
        AssertLoaderIsFirstInstruction(mainMethod);
    }

    public static bool IsBootstrapCall(Instruction instruction)
    {
        return instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference method &&
            method.DeclaringType.FullName == PatchTarget.RuntimePatchBootstrapType &&
            method.Name == "Apply" &&
            method.Parameters.Count == 0;
    }

    private static MethodReference AddRuntimePatchReference(ModuleDefinition module)
    {
        AssemblyNameReference assembly = new(
            PatchTarget.RuntimePatchAssemblyName,
            PatchTarget.RuntimePatchAssemblyVersion);
        module.AssemblyReferences.Add(assembly);

        TypeReference bootstrapType = new(
            "Magicka.InventoryBoxRuntimePatch",
            "Bootstrap",
            module,
            assembly,
            false);

        return new MethodReference("Apply", module.TypeSystem.Void, bootstrapType)
        {
            HasThis = false,
            CallingConvention = MethodCallingConvention.Default
        };
    }

    private static void AssertLoaderIsAbsent(ModuleDefinition module, MethodDefinition mainMethod)
    {
        if (module.AssemblyReferences.Any(reference =>
                reference.Name == PatchTarget.RuntimePatchAssemblyName))
            throw new InvalidOperationException("The runtime patch assembly reference is already present.");

        if (mainMethod.Body.Instructions.Any(IsBootstrapCall))
            throw new InvalidOperationException("The runtime patch bootstrap call is already present.");
    }

    private static void AssertLoaderIsFirstInstruction(MethodDefinition mainMethod)
    {
        if (!IsBootstrapCall(mainMethod.Body.Instructions[0]))
            throw new InvalidOperationException("The runtime patch bootstrap is not the first Main instruction.");
    }
}
