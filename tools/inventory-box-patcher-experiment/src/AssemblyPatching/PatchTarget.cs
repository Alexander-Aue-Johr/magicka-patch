using Mono.Cecil;

namespace InventoryBoxPatcherExperiment;

public static class PatchTarget
{
    public const string RuntimePatchAssemblyName = "Magicka.InventoryBox.RuntimePatch";
    public const string RuntimePatchBootstrapType = "Magicka.InventoryBoxRuntimePatch.Bootstrap";
    public const string InventoryBoxType = "Magicka.GameLogic.UI.InventoryBox";
    public const string RenderDataType = "Magicka.GameLogic.UI.InventoryBox/RenderData";
    public const string DrawMethodName = "Draw";
    public const string ProgramType = "Magicka.Program";

    public static readonly Version RuntimePatchAssemblyVersion = new(1, 0, 0, 0);

    public static MethodDefinition FindDrawMethod(ModuleDefinition module)
    {
        TypeDefinition renderData = FindType(module, RenderDataType);
        MethodDefinition[] matches = renderData.Methods
            .Where(method =>
                method.Name == DrawMethodName &&
                !method.IsStatic &&
                method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.FullName == "System.Single")
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one {RenderDataType}.{DrawMethodName}(System.Single), found {matches.Length}.");
    }

    public static MethodDefinition FindMainMethod(ModuleDefinition module)
    {
        TypeDefinition program = Flatten(module.Types)
            .SingleOrDefault(type => type.FullName == ProgramType)
            ?? throw new InvalidOperationException($"Type {ProgramType} was not found.");

        MethodDefinition[] matches = program.Methods
            .Where(method => method.Name == "Main" && method.IsStatic && method.HasBody)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one static {ProgramType}.Main method with a body, found {matches.Length}.");
    }

    public static MethodDefinition FindInventoryBoxConstructor(ModuleDefinition module)
    {
        TypeDefinition inventoryBox = FindType(module, InventoryBoxType);
        MethodDefinition[] matches = inventoryBox.Methods
            .Where(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one parameterless {InventoryBoxType} constructor, found {matches.Length}.");
    }

    public static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> roots)
    {
        foreach (TypeDefinition root in roots)
        {
            yield return root;
            foreach (TypeDefinition nested in Flatten(root.NestedTypes))
                yield return nested;
        }
    }

    private static TypeDefinition FindType(ModuleDefinition module, string fullName)
    {
        return Flatten(module.Types).SingleOrDefault(type => type.FullName == fullName)
            ?? throw new InvalidOperationException($"Type {fullName} was not found.");
    }
}
