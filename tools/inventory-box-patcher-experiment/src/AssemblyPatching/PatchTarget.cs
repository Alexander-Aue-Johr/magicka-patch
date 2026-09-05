using Mono.Cecil;

namespace InventoryBoxPatcherExperiment;

public static class PatchTarget
{
    public const string RuntimePatchAssemblyName = "Magicka.CommunityPatch.Runtime";
    public const string RuntimePatchBootstrapType = "Magicka.CommunityPatch.Runtime.Bootstrap";
    public const string ProgramType = "Magicka.Program";

    public static readonly Version RuntimePatchAssemblyVersion = new(1, 0, 0, 0);

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

    public static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> roots)
    {
        foreach (TypeDefinition root in roots)
        {
            yield return root;
            foreach (TypeDefinition nested in Flatten(root.NestedTypes))
                yield return nested;
        }
    }
}
