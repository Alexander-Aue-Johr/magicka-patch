using InventoryBoxPatcherExperiment;
using Mono.Cecil;

return RuntimeLoaderInjector.Run(args);

internal static class RuntimeLoaderInjector
{
    public static int Run(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            Console.Error.WriteLine(
                "usage: RuntimeLoaderInjector <original Magicka.exe> <runtime-enabled Magicka.exe>");
            return 2;
        }

        string inputPath = Path.GetFullPath(arguments[0]);
        string outputPath = Path.GetFullPath(arguments[1]);

        using ModuleDefinition originalAssembly = AssemblyFile.Read(inputPath);
        RuntimeLoaderInjection.Apply(originalAssembly);
        AssemblyFile.WriteNew(originalAssembly, outputPath);

        Console.WriteLine("Injected one runtime patch loader call at the beginning of Magicka.Program.Main.");
        Console.WriteLine($"Input:  {inputPath}");
        Console.WriteLine($"Output: {outputPath}");
        return 0;
    }
}
