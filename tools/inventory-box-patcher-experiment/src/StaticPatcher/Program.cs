using InventoryBoxPatcherExperiment;
using Mono.Cecil;

return StaticPatcher.Run(args);

internal static class StaticPatcher
{
    public static int Run(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            Console.Error.WriteLine("usage: StaticPatcher <original Magicka.exe> <patched Magicka.exe>");
            return 2;
        }

        string inputPath = Path.GetFullPath(arguments[0]);
        string outputPath = Path.GetFullPath(arguments[1]);

        using ModuleDefinition originalAssembly = AssemblyFile.Read(inputPath);
        MethodDefinition unchangedConstructor = PatchTarget.FindInventoryBoxConstructor(originalAssembly);
        InventoryBoxScreenSizeStaticPatch.Apply(originalAssembly);
        AssemblyFile.WriteNew(originalAssembly, outputPath, unchangedConstructor);

        Console.WriteLine("Applied InventoryBox.RenderData.Draw ScreenSize patch.");
        Console.WriteLine($"Input:  {inputPath}");
        Console.WriteLine($"Output: {outputPath}");
        return 0;
    }
}
