using Mono.Cecil;
using Mono.Cecil.Cil;

const string EntityTypeName = "Magicka.GameLogic.Entities.Entity";
const string PlayStateTypeName = "Magicka.GameLogic.GameStates.PlayState";
const string GameStateTypeName = "Magicka.GameLogic.GameStates.GameState";
const string RegistryTypeName = "Magicka.GcDiagnostics.RetentionRegistry";

if (args.Length == 3
    && args[0] == "--patch-polygon-light-scene-detach")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchPolygonHeadLightSceneDetachOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": repaired Light scene detachment");
    return 0;
}

if (args.Length != 4)
{
    Console.Error.WriteLine(
        "Usage: RetentionPatcher <Magicka.exe> <PolygonHead.dll>"
        + " <Magicka.GcDiagnostics.dll> <output-directory>\n"
        + "   or: RetentionPatcher"
        + " --patch-polygon-light-scene-detach"
        + " <PolygonHead.dll> <output-PolygonHead.dll>");
    return 2;
}

string magickaPath = Path.GetFullPath(args[0]);
string polygonHeadPath = Path.GetFullPath(args[1]);
string diagnosticsPath = Path.GetFullPath(args[2]);
string outputDirectory = Path.GetFullPath(args[3]);

Directory.CreateDirectory(outputDirectory);

using AssemblyDefinition diagnostics = ReadAssembly(diagnosticsPath);
HelperMethods helperMethods = LoadHelperMethods(diagnostics);

PatchReport magickaReport = PatchMagicka(
    magickaPath,
    Path.Combine(outputDirectory, Path.GetFileName(magickaPath)),
    helperMethods);
PatchReport polygonHeadReport = PatchPolygonHead(
    polygonHeadPath,
    Path.Combine(outputDirectory, Path.GetFileName(polygonHeadPath)),
    helperMethods);

Console.WriteLine(magickaReport);
Console.WriteLine(polygonHeadReport);
return 0;

static PatchReport PatchMagicka(
    string inputPath,
    string outputPath,
    HelperMethods sourceHelpers)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    EnsureNotAlreadyPatched(assembly);
    ModuleDefinition module = assembly.MainModule;
    HelperMethods helpers = sourceHelpers.ImportInto(module);
    Dictionary<string, TypeDefinition> types = AllTypes(module)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);

    RepairParadoxStoreWorkerDelegate(module, types);
    RepairClr2CollectionLocks(types);

    int registrations = 0;
    int activeHooks = 0;
    int residentActiveHooks = 0;
    int deactivatedHooks = 0;
    int collectHooks = 0;
    int detachHooks = 0;
    int checkpointHooks = 0;

    TypeDefinition entityType = RequireType(types, EntityTypeName);
    RepairAvatarCacheExpansionBranch(
        RequireMethod(
            RequireType(types, "Magicka.GameLogic.Entities.Avatar"),
            "GetFromCache",
            parameterCount: 1));
    foreach (MethodDefinition constructor in entityType.Methods.Where(
                 method => method.IsConstructor && !method.IsStatic && method.HasBody))
    {
        registrations += InstrumentSelfAtReturns(
            constructor,
            helpers.Register,
            EntityTypeName + "..ctor");
    }

    foreach (TypeDefinition type in types.Values.Where(
                 type => IsSameModuleSubclassOf(type, EntityTypeName, types)))
    {
        foreach (MethodDefinition method in type.Methods.Where(
                     method => !method.IsStatic
                               && method.HasBody
                               && method.ReturnType.FullName == "System.Void"))
        {
            string lifecycle = type.FullName + "." + method.Name;
            if (method.Name == "Initialize")
            {
                activeHooks += InstrumentSelfAtReturns(
                    method,
                    helpers.MarkActive,
                    lifecycle);
            }
            else if (string.Equals(
                         method.Name, "Deinitialize", StringComparison.OrdinalIgnoreCase))
            {
                deactivatedHooks += InstrumentSelfAtEntry(
                    method,
                    helpers.MarkDeactivated,
                    lifecycle);
            }
            else if (method.Name == "Dispose")
            {
                collectHooks += InstrumentSelfAtEntry(
                    method,
                    helpers.MarkMustCollect,
                    lifecycle);
            }
        }
    }

    HashSet<string> poolSinkNames = new HashSet<string>(
        new[] { "ReturnToCache", "AddToCache", "ReturnGib" },
        StringComparer.Ordinal);
    MethodDefinition[] poolSinks = types.Values
        .SelectMany(type => type.Methods)
        .Where(method =>
            method.IsStatic
            && method.HasBody
            && method.ReturnType.FullName == "System.Void"
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName
                == method.DeclaringType.FullName
            && poolSinkNames.Contains(method.Name))
        .OrderBy(method => method.FullName, StringComparer.Ordinal)
        .ToArray();
    if (poolSinks.Length < 8)
    {
        throw new InvalidOperationException(
            "Expected at least eight same-type static pool sinks, found "
            + poolSinks.Length);
    }

    HashSet<string> pooledHolderTypes = new HashSet<string>(
        StringComparer.Ordinal);
    foreach (MethodDefinition method in poolSinks)
    {
        string typeName = method.DeclaringType.FullName;
        pooledHolderTypes.Add(typeName);
        detachHooks += InstrumentFirstArgumentAtReturns(
            method,
            helpers.MarkMustDetach,
            typeName + "." + method.Name);
    }

    int directCacheInsertions = 0;
    foreach (TypeDefinition type in types.Values)
    {
        foreach (MethodDefinition method in type.Methods.Where(
                     method => method.HasBody))
        {
            int hooks = InstrumentCacheInsertionSites(
                method,
                helpers.MarkMustDetach,
                type.FullName + "." + method.Name + ".CacheInsert");
            if (hooks != 0)
            {
                pooledHolderTypes.Add(type.FullName);
                directCacheInsertions += hooks;
                detachHooks += hooks;
            }
        }
    }

    if (directCacheInsertions < 20)
    {
        throw new InvalidOperationException(
            "Expected at least twenty direct instance cache insertions, found "
            + directCacheInsertions);
    }

    string[] residentPoolTypeNames =
    [
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity",
        "Magicka.GameLogic.Entities.SpellMine",
        "Magicka.GameLogic.Entities.MissileEntity",
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease/GreaseField",
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.VortexEntity",
        "Magicka.GameLogic.Entities.Items.Item",
        "Magicka.GameLogic.Entities.SprayEntity",
        "Magicka.GameLogic.Entities.ElementalEgg",
    ];
    HashSet<string> residentPoolTypes = new HashSet<string>(
        residentPoolTypeNames,
        StringComparer.Ordinal);
    foreach (string typeName in residentPoolTypeNames)
    {
        TypeDefinition type = RequireType(types, typeName);
        pooledHolderTypes.Add(typeName);
        _ = RequireMethod(
            type,
            "Deinitialize",
            parameterCount: 0);
    }

    int cacheSourceHooks = 0;
    foreach (string typeName in pooledHolderTypes.OrderBy(
                 name => name,
                 StringComparer.Ordinal))
    {
        TypeDefinition type = RequireType(types, typeName);
        foreach (MethodDefinition method in type.Methods.Where(
                     method => method.IsStatic
                               && method.HasBody
                               && method.ReturnType.FullName == type.FullName
                               && ReadsCacheOrPoolField(method)))
        {
            bool isResidentPool = residentPoolTypes.Contains(type.FullName);
            int hooks = InstrumentReturnValueAtReturns(
                method,
                isResidentPool
                    ? helpers.MarkResidentActive
                    : helpers.MarkActive,
                type.FullName + "." + method.Name);
            cacheSourceHooks += hooks;
            if (isResidentPool)
            {
                residentActiveHooks += hooks;
            }
            else
            {
                activeHooks += hooks;
            }
        }

        if (IsSameModuleSubclassOf(type, EntityTypeName, types))
        {
            continue;
        }

        foreach (MethodDefinition method in type.Methods.Where(
                     method => !method.IsStatic
                               && method.HasBody
                               && IsReuseEntryMethod(method.Name)))
        {
            activeHooks += InstrumentSelfAtEntry(
                method,
                helpers.MarkActive,
                type.FullName + "." + method.Name);
        }
    }

    const string avatarTypeName = "Magicka.GameLogic.Entities.Avatar";
    MethodDefinition avatarMissileSource = RequireMethod(
        RequireType(types, avatarTypeName),
        "GetMissileInstance",
        parameterCount: 0);
    if (avatarMissileSource.ReturnType.FullName
        != "Magicka.GameLogic.Entities.MissileEntity")
    {
        throw new InvalidOperationException(
            "Unexpected Avatar.GetMissileInstance return type: "
            + avatarMissileSource.ReturnType.FullName);
    }

    int avatarMissileHooks = InstrumentReturnValueAtReturns(
        avatarMissileSource,
        helpers.MarkResidentActive,
        avatarTypeName + ".GetMissileInstance");
    cacheSourceHooks += avatarMissileHooks;
    residentActiveHooks += avatarMissileHooks;

    if (cacheSourceHooks == 0)
    {
        throw new InvalidOperationException(
            "Expected at least one cache-source return hook.");
    }

    const string cthulhuMistTypeName =
        "Magicka.GameLogic.Entities.Bosses.CthulhuMist";
    deactivatedHooks += InstrumentSelfAtEntry(
        RequireMethod(
            RequireType(types, cthulhuMistTypeName),
            "Deactivate",
            parameterCount: 0),
        helpers.MarkDeactivated,
        cthulhuMistTypeName + ".Deactivate");

    const string itemTypeName = "Magicka.GameLogic.Entities.Items.Item";
    MethodDefinition itemReinitialize = RequireMethod(
        RequireType(types, itemTypeName),
        "Reinitialize",
        parameterCount: 1);
    activeHooks += InstrumentSelfAtReturns(
        itemReinitialize,
        helpers.MarkActive,
        itemTypeName + ".Reinitialize");

    string[] complexTypes =
    [
        PlayStateTypeName,
        "Magicka.Levels.Level",
        "Magicka.Levels.GameScene",
        "Magicka.Levels.LevelModel",
        "Magicka.GameLogic.Entities.CharacterTemplate",
        "Magicka.GameLogic.Entities.PhysicsEntityTemplate",
    ];

    foreach (string typeName in complexTypes)
    {
        TypeDefinition type = RequireType(types, typeName);
        foreach (MethodDefinition constructor in type.Methods.Where(
                     method => method.IsConstructor
                               && !method.IsStatic
                               && method.HasBody))
        {
            if (typeName == PlayStateTypeName)
            {
                registrations += InstrumentSelfAfterBaseConstructor(
                    constructor,
                    helpers.BeginEpoch,
                    PlayStateTypeName + "..ctor");
            }
            else
            {
                registrations += InstrumentSelfAtReturns(
                    constructor,
                    helpers.Register,
                    typeName + "..ctor");
            }
        }

        foreach (MethodDefinition dispose in type.Methods.Where(
                     method => method.Name == "Dispose"
                               && !method.IsStatic
                               && method.HasBody
                               && method.ReturnType.FullName == "System.Void"))
        {
            collectHooks += InstrumentSelfAtEntry(
                dispose,
                helpers.MarkMustCollect,
                typeName + ".Dispose");
        }
    }

    TypeDefinition playState = RequireType(types, PlayStateTypeName);
    MethodDefinition playStateDispose = RequireMethod(
        playState,
        "Dispose",
        parameterCount: 0);
    TypeDefinition gameState = RequireType(types, GameStateTypeName);
    MethodDefinition sceneGetter = RequireMethod(
        gameState,
        "get_Scene",
        parameterCount: 0);
    collectHooks += InstrumentRelatedAtEntry(
        playStateDispose,
        module.ImportReference(sceneGetter),
        helpers.MarkMustCollect,
        PlayStateTypeName + ".Dispose.Scene");
    checkpointHooks += InstrumentCheckpointAtReturns(
        playStateDispose,
        helpers.Checkpoint,
        PlayStateTypeName + ".Dispose");

    WriteAssembly(assembly, outputPath);
    return new PatchReport(
        Path.GetFileName(inputPath),
        registrations,
        activeHooks,
        residentActiveHooks,
        collectHooks,
        deactivatedHooks,
        detachHooks,
        checkpointHooks);
}

static PatchReport PatchPolygonHead(
    string inputPath,
    string outputPath,
    HelperMethods sourceHelpers)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    EnsureNotAlreadyPatched(assembly);
    ModuleDefinition module = assembly.MainModule;
    HelperMethods helpers = sourceHelpers.ImportInto(module);
    Dictionary<string, TypeDefinition> types = AllTypes(module)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);

    RepairLightSceneDetach(types);

    int registrations = 0;
    int collectHooks = 0;
    TypeDefinition biTreeModel = RequireType(
        types,
        "PolygonHead.Models.BiTreeModel");
    foreach (MethodDefinition constructor in biTreeModel.Methods.Where(
                 method => method.IsConstructor && !method.IsStatic && method.HasBody))
    {
        registrations += InstrumentSelfAtReturns(
            constructor,
            helpers.Register,
            "PolygonHead.Models.BiTreeModel..ctor");
    }

    foreach (MethodDefinition dispose in biTreeModel.Methods.Where(
                 method => method.Name == "Dispose"
                           && !method.IsStatic
                           && method.HasBody
                           && method.ReturnType.FullName == "System.Void"))
    {
        collectHooks += InstrumentSelfAtEntry(
            dispose,
            helpers.MarkMustCollect,
            "PolygonHead.Models.BiTreeModel.Dispose");
    }

    WriteAssembly(assembly, outputPath);
    return new PatchReport(
        Path.GetFileName(inputPath),
        registrations,
        ActiveHooks: 0,
        ResidentActiveHooks: 0,
        collectHooks,
        DeactivatedHooks: 0,
        DetachHooks: 0,
        CheckpointHooks: 0);
}

static void PatchPolygonHeadLightSceneDetachOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairLightSceneDetach(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairLightSceneDetach(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition light = RequireType(types, "PolygonHead.Lights.Light");
    TypeDefinition scene = RequireType(types, "PolygonHead.Scene");
    FieldDefinition sceneField = light.Fields.Single(
        field => field.Name == "mScene"
                 && field.FieldType.FullName == scene.FullName);
    MethodDefinition onRemove = RequireMethod(
        light,
        "OnRemove",
        parameterCount: 0);
    MethodDefinition disable = RequireMethod(
        light,
        "Disable",
        parameterCount: 2);
    MethodDefinition update = RequireMethod(
        light,
        "Update",
        parameterCount: 4);
    MethodDefinition removeLight = RequireMethod(
        scene,
        "RemoveLight",
        parameterCount: 1);

    if (!onRemove.HasBody
        || onRemove.Body.Instructions.Count != 1
        || onRemove.Body.Instructions[0].OpCode != OpCodes.Ret)
    {
        throw new InvalidOperationException(
            "Expected an empty PolygonHead.Lights.Light.OnRemove method.");
    }

    RemoveSceneRemovalAfterOnRemove(
        disable,
        onRemove,
        sceneField,
        removeLight);
    RemoveSceneRemovalAfterOnRemove(
        update,
        onRemove,
        sceneField,
        removeLight);

    MethodBody body = onRemove.Body;
    body.Instructions.Clear();
    body.ExceptionHandlers.Clear();
    body.Variables.Clear();
    body.InitLocals = true;
    body.MaxStackSize = 2;

    VariableDefinition oldScene = new VariableDefinition(scene);
    body.Variables.Add(oldScene);
    ILProcessor processor = body.GetILProcessor();
    Instruction returnInstruction = Instruction.Create(OpCodes.Ret);
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, sceneField));
    processor.Append(Instruction.Create(OpCodes.Stloc, oldScene));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Stfld, sceneField));
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldScene));
    processor.Append(Instruction.Create(OpCodes.Brfalse_S, returnInstruction));
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldScene));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, removeLight));
    processor.Append(returnInstruction);
}

static void RemoveSceneRemovalAfterOnRemove(
    MethodDefinition method,
    MethodDefinition onRemove,
    FieldDefinition sceneField,
    MethodDefinition removeLight)
{
    Instruction[] instructions = method.Body.Instructions.ToArray();
    List<Instruction[]> matches = new List<Instruction[]>();
    for (int index = 0; index + 4 < instructions.Length; index++)
    {
        if (!IsMethodCall(instructions[index], onRemove)
            || instructions[index + 1].OpCode != OpCodes.Ldarg_0
            || !IsFieldLoad(instructions[index + 2], sceneField)
            || instructions[index + 3].OpCode != OpCodes.Ldarg_0
            || !IsMethodCall(instructions[index + 4], removeLight))
        {
            continue;
        }

        matches.Add(
        [
            instructions[index + 1],
            instructions[index + 2],
            instructions[index + 3],
            instructions[index + 4],
        ]);
    }

    if (matches.Count != 1)
    {
        throw new InvalidOperationException(
            "Expected one post-OnRemove scene removal in "
            + method.FullName + ", found " + matches.Count + ".");
    }

    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction instruction in matches[0].Reverse())
    {
        processor.Remove(instruction);
    }
}

static bool IsMethodCall(
    Instruction instruction,
    MethodDefinition expected)
{
    return (instruction.OpCode == OpCodes.Call
            || instruction.OpCode == OpCodes.Callvirt)
           && instruction.Operand is MethodReference called
           && called.FullName == expected.FullName;
}

static bool IsFieldLoad(
    Instruction instruction,
    FieldDefinition expected)
{
    return instruction.OpCode == OpCodes.Ldfld
           && instruction.Operand is FieldReference field
           && field.FullName == expected.FullName;
}

static AssemblyDefinition ReadAssembly(string path)
{
    return AssemblyDefinition.ReadAssembly(
        path,
        new ReaderParameters
        {
            InMemory = true,
            ReadSymbols = false,
        });
}

static void WriteAssembly(AssemblyDefinition assembly, string outputPath)
{
    string temporaryPath = outputPath + ".tmp";
    if (File.Exists(temporaryPath))
    {
        File.Delete(temporaryPath);
    }

    assembly.Write(
        temporaryPath,
        new WriterParameters { WriteSymbols = false });
    File.Move(temporaryPath, outputPath, overwrite: true);
}

static void EnsureNotAlreadyPatched(AssemblyDefinition assembly)
{
    if (assembly.MainModule.AssemblyReferences.Any(
            reference => reference.Name == "Magicka.GcDiagnostics"))
    {
        throw new InvalidOperationException(
            assembly.Name.Name + " is already instrumented.");
    }
}

static TypeDefinition RequireType(
    IReadOnlyDictionary<string, TypeDefinition> types,
    string fullName)
{
    if (!types.TryGetValue(fullName, out TypeDefinition? type))
    {
        throw new InvalidOperationException("Required type not found: " + fullName);
    }

    return type;
}

static MethodDefinition RequireMethod(
    TypeDefinition type,
    string name,
    int parameterCount)
{
    MethodDefinition[] methods = type.Methods.Where(
            method => method.Name == name
                      && method.Parameters.Count == parameterCount)
        .ToArray();
    if (methods.Length != 1)
    {
        throw new InvalidOperationException(
            $"Expected one {type.FullName}.{name} method, found {methods.Length}.");
    }

    return methods[0];
}

static bool IsSameModuleSubclassOf(
    TypeDefinition type,
    string baseTypeName,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeReference? current = type;
    while (current is not null)
    {
        if (current.FullName == baseTypeName)
        {
            return true;
        }

        if (!types.TryGetValue(current.FullName, out TypeDefinition? definition))
        {
            return false;
        }

        current = definition.BaseType;
    }

    return false;
}

static int InstrumentCacheInsertionSites(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    Instruction[] originalInstructions = method.Body.Instructions.ToArray();
    List<Instruction> cacheCalls = new List<Instruction>();

    for (int index = 0; index < originalInstructions.Length; index++)
    {
        Instruction instruction = originalInstructions[index];
        if (instruction.Operand is not MethodReference called
            || (called.Name != "Add"
                && called.Name != "Enqueue"
                && called.Name != "Push"))
        {
            continue;
        }

        if (MatchesOwnerCacheInsertion(
                originalInstructions,
                index,
                method))
        {
            cacheCalls.Add(instruction);
        }
    }

    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction cacheCall in cacheCalls)
    {
        Instruction cursor = cacheCall;
        Instruction loadSelf = Instruction.Create(OpCodes.Ldarg_0);
        processor.InsertAfter(cursor, loadSelf);
        cursor = loadSelf;
        Instruction loadLifecycle = Instruction.Create(OpCodes.Ldstr, lifecycle);
        processor.InsertAfter(cursor, loadLifecycle);
        cursor = loadLifecycle;
        processor.InsertAfter(cursor, Instruction.Create(OpCodes.Call, helper));
    }

    if (cacheCalls.Count != 0)
    {
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 3);
    }

    return cacheCalls.Count;
}

static bool MatchesOwnerCacheInsertion(
    IReadOnlyList<Instruction> instructions,
    int callIndex,
    MethodDefinition method)
{
    if (callIndex < 2)
    {
        return false;
    }

    Instruction directCacheLoad = instructions[callIndex - 2];
    Instruction directOwnerLoad = instructions[callIndex - 1];
    if (directCacheLoad.OpCode == OpCodes.Ldsfld
        && directCacheLoad.Operand is FieldReference directCacheField
        && IsCacheOrPoolField(directCacheField.Name)
        && CacheElementMatchesOwner(
            directCacheField,
            method.DeclaringType)
        && directOwnerLoad.OpCode == OpCodes.Ldarg_0)
    {
        return true;
    }

    if (!method.IsStatic
        || method.Parameters.Count == 0
        || method.Parameters[0].ParameterType.FullName
            != method.DeclaringType.FullName)
    {
        return false;
    }

    int firstIndex = Math.Max(0, callIndex - 8);
    int cacheLoadIndex = -1;
    int capturedOwnerIndex = -1;
    for (int index = firstIndex; index < callIndex; index++)
    {
        Instruction instruction = instructions[index];
        if (instruction.OpCode == OpCodes.Ldsfld
            && instruction.Operand is FieldReference cacheField
            && IsCacheOrPoolField(cacheField.Name)
            && CacheElementMatchesOwner(
                cacheField,
                method.DeclaringType))
        {
            cacheLoadIndex = index;
        }

        if (instruction.OpCode == OpCodes.Ldfld
            && instruction.Operand is FieldReference capturedField
            && capturedField.FieldType.FullName
                == method.DeclaringType.FullName)
        {
            capturedOwnerIndex = index;
        }
    }

    return cacheLoadIndex >= firstIndex
           && capturedOwnerIndex > cacheLoadIndex
           && capturedOwnerIndex < callIndex;
}

static bool IsCacheOrPoolField(string fieldName)
{
    return (fieldName.Contains("cache", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("pool", StringComparison.OrdinalIgnoreCase))
           && !fieldName.Contains("active", StringComparison.OrdinalIgnoreCase);
}

static bool CacheElementMatchesOwner(
    FieldReference field,
    TypeDefinition owner)
{
    GenericInstanceType? collection = field.FieldType as GenericInstanceType;
    return collection is not null
           && collection.GenericArguments.Any(
               argument => argument.FullName == owner.FullName);
}

static bool ReadsCacheOrPoolField(MethodDefinition method)
{
    return method.Body.Instructions.Any(
        instruction => (instruction.OpCode == OpCodes.Ldsfld
                        || instruction.OpCode == OpCodes.Ldsflda)
                       && instruction.Operand is FieldReference field
                       && IsCacheOrPoolField(field.Name));
}

static void RepairAvatarCacheExpansionBranch(MethodDefinition method)
{
    Instruction[] unresolvedShortBranches = method.Body.Instructions.Where(
            instruction => instruction.OpCode == OpCodes.Br_S
                           && instruction.Operand is null)
        .ToArray();
    if (unresolvedShortBranches.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one overflowed short branch in " + method.FullName
            + ", found " + unresolvedShortBranches.Length + ".");
    }

    Instruction[] commonExitBranches = method.Body.Instructions.Where(
            instruction => instruction.OpCode == OpCodes.Br_S
                           && instruction.Operand is Instruction target
                           && target.OpCode == OpCodes.Leave_S)
        .ToArray();
    if (commonExitBranches.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one resolved short branch to the common exit in "
            + method.FullName + ", found " + commonExitBranches.Length + ".");
    }

    unresolvedShortBranches[0].OpCode = OpCodes.Br;
    unresolvedShortBranches[0].Operand = commonExitBranches[0].Operand;
}

static void RepairParadoxStoreWorkerDelegate(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition guards = RequireType(
        types,
        "Magicka.CommunityPatch.RuntimeCompatibilityGuards");
    MethodDefinition queue = RequireMethod(
        guards,
        "QueueParadoxStorePriceUpdate",
        parameterCount: 1);
    MethodDefinition worker = RequireMethod(
        guards,
        "RunParadoxStorePriceUpdate",
        parameterCount: 1);
    MethodDefinition update = RequireMethod(
        RequireType(
            types,
            "Magicka.CoreFramework.GameSystem.Store.StoreItemDatabase"),
        "UpdateParadoxItems",
        parameterCount: 0);

    if (queue.Parameters[0].ParameterType.FullName != "System.Action")
    {
        throw new InvalidOperationException(
            "Unexpected Paradox price worker delegate: "
            + queue.Parameters[0].ParameterType.FullName);
    }

    AssemblyNameReference mscorlib20 = module.AssemblyReferences.Single(
        reference => reference.Name == "mscorlib"
                     && reference.Version == new Version(2, 0, 0, 0));
    TypeReference threadStart = new TypeReference(
        "System.Threading",
        "ThreadStart",
        module,
        mscorlib20,
        valueType: false);

    Instruction workerCast = worker.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Castclass
        && instruction.Operand is TypeReference type
        && type.FullName == "System.Action");
    Instruction workerInvoke = worker.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Callvirt
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "System.Action"
        && called.Name == "Invoke");
    Instruction actionConstructor = update.Body.Instructions.Single(
        instruction => instruction.OpCode == OpCodes.Newobj
                       && instruction.Operand is MethodReference called
                       && called.DeclaringType.FullName == "System.Action");
    Instruction queueCall = update.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == guards.FullName
        && called.Name == queue.Name);

    queue.Parameters[0].ParameterType = threadStart;
    workerCast.Operand = threadStart;
    workerInvoke.Operand = CreateDelegateMethod(
        module,
        threadStart,
        (MethodReference)workerInvoke.Operand);
    actionConstructor.Operand = CreateDelegateMethod(
        module,
        threadStart,
        (MethodReference)actionConstructor.Operand);
    ((MethodReference)queueCall.Operand).Parameters[0].ParameterType = threadStart;
}

static MethodReference CreateDelegateMethod(
    ModuleDefinition module,
    TypeReference delegateType,
    MethodReference source)
{
    MethodReference replacement = new MethodReference(
        source.Name,
        module.ImportReference(source.ReturnType),
        delegateType)
    {
        HasThis = source.HasThis,
        ExplicitThis = source.ExplicitThis,
        CallingConvention = source.CallingConvention,
    };
    foreach (ParameterDefinition parameter in source.Parameters)
    {
        replacement.Parameters.Add(new ParameterDefinition(
            module.ImportReference(parameter.ParameterType)));
    }

    return replacement;
}

static void RepairClr2CollectionLocks(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    (string TypeName, string MethodName, int ParameterCount)[] lockMethods =
    [
        ("Magicka.StaticList`1", "Add", 1),
        ("Magicka.StaticList`1", "Insert", 2),
        ("Magicka.StaticWeakList`1", "Add", 1),
        ("Magicka.StaticWeakList`1", "Insert", 2),
        ("Magicka.StaticWeakList`1", "Expand", 1),
    ];
    foreach ((string typeName, string methodName, int parameterCount) in lockMethods)
    {
        TypeDefinition collection = RequireType(types, typeName);
        MethodDefinition method = RequireMethod(
            collection,
            methodName,
            parameterCount);
        Instruction enterCall = method.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Threading.Monitor"
            && called.Name == "Enter"
            && called.Parameters.Count == 2);
        Instruction loadTakenAddress = enterCall.Previous;
        if ((loadTakenAddress.OpCode != OpCodes.Ldloca
             && loadTakenAddress.OpCode != OpCodes.Ldloca_S)
            || loadTakenAddress.Operand is not VariableDefinition taken)
        {
            throw new InvalidOperationException(
                "Expected a lock-taken local before Monitor.Enter in "
                + method.FullName);
        }

        MethodReference oldEnter = (MethodReference)enterCall.Operand;
        MethodReference clr2Enter = new MethodReference(
            oldEnter.Name,
            oldEnter.ReturnType,
            oldEnter.DeclaringType)
        {
            HasThis = false,
            CallingConvention = MethodCallingConvention.Default,
        };
        clr2Enter.Parameters.Add(new ParameterDefinition(
            oldEnter.Parameters[0].ParameterType));

        ILProcessor processor = method.Body.GetILProcessor();
        processor.Remove(loadTakenAddress);
        enterCall.Operand = clr2Enter;
        Instruction acquired = Instruction.Create(OpCodes.Ldc_I4_1);
        processor.InsertAfter(enterCall, acquired);
        processor.InsertAfter(acquired, Instruction.Create(OpCodes.Stloc, taken));
    }
}

static bool IsReuseEntryMethod(string methodName)
{
    return methodName == "Execute"
           || methodName == "Reinitialize"
           || methodName.StartsWith(
               "Cast",
               StringComparison.Ordinal)
           || methodName.StartsWith(
               "Initialize",
               StringComparison.Ordinal);
}

static int InstrumentReturnValueAtReturns(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    return InstrumentReturns(
        method,
        ret =>
        [
            Instruction.Create(OpCodes.Dup),
            Instruction.Create(OpCodes.Ldstr, lifecycle),
            Instruction.Create(OpCodes.Call, helper),
        ]);
}

static int InstrumentSelfAtEntry(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    if (!method.HasBody || method.Body.Instructions.Count == 0)
    {
        throw new InvalidOperationException(
            "Cannot instrument empty method " + method.FullName);
    }

    ILProcessor processor = method.Body.GetILProcessor();
    Instruction first = method.Body.Instructions[0];
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, lifecycle));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Call, helper));
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 3);
    return 1;
}

static int InstrumentSelfAtReturns(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    return InstrumentReturns(
        method,
        ret =>
        [
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, lifecycle),
            Instruction.Create(OpCodes.Call, helper),
        ]);
}

static int InstrumentFirstArgumentAtReturns(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    return InstrumentReturns(
        method,
        ret =>
        [
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, lifecycle),
            Instruction.Create(OpCodes.Call, helper),
        ]);
}

static int InstrumentRelatedAtEntry(
    MethodDefinition method,
    MethodReference getter,
    MethodReference helper,
    string lifecycle)
{
    if (!method.HasBody || method.Body.Instructions.Count == 0)
    {
        throw new InvalidOperationException(
            "Cannot instrument empty method " + method.FullName);
    }

    ILProcessor processor = method.Body.GetILProcessor();
    Instruction first = method.Body.Instructions[0];
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Call, getter));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, lifecycle));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Call, helper));
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 4);
    return 1;
}

static int InstrumentCheckpointAtReturns(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    return InstrumentReturns(
        method,
        ret =>
        [
            Instruction.Create(OpCodes.Ldstr, lifecycle),
            Instruction.Create(OpCodes.Call, helper),
        ]);
}

static int InstrumentSelfAfterBaseConstructor(
    MethodDefinition constructor,
    MethodReference helper,
    string lifecycle)
{
    Instruction? baseCall = constructor.Body.Instructions.FirstOrDefault(
        instruction => instruction.OpCode == OpCodes.Call
                       && instruction.Operand is MethodReference called
                       && called.Name == ".ctor"
                       && called.DeclaringType.FullName
                           != constructor.DeclaringType.FullName);
    if (baseCall is null)
    {
        throw new InvalidOperationException(
            "Base constructor call not found in " + constructor.FullName);
    }

    ILProcessor processor = constructor.Body.GetILProcessor();
    Instruction loadThis = Instruction.Create(OpCodes.Ldarg_0);
    Instruction loadLifecycle = Instruction.Create(OpCodes.Ldstr, lifecycle);
    Instruction callHelper = Instruction.Create(OpCodes.Call, helper);
    processor.InsertAfter(baseCall, loadThis);
    processor.InsertAfter(loadThis, loadLifecycle);
    processor.InsertAfter(loadLifecycle, callHelper);
    constructor.Body.MaxStackSize = Math.Max(constructor.Body.MaxStackSize, 2);
    return 1;
}

static int InstrumentReturns(
    MethodDefinition method,
    Func<Instruction, IReadOnlyList<Instruction>> createInstructions)
{
    Instruction[] returns = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ret)
        .ToArray();
    if (returns.Length == 0)
    {
        throw new InvalidOperationException(
            "No return instruction found in " + method.FullName);
    }

    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction originalReturn in returns)
    {
        IReadOnlyList<Instruction> injected = createInstructions(originalReturn);
        if (injected.Count == 0)
        {
            continue;
        }

        originalReturn.OpCode = injected[0].OpCode;
        originalReturn.Operand = injected[0].Operand;
        Instruction previous = originalReturn;
        for (int index = 1; index < injected.Count; index++)
        {
            processor.InsertAfter(previous, injected[index]);
            previous = injected[index];
        }

        Instruction replacementReturn = Instruction.Create(OpCodes.Ret);
        processor.InsertAfter(previous, replacementReturn);
    }

    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 4);
    return returns.Length;
}

static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
{
    return module.Types.SelectMany(Flatten);
}

static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
{
    yield return type;
    foreach (TypeDefinition nested in type.NestedTypes)
    {
        foreach (TypeDefinition descendant in Flatten(nested))
        {
            yield return descendant;
        }
    }
}

static HelperMethods LoadHelperMethods(AssemblyDefinition diagnostics)
{
    TypeDefinition registry = diagnostics.MainModule.GetType(RegistryTypeName)
        ?? throw new InvalidOperationException(
            "Runtime registry type not found: " + RegistryTypeName);
    return new HelperMethods(
        RequireRuntimeMethod(registry, "BeginEpoch"),
        RequireRuntimeMethod(registry, "Register"),
        RequireRuntimeMethod(registry, "MarkActive"),
        RequireRuntimeMethod(registry, "MarkResidentActive"),
        RequireRuntimeMethod(registry, "MarkDeactivated"),
        RequireRuntimeMethod(registry, "MarkMustCollect"),
        RequireRuntimeMethod(registry, "MarkMustDetach"),
        RequireRuntimeMethod(registry, "Checkpoint"));
}

static MethodDefinition RequireRuntimeMethod(
    TypeDefinition registry,
    string name)
{
    MethodDefinition[] methods = registry.Methods.Where(
            method => method.Name == name)
        .ToArray();
    if (methods.Length != 1)
    {
        throw new InvalidOperationException(
            $"Expected one runtime method {name}, found {methods.Length}.");
    }

    return methods[0];
}

sealed record HelperMethods(
    MethodReference BeginEpoch,
    MethodReference Register,
    MethodReference MarkActive,
    MethodReference MarkResidentActive,
    MethodReference MarkDeactivated,
    MethodReference MarkMustCollect,
    MethodReference MarkMustDetach,
    MethodReference Checkpoint)
{
    public HelperMethods ImportInto(ModuleDefinition module)
    {
        return new HelperMethods(
            module.ImportReference(BeginEpoch),
            module.ImportReference(Register),
            module.ImportReference(MarkActive),
            module.ImportReference(MarkResidentActive),
            module.ImportReference(MarkDeactivated),
            module.ImportReference(MarkMustCollect),
            module.ImportReference(MarkMustDetach),
            module.ImportReference(Checkpoint));
    }
}

sealed record PatchReport(
    string Assembly,
    int Registrations,
    int ActiveHooks,
    int ResidentActiveHooks,
    int CollectHooks,
    int DeactivatedHooks,
    int DetachHooks,
    int CheckpointHooks)
{
    public override string ToString()
    {
        return $"{Assembly}: register={Registrations}, active={ActiveHooks},"
               + $" resident-active={ResidentActiveHooks},"
               + $" collect={CollectHooks}, deactivated={DeactivatedHooks},"
               + $" detach={DetachHooks},"
               + $" checkpoint={CheckpointHooks}";
    }
}
