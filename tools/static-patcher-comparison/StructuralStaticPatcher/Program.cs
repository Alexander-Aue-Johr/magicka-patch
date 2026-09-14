using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

if (args.Length != 2)
    throw new ArgumentException("Usage: StructuralStaticPatcher <original-Magicka.exe> <output.exe>");

string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (input.Equals(output, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Input and output must differ.");

using (ModuleDefMD module = ModuleDefMD.Load(input))
{
    ApplyFeatureChanges(module);
    ModuleWriterOptions options = new(module);
    options.MetadataOptions.Flags = MetadataFlags.PreserveAll;
    module.Write(output, options);
}
EnableLargeAddressAwareness(output);
Console.WriteLine(output);

static void ApplyFeatureChanges(ModuleDefMD module)
{
    RemoveGameSparksLifecycleInterop(module);
    RestoreLegacyPlayStateFinalization(module);
}

static void RemoveGameSparksLifecycleInterop(ModuleDefMD module)
{
    TypeDef game = RequireType(module, "Magicka.Game");
    RemoveGameSparksInitialize(game);
    RemoveGameSparksUpdate(game);
    RemoveGameSparksDeinitialize(game);
}

static void RemoveGameSparksInitialize(TypeDef game) =>
    RemoveSingleGameSparksStatement(RequireGameSparksMethod(game, "Initialize"));

static void RemoveGameSparksUpdate(TypeDef game) =>
    RemoveSingleGameSparksStatement(RequireGameSparksMethod(game, "Update"));

static void RemoveGameSparksDeinitialize(TypeDef game) =>
    RemoveSingleGameSparksStatement(RequireGameSparksMethod(game, "EndRun"));

static void RestoreLegacyPlayStateFinalization(ModuleDefMD module)
{
    TypeDef playState = RequireType(module,
        "Magicka.GameLogic.GameStates.PlayState");
    MethodDef[] finalizers = playState.Methods.Where(method =>
        method.Name.String == "Finalize" &&
        method.MethodSig?.Params.Count == 0).ToArray();
    if (finalizers.Length != 1)
        throw new InvalidOperationException("Expected exactly one PlayState finalizer.");
    playState.Methods.Remove(finalizers[0]);
}

static void RemoveSingleGameSparksStatement(MethodDef method)
{
    IList<Instruction> instructions = method.Body.Instructions;
    int[] calls = instructions.Select((instruction, index) => (instruction, index))
        .Where(item => item.instruction.Operand is IMethod called &&
            called.DeclaringType.FullName ==
                "Magicka.WebTools.GameSparks.GameSparksServices")
        .Select(item => item.index).ToArray();
    if (calls.Length != 1 || calls[0] == 0 ||
        instructions[calls[0] - 1].Operand is not IMethod getter ||
        getter.Name != "get_Instance")
        throw new InvalidOperationException("Unexpected GameSparks call shape in " +
            method.FullName);
    if (instructions.Any(instruction =>
        ReferenceEquals(instruction.Operand, instructions[calls[0] - 1]) ||
        ReferenceEquals(instruction.Operand, instructions[calls[0]])))
        throw new InvalidOperationException("Refusing to remove a branch target.");
    instructions.RemoveAt(calls[0]);
    instructions.RemoveAt(calls[0] - 1);
}

static TypeDef RequireType(ModuleDef module, string name) =>
    module.Find(name, false) ?? throw new InvalidOperationException("Missing type " + name);

static MethodDef RequireGameSparksMethod(TypeDef type, string name) =>
    type.Methods.Single(method => method.Name.String == name && method.HasBody &&
        method.Body.Instructions.Any(instruction =>
            instruction.Operand is IMethod called &&
            called.DeclaringType.FullName ==
                "Magicka.WebTools.GameSparks.GameSparksServices"));

static void EnableLargeAddressAwareness(string path)
{
    using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite,
        FileShare.None);
    using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, true);
    using BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true);
    stream.Position = 0x3c;
    int peOffset = reader.ReadInt32();
    stream.Position = peOffset;
    if (reader.ReadUInt32() != 0x00004550)
        throw new BadImageFormatException("Missing PE signature.");
    stream.Position = peOffset + 4 + 18;
    ushort characteristics = reader.ReadUInt16();
    stream.Position -= 2;
    writer.Write((ushort)(characteristics | 0x20));
}
