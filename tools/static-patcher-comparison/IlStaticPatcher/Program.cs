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
    ReplaceStoredPlayStateReferencesWithRecentPlayState(module);
    if (retainSceneContent)
        RetainPreviousSceneContentDuringSceneChanges(module);
    if (probeHighGcHeap)
        AddHighGcHeapCapabilityProbe(module);
}

static void ReplaceStoredPlayStateReferencesWithRecentPlayState(
    ModuleDefinition module)
{
    TypeDefinition playState = RequireType(module,
        "Magicka.GameLogic.GameStates.PlayState");
    MethodDefinition recentPlayState = playState.Methods.Single(method =>
        method.Name == "get_RecentPlayState" && method.IsStatic &&
        method.Parameters.Count == 0);

    foreach (string fieldName in MigratedPlayStateFields())
    {
        int separator = fieldName.LastIndexOf("::", StringComparison.Ordinal);
        TypeDefinition type = RequireType(module, fieldName[..separator]);
        string name = fieldName[(separator + 2)..];
        FieldDefinition field = type.Fields.Single(candidate =>
            candidate.Name == name && candidate.FieldType.FullName ==
                playState.FullName);
        ReplacePlayStateFieldReferences(module, field, recentPlayState,
            replaceWrites: true, methodFilter: null);
        type.Fields.Remove(field);
    }

    foreach (string fieldName in RetainedPlayStateFieldsWithAllUsesMigrated())
    {
        FieldDefinition field = RequirePlayStateField(module, playState, fieldName);
        ReplacePlayStateFieldReferences(module, field, recentPlayState,
            replaceWrites: true, methodFilter: null);
    }

    foreach (string fieldName in RetainedPlayStateFieldsWithReadsMigrated())
    {
        FieldDefinition field = RequirePlayStateField(module, playState, fieldName);
        ReplacePlayStateFieldReferences(module, field, recentPlayState,
            replaceWrites: false, methodFilter: null);
    }

    FieldDefinition entityPlayState = RequirePlayStateField(module, playState,
        "Magicka.GameLogic.Entities.Entity::mPlayState");
    HashSet<string> entityMethods = new(EntityPlayStateMethodsMigrated(),
        StringComparer.Ordinal);
    ReplacePlayStateFieldReferences(module, entityPlayState, recentPlayState,
        replaceWrites: true, methodFilter: method =>
            entityMethods.Contains(method.FullName));
}

static FieldDefinition RequirePlayStateField(ModuleDefinition module,
    TypeDefinition playState, string fieldName)
{
    int separator = fieldName.LastIndexOf("::", StringComparison.Ordinal);
    TypeDefinition type = RequireType(module, fieldName[..separator]);
    string name = fieldName[(separator + 2)..];
    return type.Fields.Single(candidate => candidate.Name == name &&
        candidate.FieldType.FullName == playState.FullName);
}

static void ReplacePlayStateFieldReferences(ModuleDefinition module,
    FieldDefinition field, MethodDefinition recentPlayState, bool replaceWrites,
    Func<MethodDefinition, bool>? methodFilter)
{
    int reads = 0;
    int writes = 0;
    foreach (TypeDefinition type in AllTypes(module.Types))
    {
        foreach (MethodDefinition method in type.Methods.Where(candidate =>
            candidate.HasBody))
        {
            if (methodFilter != null && !methodFilter(method))
                continue;
            IList<Instruction> instructions = method.Body.Instructions;
            for (int index = 0; index < instructions.Count; index++)
            {
                Instruction instruction = instructions[index];
                if (instruction.Operand is not FieldReference reference ||
                    reference.FullName != field.FullName)
                    continue;

                if (instruction.OpCode.Code == Code.Ldfld)
                {
                    RemoveConsumedValues(method, index, 1);
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = recentPlayState;
                    reads++;
                    continue;
                }
                if (instruction.OpCode.Code == Code.Ldsfld)
                {
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = recentPlayState;
                    reads++;
                    continue;
                }
                if (instruction.OpCode.Code == Code.Stfld)
                {
                    if (!replaceWrites)
                        continue;
                    RemoveFieldAssignment(method, index, 2);
                    writes++;
                    continue;
                }
                if (instruction.OpCode.Code == Code.Stsfld)
                {
                    if (!replaceWrites)
                        continue;
                    RemoveFieldAssignment(method, index, 1);
                    writes++;
                    continue;
                }
                throw UnsupportedPlayStateFieldUse(method, instruction);
            }
        }
    }
    Console.WriteLine("Migrated " + field.FullName + ": " + reads +
        " reads, " + writes + " assignments");
}

static InvalidOperationException UnsupportedPlayStateFieldUse(
    MethodDefinition method, Instruction instruction) => new(
        "Unsupported PlayState field use in " + method.FullName + " at IL_" +
        instruction.Offset.ToString("X4") + ": " + instruction);

static void RemoveFieldAssignment(MethodDefinition method, int storeIndex,
    int requiredValues)
{
    RemoveConsumedValues(method, storeIndex, requiredValues);
    MakeNop(method.Body.Instructions[storeIndex]);
}

static void RemoveConsumedValues(MethodDefinition method, int consumerIndex,
    int requiredValues)
{
    IList<Instruction> instructions = method.Body.Instructions;
    int required = requiredValues;
    int start = consumerIndex;
    for (int index = consumerIndex - 1; index >= 0; index--)
    {
        Instruction instruction = instructions[index];
        if (instruction.OpCode.FlowControl is FlowControl.Branch or
            FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw)
            throw UnsupportedPlayStateFieldUse(method, instructions[consumerIndex]);
        required -= PushCount(instruction);
        required += PopCount(instruction);
        if (required == 0)
        {
            start = index;
            break;
        }
        if (required < 0)
            throw UnsupportedPlayStateFieldUse(method, instructions[consumerIndex]);
    }
    if (required != 0)
        throw UnsupportedPlayStateFieldUse(method, instructions[consumerIndex]);
    for (int index = start; index < consumerIndex; index++)
        MakeNop(instructions[index]);
}

static int PushCount(Instruction instruction)
{
    if (instruction.OpCode.Code == Code.Dup)
        return 2;
    if (instruction.OpCode.StackBehaviourPush == StackBehaviour.Varpush)
    {
        if (instruction.OpCode.Code == Code.Newobj)
            return 1;
        if (instruction.Operand is MethodReference method)
            return method.ReturnType.MetadataType == MetadataType.Void ? 0 : 1;
    }
    return instruction.OpCode.StackBehaviourPush switch
    {
        StackBehaviour.Push0 => 0,
        StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or
            StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or
            StackBehaviour.Pushref => 1,
        StackBehaviour.Push1_push1 => 2,
        _ => throw new InvalidOperationException("Unsupported push behavior: " +
            instruction)
    };
}

static int PopCount(Instruction instruction)
{
    if (instruction.OpCode.StackBehaviourPop == StackBehaviour.Varpop)
    {
        if (instruction.Operand is MethodReference method)
            return method.Parameters.Count +
                ((method.HasThis && instruction.OpCode.Code != Code.Newobj) ? 1 : 0);
    }
    return instruction.OpCode.StackBehaviourPop switch
    {
        StackBehaviour.Pop0 => 0,
        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or
            StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or
            StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or
            StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or
            StackBehaviour.Popref_popi_popi8 or
            StackBehaviour.Popref_popi_popr4 or
            StackBehaviour.Popref_popi_popr8 or
            StackBehaviour.Popref_popi_popref => 3,
        _ => throw new InvalidOperationException("Unsupported pop behavior: " +
            instruction)
    };
}

static IEnumerable<TypeDefinition> AllTypes(
    IEnumerable<TypeDefinition> roots)
{
    foreach (TypeDefinition type in roots)
    {
        yield return type;
        foreach (TypeDefinition nested in AllTypes(type.NestedTypes))
            yield return nested;
    }
}

static IEnumerable<string> MigratedPlayStateFields() =>
[
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ArrowRain::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Blizzard::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.BreakBarriers::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ChillyBlast::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Conflagration::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.DeflectionAura::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.DrainLife::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.DrinkBlood::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.EarthQuake::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.EtherealClone::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.FloorStomp::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease/GreaseField::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.GreaseLump::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.GreaseTrail::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.HealingRain::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.HomingCharge::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.MeteorShower::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Napalm::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.PoisonSpray::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Polymorph::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Rain::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.RandomMine::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Revive::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SpawnSlime::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.StopCharge::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonCross::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonDeath::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonFlamer::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonSpirit::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonUndead::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonZombie::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Thunderbolt::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Thunderstorm::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TimeWarp::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TimeWarpStaff::mPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Wave::mPlayState",
    "Magicka.GameLogic.Entities.Bosses.BossFight::mPlayState",
    "Magicka.GameLogic.Entities.EntityManager::mPlayState",
    "Magicka.GameLogic.Entities.EntityStateStorage::mPlayState",
    "Magicka.GameLogic.Entities.TeslaField::mPlaystate",
    "Magicka.GameLogic.Spells.ArcaneBlade::mPlayState",
    "Magicka.GameLogic.Spells.SpellEffects.SpellEffect::mPlayState",
    "Magicka.GameLogic.Spells.UnderGroundAttack::mPlayState",
    "Magicka.GameLogic.UI.IconRenderer::mPlayState",
    "Magicka.GameLogic.UI.SpellWheel::mPlayState",
    "Magicka.Graphics.TutorialManager::mPlayState",
    "Magicka.Levels.ForceField::mPlayState"
];

static IEnumerable<string> RetainedPlayStateFieldsWithAllUsesMigrated() =>
[
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Starfall::sPlayState",
    "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonPhoenix::sPlayState",
    "Magicka.GameLogic.Entities.Bosses.Vlad::mPlayState",
    "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu::sPlayState",
    "Magicka.Graphics.MagickCamera::mPlayState"
];

static IEnumerable<string> RetainedPlayStateFieldsWithReadsMigrated() =>
[
    "Magicka.Levels.GameScene::mPlayState",
    "Magicka.Levels.Level::mPlayState"
];

static IEnumerable<string> EntityPlayStateMethodsMigrated() =>
[
    "System.Void Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal/PortalEntity::Initialize(Magicka.Network.SpawnPortalMessage&)",
    "System.Void Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal/PortalEntity::Initialize(Microsoft.Xna.Framework.Vector3&)",
    "System.Void Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal/PortalEntity::Update(PolygonHead.DataChannel,System.Single)",
    "System.Void Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonDeath/MagickDeath::.ctor(Magicka.GameLogic.GameStates.PlayState)",
    "System.Void Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonDeath/MagickDeath::Deinitialize()",
    "System.Void Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonDeath/MagickDeath::Initialize(Microsoft.Xna.Framework.Matrix&,Magicka.GameLogic.GameStates.PlayState,Magicka.GameLogic.Entities.Character)",
    "System.Void Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonDeath/MagickDeath::Update(PolygonHead.DataChannel,System.Single)"
];

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
