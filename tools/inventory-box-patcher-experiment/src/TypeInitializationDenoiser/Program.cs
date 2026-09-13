using Mono.Cecil;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("usage: TypeInitializationDenoiser <original> <patched> [output]");
    return 2;
}

string originalPath = Path.GetFullPath(args[0]);
string patchedPath = Path.GetFullPath(args[1]);
string? outputPath = args.Length == 3 ? Path.GetFullPath(args[2]) : null;

using AssemblyDefinition original = AssemblyDefinition.ReadAssembly(originalPath);
using AssemblyDefinition patched = AssemblyDefinition.ReadAssembly(patchedPath);
Dictionary<string, TypeDefinition> originals = Types(original.MainModule)
    .ToDictionary(type => type.FullName, StringComparer.Ordinal);

List<TypeDefinition> mismatches = Types(patched.MainModule)
    .Where(type => originals.TryGetValue(type.FullName, out TypeDefinition? oldType) &&
        oldType.IsBeforeFieldInit != type.IsBeforeFieldInit)
    .OrderBy(type => type.FullName, StringComparer.Ordinal)
    .ToList();

foreach (TypeDefinition type in mismatches)
{
    TypeDefinition oldType = originals[type.FullName];
    Console.WriteLine($"{type.FullName}\toriginal={oldType.IsBeforeFieldInit}\tpatched={type.IsBeforeFieldInit}");
    if (outputPath != null)
        type.IsBeforeFieldInit = oldType.IsBeforeFieldInit;
}

Console.WriteLine($"mismatches={mismatches.Count}");
if (outputPath != null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    patched.Write(outputPath);
    Console.WriteLine($"written={outputPath}");
}

return 0;

static IEnumerable<TypeDefinition> Types(ModuleDefinition module) =>
    module.Types.SelectMany(Traverse);

static IEnumerable<TypeDefinition> Traverse(TypeDefinition type) =>
    new[] { type }.Concat(type.NestedTypes.SelectMany(Traverse));
