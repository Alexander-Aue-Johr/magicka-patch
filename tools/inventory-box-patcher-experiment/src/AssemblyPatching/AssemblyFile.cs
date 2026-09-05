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
        string path,
        params MethodDefinition[] methodHeadersToPreserve)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            throw new IOException($"Refusing to overwrite existing output: {fullPath}");

        Dictionary<string, int> preservedMaxStacks = methodHeadersToPreserve
            .ToDictionary(
                method => method.FullName,
                method => method.Body.MaxStackSize,
                StringComparer.Ordinal);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        module.Write(fullPath, new WriterParameters { WriteSymbols = false });
        MethodHeaderPreserver.RestoreMaxStacks(fullPath, preservedMaxStacks);
    }
}

internal static class MethodHeaderPreserver
{
    internal static void RestoreMaxStacks(
        string assemblyPath,
        IReadOnlyDictionary<string, int> expectedMaxStacks)
    {
        List<MaxStackPatch> patches = FindChangedMaxStacks(assemblyPath, expectedMaxStacks);
        if (patches.Count == 0)
            return;

        byte[] image = File.ReadAllBytes(assemblyPath);
        foreach (MaxStackPatch patch in patches)
        {
            int methodOffset = PortableExecutableLayout.RvaToFileOffset(image, patch.MethodRva);
            if ((image[methodOffset] & 3) != 3)
                throw new InvalidOperationException(
                    $"Method {patch.MethodName} has no writable fat method header.");
            image[methodOffset + 2] = (byte)(patch.MaxStack & 0xff);
            image[methodOffset + 3] = (byte)(patch.MaxStack >> 8);
        }

        File.WriteAllBytes(assemblyPath, image);
    }

    private static List<MaxStackPatch> FindChangedMaxStacks(
        string assemblyPath,
        IReadOnlyDictionary<string, int> expectedMaxStacks)
    {
        using ModuleDefinition writtenModule = ModuleDefinition.ReadModule(assemblyPath);
        return PatchTarget.Flatten(writtenModule.Types)
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .Where(method => expectedMaxStacks.TryGetValue(method.FullName, out int expected) &&
                method.Body.MaxStackSize != expected)
            .Select(method => new MaxStackPatch(
                method.FullName,
                checked((uint)method.RVA),
                checked((ushort)expectedMaxStacks[method.FullName])))
            .ToList();
    }

    private sealed record MaxStackPatch(string MethodName, uint MethodRva, ushort MaxStack);
}

internal static class PortableExecutableLayout
{
    internal static int RvaToFileOffset(byte[] image, uint rva)
    {
        int peHeaderOffset = checked((int)ReadUInt32(image, 0x3c));
        if (ReadUInt32(image, peHeaderOffset) != 0x00004550)
            throw new InvalidOperationException("The output does not contain a valid PE signature.");

        int coffHeaderOffset = peHeaderOffset + 4;
        int sectionCount = ReadUInt16(image, coffHeaderOffset + 2);
        int optionalHeaderSize = ReadUInt16(image, coffHeaderOffset + 16);
        int sectionHeaderOffset = coffHeaderOffset + 20 + optionalHeaderSize;

        for (int sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
        {
            int currentSection = sectionHeaderOffset + sectionIndex * 40;
            uint virtualSize = ReadUInt32(image, currentSection + 8);
            uint virtualAddress = ReadUInt32(image, currentSection + 12);
            uint rawSize = ReadUInt32(image, currentSection + 16);
            uint rawAddress = ReadUInt32(image, currentSection + 20);
            uint mappedSize = Math.Max(virtualSize, rawSize);
            if (rva < virtualAddress || rva >= virtualAddress + mappedSize)
                continue;

            return checked((int)(rawAddress + rva - virtualAddress));
        }

        throw new InvalidOperationException($"RVA 0x{rva:X8} is outside all PE sections.");
    }

    private static ushort ReadUInt16(byte[] image, int offset)
    {
        return (ushort)(image[offset] | image[offset + 1] << 8);
    }

    private static uint ReadUInt32(byte[] image, int offset)
    {
        return (uint)(
            image[offset] |
            image[offset + 1] << 8 |
            image[offset + 2] << 16 |
            image[offset + 3] << 24);
    }
}
