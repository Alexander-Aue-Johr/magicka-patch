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
        ILProcessor processor = mainMethod.Body.GetILProcessor();
        Instruction first = mainMethod.Body.Instructions[0];
        processor.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        processor.InsertBefore(first, Instruction.Create(OpCodes.Call, bootstrap));
        AssertApplied(module);
    }

    public static void AssertApplied(ModuleDefinition module)
    {
        MethodDefinition mainMethod = PatchTarget.FindMainMethod(module);
        int matchingReferences = module.AssemblyReferences.Count(reference =>
            reference.Name == PatchTarget.RuntimePatchAssemblyName);
        if (matchingReferences != 1 ||
            mainMethod.Body.Instructions[0].OpCode != OpCodes.Ldarg_0 ||
            !IsBootstrapCall(mainMethod.Body.Instructions[1]))
            throw new InvalidOperationException(
                "Expected one runtime assembly reference and an argument-aware bootstrap call at the beginning of Main.");
    }

    public static bool IsBootstrapCall(Instruction instruction)
    {
        return instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference method &&
            method.DeclaringType.FullName == PatchTarget.RuntimePatchBootstrapType &&
            method.Name == "Apply" &&
            method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.FullName == "System.String[]";
    }

    private static MethodReference AddRuntimePatchReference(ModuleDefinition module)
    {
        AssemblyNameReference assembly = new(
            PatchTarget.RuntimePatchAssemblyName,
            PatchTarget.RuntimePatchAssemblyVersion);
        module.AssemblyReferences.Add(assembly);

        TypeReference bootstrapType = new(
            "Magicka.CommunityPatch.Runtime",
            "Bootstrap",
            module,
            assembly,
            false);

        MethodReference bootstrap = new(
            "Apply",
            module.TypeSystem.Void,
            bootstrapType)
        {
            HasThis = false,
            CallingConvention = MethodCallingConvention.Default
        };
        bootstrap.Parameters.Add(
            new ParameterDefinition(new ArrayType(module.TypeSystem.String)));
        return bootstrap;
    }

    private static void AssertLoaderIsAbsent(ModuleDefinition module, MethodDefinition mainMethod)
    {
        if (module.AssemblyReferences.Any(reference =>
                reference.Name == PatchTarget.RuntimePatchAssemblyName))
            throw new InvalidOperationException("The runtime patch assembly reference is already present.");

        if (mainMethod.Body.Instructions.Any(IsBootstrapCall))
            throw new InvalidOperationException("The runtime patch bootstrap call is already present.");
    }
}
