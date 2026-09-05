using Mono.Cecil;

namespace InventoryBoxPatcherExperiment;

public static class AssemblyFile
{
    public static ModuleDefinition Read(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Assembly input does not exist.", fullPath);

        DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(Path.GetDirectoryName(fullPath)!);

        return ModuleDefinition.ReadModule(fullPath, new ReaderParameters
        {
            AssemblyResolver = resolver,
            InMemory = true,
            ReadSymbols = false
        });
    }

    public static void WriteNew(
        ModuleDefinition module,
        string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            throw new IOException($"Refusing to overwrite existing output: {fullPath}");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        module.Write(fullPath, new WriterParameters { WriteSymbols = false });
    }
}
