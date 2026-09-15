using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length is < 2 or > 3)
    throw new ArgumentException("Usage: IlStaticPatcher <original-Magicka.exe> " +
        "<output.exe> [--retain-scene-content|--probe-high-gc-heap]");

bool retainSceneContent = args.Length == 3 &&
    args[2] == "--retain-scene-content";
bool probeHighGcHeap = args.Length == 3 && args[2] == "--probe-high-gc-heap";
if (args.Length == 3 && !retainSceneContent && !probeHighGcHeap)
    throw new ArgumentException("Unknown option: " + args[2]);

string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (input.Equals(output, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Input and output must differ.");
File.Copy(input, output, true);

using (ModuleDefinition module = ModuleDefinition.ReadModule(output,
    new ReaderParameters { InMemory = true, ReadWrite = false }))
{
    ApplyFeatureChanges(module, retainSceneContent, probeHighGcHeap);
    module.Write(output + ".writing");
}
File.Move(output + ".writing", output, true);
EnableLargeAddressAwareness(output);
Console.WriteLine(output);

static void ApplyFeatureChanges(ModuleDefinition module, bool retainSceneContent,
    bool probeHighGcHeap)
{
    RemoveGameSparksLifecycleInterop(module);
    RestoreLegacyPlayStateFinalization(module);
    if (retainSceneContent)
        RetainPreviousSceneContentDuringSceneChanges(module);
    if (probeHighGcHeap)
        AddHighGcHeapCapabilityProbe(module);
}

static void AddHighGcHeapCapabilityProbe(ModuleDefinition module)
{
    const string typeName = "ExperimentalHighGcHeapProbe";
    if (module.Types.Any(type => type.Name == typeName))
        throw new InvalidOperationException("High-GC-heap probe is already present.");

    TypeDefinition type = new("Magicka", typeName,
        TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.NotPublic,
        module.TypeSystem.Object);
    module.Types.Add(type);
    MethodDefinition run = new("Run",
        MethodAttributes.Static | MethodAttributes.Assembly |
        MethodAttributes.HideBySig, module.TypeSystem.Void);
    type.Methods.Add(run);
    BuildHighGcHeapProbeBody(module, run);

    MethodDefinition main = RequireType(module, "Magicka.Program").Methods.Single(
        method => method.Name == "Main" && method.Parameters.Count == 1);
    ILProcessor processor = main.Body.GetILProcessor();
    processor.InsertBefore(main.Body.Instructions[0],
        Instruction.Create(OpCodes.Call, run));
}

static void BuildHighGcHeapProbeBody(ModuleDefinition module,
    MethodDefinition method)
{
    MethodBody body = method.Body;
    body.InitLocals = true;
    VariableDefinition path = new(module.TypeSystem.String);
    VariableDefinition blocks = new(new ArrayType(new ArrayType(module.TypeSystem.Byte)));
    VariableDefinition completed = new(module.TypeSystem.Int32);
    TypeReference exceptionType = FrameworkType(module, "System", "Exception");
    VariableDefinition error = new(exceptionType);
    body.Variables.Add(path);
    body.Variables.Add(blocks);
    body.Variables.Add(completed);
    body.Variables.Add(error);

    TypeReference appDomain = FrameworkType(module, "System", "AppDomain");
    MethodReference currentDomain = FrameworkMethod("get_CurrentDomain", appDomain,
        appDomain, false);
    MethodReference baseDirectory = FrameworkMethod("get_BaseDirectory",
        module.TypeSystem.String, appDomain, true);
    MethodReference combine = FrameworkMethod("Combine", module.TypeSystem.String,
        FrameworkType(module, "System.IO", "Path"), false,
        module.TypeSystem.String, module.TypeSystem.String);
    MethodReference writeAllText = FrameworkMethod("WriteAllText",
        module.TypeSystem.Void, FrameworkType(module, "System.IO", "File"), false,
        module.TypeSystem.String, module.TypeSystem.String);
    MethodReference intToString = FrameworkMethod("ToString",
        module.TypeSystem.String, module.TypeSystem.Int32, true);
    MethodReference exceptionToString = FrameworkMethod("ToString",
        module.TypeSystem.String, exceptionType, true);
    TypeReference gc = FrameworkType(module, "System", "GC");
    MethodReference collect = FrameworkMethod("Collect", module.TypeSystem.Void,
        gc, false);
    MethodReference wait = FrameworkMethod("WaitForPendingFinalizers",
        module.TypeSystem.Void, gc, false);

    ILProcessor il = body.GetILProcessor();
    Instruction tryStart = Instruction.Create(OpCodes.Nop);
    Instruction loopCheck = Instruction.Create(OpCodes.Ldloc, completed);
    Instruction success = Instruction.Create(OpCodes.Ldloc, path);
    Instruction catchStart = Instruction.Create(OpCodes.Stloc, error);
    Instruction cleanup = Instruction.Create(OpCodes.Ldnull);

    il.Append(Instruction.Create(OpCodes.Call, currentDomain));
    il.Append(Instruction.Create(OpCodes.Callvirt, baseDirectory));
    il.Append(Instruction.Create(OpCodes.Ldstr, "high-gc-heap-probe.txt"));
    il.Append(Instruction.Create(OpCodes.Call, combine));
    il.Append(Instruction.Create(OpCodes.Stloc, path));
    il.Append(Instruction.Create(OpCodes.Ldc_I4, 320));
    il.Append(Instruction.Create(OpCodes.Newarr, new ArrayType(module.TypeSystem.Byte)));
    il.Append(Instruction.Create(OpCodes.Stloc, blocks));
    il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    il.Append(Instruction.Create(OpCodes.Stloc, completed));
    il.Append(tryStart);
    il.Append(Instruction.Create(OpCodes.Br, loopCheck));
    Instruction loopBody = Instruction.Create(OpCodes.Ldloc, blocks);
    il.Append(loopBody);
    il.Append(Instruction.Create(OpCodes.Ldloc, completed));
    il.Append(Instruction.Create(OpCodes.Ldc_I4, 8 * 1024 * 1024));
    il.Append(Instruction.Create(OpCodes.Newarr, module.TypeSystem.Byte));
    il.Append(Instruction.Create(OpCodes.Stelem_Ref));
    il.Append(Instruction.Create(OpCodes.Ldloc, completed));
    il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    il.Append(Instruction.Create(OpCodes.Add));
    il.Append(Instruction.Create(OpCodes.Stloc, completed));
    il.Append(Instruction.Create(OpCodes.Ldloc, completed));
    il.Append(Instruction.Create(OpCodes.Ldc_I4_8));
    il.Append(Instruction.Create(OpCodes.Rem));
    il.Append(Instruction.Create(OpCodes.Brtrue, loopCheck));
    il.Append(Instruction.Create(OpCodes.Ldloc, path));
    il.Append(Instruction.Create(OpCodes.Ldloca, completed));
    il.Append(Instruction.Create(OpCodes.Call, intToString));
    il.Append(Instruction.Create(OpCodes.Call, writeAllText));
    il.Append(loopCheck);
    il.Append(Instruction.Create(OpCodes.Ldc_I4, 320));
    il.Append(Instruction.Create(OpCodes.Blt, loopBody));
    il.Append(success);
    il.Append(Instruction.Create(OpCodes.Ldstr, "SUCCESS: allocated 2560 MiB"));
    il.Append(Instruction.Create(OpCodes.Call, writeAllText));
    il.Append(Instruction.Create(OpCodes.Leave, cleanup));
    il.Append(catchStart);
    il.Append(Instruction.Create(OpCodes.Ldloc, path));
    il.Append(Instruction.Create(OpCodes.Ldloc, error));
    il.Append(Instruction.Create(OpCodes.Callvirt, exceptionToString));
    il.Append(Instruction.Create(OpCodes.Call, writeAllText));
    il.Append(Instruction.Create(OpCodes.Leave, cleanup));
    il.Append(cleanup);
    il.Append(Instruction.Create(OpCodes.Stloc, blocks));
    il.Append(Instruction.Create(OpCodes.Call, collect));
    il.Append(Instruction.Create(OpCodes.Call, wait));
    il.Append(Instruction.Create(OpCodes.Call, collect));
    il.Append(Instruction.Create(OpCodes.Ret));

    body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
    {
        CatchType = exceptionType,
        TryStart = tryStart,
        TryEnd = catchStart,
        HandlerStart = catchStart,
        HandlerEnd = cleanup
    });
}

static TypeReference FrameworkType(ModuleDefinition module, string @namespace,
    string name) => new(@namespace, name, module, module.TypeSystem.CoreLibrary);

static MethodReference FrameworkMethod(string name, TypeReference returnType,
    TypeReference declaringType, bool hasThis, params TypeReference[] parameters)
{
    MethodReference method = new(name, returnType, declaringType)
    {
        HasThis = hasThis,
        CallingConvention = MethodCallingConvention.Default
    };
    foreach (TypeReference parameter in parameters)
        method.Parameters.Add(new ParameterDefinition(parameter));
    return method;
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
