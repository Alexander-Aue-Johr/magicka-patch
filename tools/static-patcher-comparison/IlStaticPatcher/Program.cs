using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length is < 2 or > 3)
    throw new ArgumentException("Usage: IlStaticPatcher <original-Magicka.exe> " +
        "<output.exe> [--retain-scene-content]");

bool retainSceneContent = args.Length == 3 &&
    args[2] == "--retain-scene-content";
if (args.Length == 3 && !retainSceneContent)
    throw new ArgumentException("Unknown option: " + args[2]);

string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (input.Equals(output, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Input and output must differ.");
File.Copy(input, output, true);

using (ModuleDefinition module = ModuleDefinition.ReadModule(output,
    new ReaderParameters { InMemory = true, ReadWrite = false }))
{
    ApplyFeatureChanges(module, retainSceneContent);
    module.Write(output + ".writing");
}
File.Move(output + ".writing", output, true);
EnableLargeAddressAwareness(output);
Console.WriteLine(output);

static void ApplyFeatureChanges(ModuleDefinition module, bool retainSceneContent)
{
    RemoveGameSparksLifecycleInterop(module);
    RestoreLegacyPlayStateFinalization(module);
    if (retainSceneContent)
        RetainPreviousSceneContentDuringSceneChanges(module);
}

static void RetainPreviousSceneContentDuringSceneChanges(ModuleDefinition module)
{
    TypeDefinition level = RequireType(module, "Magicka.Levels.Level");
    DisablePreviousSceneContentUnload(
        level.Methods.Single(method => method.Name == "ChangeScene" &&
            method.Parameters.Count == 0));
}

static void DisablePreviousSceneContentUnload(MethodDefinition changeScene)
{
    IList<Instruction> instructions = changeScene.Body.Instructions;
    int[] calls = instructions.Select((instruction, index) => (instruction, index))
        .Where(item => item.instruction.Operand is MethodReference called &&
            called.DeclaringType.FullName == "Magicka.Levels.GameScene" &&
            called.Name == "UnloadContent" && called.Parameters.Count == 0)
        .Select(item => item.index).ToArray();
    if (calls.Length != 1 || calls[0] == 0 ||
        !IsLoadLocal(instructions[calls[0] - 1]))
        throw new InvalidOperationException(
            "Unexpected GameScene.UnloadContent call shape.");
    MakeNop(instructions[calls[0] - 1]);
    MakeNop(instructions[calls[0]]);
}

static bool IsLoadLocal(Instruction instruction) => instruction.OpCode.Code is
    Code.Ldloc or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or
    Code.Ldloc_S;

static void RemoveGameSparksLifecycleInterop(ModuleDefinition module)
{
    TypeDefinition game = RequireType(module, "Magicka.Game");
    RemoveGameSparksInitialize(game);
    RemoveGameSparksUpdate(game);
    RemoveGameSparksDeinitialize(game);
}

static void RemoveGameSparksInitialize(TypeDefinition game) =>
    RemoveSingleGameSparksCall(RequireGameSparksMethod(game, "Initialize"));

static void RemoveGameSparksUpdate(TypeDefinition game) =>
    RemoveSingleGameSparksCall(RequireGameSparksMethod(game, "Update"));

static void RemoveGameSparksDeinitialize(TypeDefinition game) =>
    RemoveSingleGameSparksCall(RequireGameSparksMethod(game, "EndRun"));

static void RestoreLegacyPlayStateFinalization(ModuleDefinition module)
{
    TypeDefinition playState = RequireType(module,
        "Magicka.GameLogic.GameStates.PlayState");
    MethodDefinition[] finalizers = playState.Methods.Where(method =>
        method.Name == "Finalize" && method.Parameters.Count == 0).ToArray();
    if (finalizers.Length != 1)
        throw new InvalidOperationException("Expected exactly one PlayState finalizer.");
    playState.Methods.Remove(finalizers[0]);
}

static void RemoveSingleGameSparksCall(MethodDefinition method)
{
    List<Instruction> instructions = method.Body.Instructions.ToList();
    int[] calls = instructions.Select((instruction, index) => (instruction, index))
        .Where(item => item.instruction.Operand is MethodReference called &&
            called.DeclaringType.FullName ==
                "Magicka.WebTools.GameSparks.GameSparksServices")
        .Select(item => item.index).ToArray();
    if (calls.Length != 1 || calls[0] == 0 ||
        instructions[calls[0] - 1].Operand is not MethodReference getter ||
        getter.Name != "get_Instance")
        throw new InvalidOperationException("Unexpected GameSparks call shape in " +
            method.FullName);
    MakeNop(instructions[calls[0] - 1]);
    MakeNop(instructions[calls[0]]);
}

static void MakeNop(Instruction instruction)
{
    instruction.OpCode = OpCodes.Nop;
    instruction.Operand = null;
}

static TypeDefinition RequireType(ModuleDefinition module, string name) =>
    module.GetType(name) ?? throw new InvalidOperationException("Missing type " + name);

static MethodDefinition RequireGameSparksMethod(TypeDefinition type, string name) =>
    type.Methods.Single(method => method.Name == name && method.HasBody &&
        method.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference called &&
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
