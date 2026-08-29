using Mono.Cecil;
using Mono.Cecil.Cil;

const string EntityTypeName = "Magicka.GameLogic.Entities.Entity";
const string PlayStateTypeName = "Magicka.GameLogic.GameStates.PlayState";
const string GameStateTypeName = "Magicka.GameLogic.GameStates.GameState";
const string RegistryTypeName = "Magicka.GcDiagnostics.RetentionRegistry";

if (args.Length == 3
    && args[0] == "--patch-character-template-static-caches")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchCharacterTemplateStaticCachesOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": released static CharacterTemplate caches");
    return 0;
}

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

if (args.Length == 3
    && args[0] == "--patch-warlord-ability-diagnostic")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchWarlordAbilityDiagnosticOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": added Warlord primary-ability diagnostic");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-railgun-parent-cycle")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchRailgunParentCycleOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": prevented Railgun parent cycles");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-jormungandr-null-target")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchJormungandrNullTargetOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": guarded Jormungandr emergence without a target");
    return 0;
}

if (args.Length != 4)
{
    Console.Error.WriteLine(
        "Usage: RetentionPatcher <Magicka.exe> <PolygonHead.dll>"
        + " <Magicka.GcDiagnostics.dll> <output-directory>\n"
        + "   or: RetentionPatcher"
        + " --patch-character-template-static-caches"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-polygon-light-scene-detach"
        + " <PolygonHead.dll> <output-PolygonHead.dll>\n"
        + "   or: RetentionPatcher"
        + " --patch-warlord-ability-diagnostic"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-railgun-parent-cycle"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-jormungandr-null-target"
        + " <Magicka.exe> <output-Magicka.exe>");
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
    RepairCharacterTemplateStaticCaches(module, types);
    PatchWarlordAbilityDiagnostic(module, types);
    RepairRailgunParentCycles(module, types);
    RepairJormungandrNullTarget(types);

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

static void PatchCharacterTemplateStaticCachesOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairCharacterTemplateStaticCaches(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void PatchWarlordAbilityDiagnosticOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    PatchWarlordAbilityDiagnostic(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void PatchRailgunParentCycleOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairRailgunParentCycles(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void PatchJormungandrNullTargetOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairJormungandrNullTarget(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairJormungandrNullTarget(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition jormungandr = RequireType(
        types,
        "Magicka.GameLogic.Entities.Bosses.Jormungandr");
    TypeDefinition undergroundState = RequireType(
        types,
        "Magicka.GameLogic.Entities.Bosses.Jormungandr/UndergroundState");
    FieldDefinition targetField = jormungandr.Fields.Single(field =>
        field.Name == "mTarget"
        && !field.IsStatic
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.Character");
    MethodDefinition selectTarget = RequireMethod(
        jormungandr,
        "SelectTarget",
        parameterCount: 1);
    MethodDefinition update = RequireMethod(
        undergroundState,
        "OnUpdate",
        parameterCount: 2);

    Instruction[] instructions = update.Body.Instructions.ToArray();
    Instruction[] targetSelections = instructions.Where(instruction =>
            IsMethodCall(instruction, selectTarget))
        .ToArray();
    if (targetSelections.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one Jormungandr underground target selection, found "
            + targetSelections.Length + ".");
    }

    Instruction selection = targetSelections[0];
    Instruction continueInstruction = selection.Next
        ?? throw new InvalidOperationException(
            "Jormungandr target selection has no following instruction.");
    if (continueInstruction.OpCode != OpCodes.Ldc_R4
        || continueInstruction.Operand is not float warningStrength
        || warningStrength != 0.5f)
    {
        throw new InvalidOperationException(
            "Unexpected Jormungandr post-selection instruction; the null-target"
            + " guard may already exist or the game method changed.");
    }

    ILProcessor processor = update.Body.GetILProcessor();
    processor.InsertBefore(
        continueInstruction,
        Instruction.Create(OpCodes.Ldarg_2));
    processor.InsertBefore(
        continueInstruction,
        Instruction.Create(OpCodes.Ldfld, targetField));
    processor.InsertBefore(
        continueInstruction,
        Instruction.Create(OpCodes.Brtrue, continueInstruction));
    processor.InsertBefore(
        continueInstruction,
        Instruction.Create(OpCodes.Ret));
    update.Body.MaxStackSize = Math.Max(update.Body.MaxStackSize, 1);
}

static void RepairRailgunParentCycles(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    const string cycleCheckName =
        "CommunityPatchWouldCreateParentCycle";
    const string reportName =
        "CommunityPatchReportParentCycleRecovery";

    TypeDefinition railgun = RequireType(
        types,
        "Magicka.GameLogic.Spells.Railgun");
    if (railgun.Methods.Any(method =>
            method.Name == cycleCheckName
            || method.Name == reportName))
    {
        throw new InvalidOperationException(
            "Railgun parent-cycle repair already exists.");
    }

    FieldDefinition parentsField = railgun.Fields.Single(field =>
        field.Name == "mParents"
        && field.FieldType.FullName
            == "System.Collections.Generic.List`1<"
               + railgun.FullName + ">");
    FieldDefinition lockTraversalActive = new FieldDefinition(
        "mCommunityPatchLockAllActive",
        FieldAttributes.Private,
        module.TypeSystem.Boolean);
    railgun.Fields.Add(lockTraversalActive);
    TypeDefinition patchTelemetry = RequireType(
        types,
        "Magicka.CommunityPatch.PatchTelemetry");
    MethodDefinition sendRuntimeGuard = RequireMethod(
        patchTelemetry,
        "SendRuntimeGuard",
        parameterCount: 6);

    MethodReference getParentCount = FindMethodReference(
        module,
        parentsField.FieldType.FullName,
        "get_Count",
        parameterCount: 0,
        returnType: "System.Int32");
    MethodReference getParentItem = railgun.Methods
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Where(method =>
            method.DeclaringType.FullName == parentsField.FieldType.FullName
            && method.Name == "get_Item"
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName == "System.Int32")
        .GroupBy(method => method.FullName, StringComparer.Ordinal)
        .Select(group => group.First())
        .Single();
    MethodReference referenceEquals = FindMethodReference(
        module,
        "System.Object",
        "ReferenceEquals",
        parameterCount: 2,
        returnType: "System.Boolean");
    MethodReference stringBuilderConstructor = FindMethodReference(
        module,
        "System.Text.StringBuilder",
        ".ctor",
        parameterCount: 0,
        returnType: "System.Void");
    MethodReference appendString = FindMethodReference(
        module,
        "System.Text.StringBuilder",
        "Append",
        parameterCount: 1,
        returnType: "System.Text.StringBuilder",
        parameterType: "System.String");
    TypeReference stringBuilderType = appendString.DeclaringType;
    MethodReference appendInteger = CreateInstanceMethodReference(
        "Append",
        stringBuilderType,
        stringBuilderType,
        module.TypeSystem.Int32);
    MethodReference stringBuilderToString = CreateInstanceMethodReference(
        "ToString",
        stringBuilderType,
        module.TypeSystem.String);

    MethodDefinition reportRecovery = new MethodDefinition(
        reportName,
        MethodAttributes.Private
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        module.TypeSystem.Void);
    reportRecovery.Parameters.Add(new ParameterDefinition(
        "reason",
        ParameterAttributes.None,
        module.TypeSystem.String));
    reportRecovery.Parameters.Add(new ParameterDefinition(
        "visitedCount",
        ParameterAttributes.None,
        module.TypeSystem.Int32));
    reportRecovery.Parameters.Add(new ParameterDefinition(
        "pendingCount",
        ParameterAttributes.None,
        module.TypeSystem.Int32));
    reportRecovery.Parameters.Add(new ParameterDefinition(
        "candidateParentCount",
        ParameterAttributes.None,
        module.TypeSystem.Int32));
    railgun.Methods.Add(reportRecovery);

    VariableDefinition details = new VariableDefinition(stringBuilderType);
    reportRecovery.Body.Variables.Add(details);
    reportRecovery.Body.InitLocals = true;
    ILProcessor reportProcessor = reportRecovery.Body.GetILProcessor();
    Instruction reportTryStart = Instruction.Create(OpCodes.Nop);
    Instruction reportHandlerStart = Instruction.Create(OpCodes.Pop);
    Instruction reportReturn = Instruction.Create(OpCodes.Ret);
    reportProcessor.Append(reportTryStart);
    reportProcessor.Append(Instruction.Create(
        OpCodes.Newobj,
        stringBuilderConstructor));
    reportProcessor.Append(Instruction.Create(OpCodes.Stloc, details));
    AppendStringBuilderText(
        reportProcessor,
        details,
        appendString,
        "visited_count=");
    reportProcessor.Append(Instruction.Create(OpCodes.Ldloc, details));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    reportProcessor.Append(Instruction.Create(OpCodes.Callvirt, appendInteger));
    reportProcessor.Append(Instruction.Create(OpCodes.Pop));
    AppendStringBuilderText(
        reportProcessor,
        details,
        appendString,
        ";pending_count=");
    reportProcessor.Append(Instruction.Create(OpCodes.Ldloc, details));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldarg_2));
    reportProcessor.Append(Instruction.Create(OpCodes.Callvirt, appendInteger));
    reportProcessor.Append(Instruction.Create(OpCodes.Pop));
    AppendStringBuilderText(
        reportProcessor,
        details,
        appendString,
        ";candidate_parent_count=");
    reportProcessor.Append(Instruction.Create(OpCodes.Ldloc, details));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldarg_3));
    reportProcessor.Append(Instruction.Create(OpCodes.Callvirt, appendInteger));
    reportProcessor.Append(Instruction.Create(OpCodes.Pop));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "magicka_patch_runtime_recovery"));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "Railgun.mParents"));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "Magicka.GameLogic.Spells.Railgun"));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldloc, details));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Callvirt,
        stringBuilderToString));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldstr, string.Empty));
    reportProcessor.Append(Instruction.Create(OpCodes.Call, sendRuntimeGuard));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        reportReturn));
    reportProcessor.Append(reportHandlerStart);
    reportProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        reportReturn));
    reportProcessor.Append(reportReturn);
    reportRecovery.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = module.TypeSystem.Object,
        TryStart = reportTryStart,
        TryEnd = reportHandlerStart,
        HandlerStart = reportHandlerStart,
        HandlerEnd = reportReturn,
    });
    reportRecovery.Body.MaxStackSize = 6;

    MethodDefinition cycleCheck = new MethodDefinition(
        cycleCheckName,
        MethodAttributes.Private | MethodAttributes.HideBySig,
        module.TypeSystem.Boolean);
    cycleCheck.Parameters.Add(new ParameterDefinition(
        "candidate",
        ParameterAttributes.None,
        railgun));
    railgun.Methods.Add(cycleCheck);

    ArrayType railgunArray = new ArrayType(railgun);
    VariableDefinition pending = new VariableDefinition(railgunArray);
    VariableDefinition visited = new VariableDefinition(railgunArray);
    VariableDefinition pendingCount = new VariableDefinition(module.TypeSystem.Int32);
    VariableDefinition visitedCount = new VariableDefinition(module.TypeSystem.Int32);
    VariableDefinition current = new VariableDefinition(railgun);
    VariableDefinition visitedIndex = new VariableDefinition(module.TypeSystem.Int32);
    VariableDefinition parent = new VariableDefinition(railgun);
    VariableDefinition parentIndex = new VariableDefinition(module.TypeSystem.Int32);
    VariableDefinition result = new VariableDefinition(module.TypeSystem.Boolean);
    cycleCheck.Body.Variables.Add(pending);
    cycleCheck.Body.Variables.Add(visited);
    cycleCheck.Body.Variables.Add(pendingCount);
    cycleCheck.Body.Variables.Add(visitedCount);
    cycleCheck.Body.Variables.Add(current);
    cycleCheck.Body.Variables.Add(visitedIndex);
    cycleCheck.Body.Variables.Add(parent);
    cycleCheck.Body.Variables.Add(parentIndex);
    cycleCheck.Body.Variables.Add(result);
    cycleCheck.Body.InitLocals = true;

    ILProcessor cycleProcessor = cycleCheck.Body.GetILProcessor();
    Instruction cycleTryStart = Instruction.Create(OpCodes.Nop);
    Instruction candidateValid = Instruction.Create(OpCodes.Ldc_I4, 256);
    Instruction outerCondition = Instruction.Create(OpCodes.Ldloc, pendingCount);
    Instruction outerBody = Instruction.Create(OpCodes.Ldloc, pendingCount);
    Instruction outerContinue = Instruction.Create(OpCodes.Nop);
    Instruction visitedCondition = Instruction.Create(OpCodes.Ldloc, visitedIndex);
    Instruction visitedBody = Instruction.Create(OpCodes.Ldloc, visited);
    Instruction currentUnique = Instruction.Create(OpCodes.Ldloc, current);
    Instruction parentCondition = Instruction.Create(OpCodes.Ldloc, parentIndex);
    Instruction parentBody = Instruction.Create(OpCodes.Ldloc, current);
    Instruction parentContinue = Instruction.Create(OpCodes.Ldloc, parentIndex);
    Instruction cycleDetected = Instruction.Create(
        OpCodes.Ldstr,
        "railgun_parent_cycle_prevented");
    Instruction traversalLimit = Instruction.Create(
        OpCodes.Ldstr,
        "railgun_parent_cycle_check_limit_reached");
    Instruction safeCompletion = Instruction.Create(OpCodes.Ldc_I4_0);
    Instruction cycleHandlerStart = Instruction.Create(OpCodes.Pop);
    Instruction cycleReturn = Instruction.Create(OpCodes.Ldloc, result);

    cycleProcessor.Append(cycleTryStart);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brtrue,
        candidateValid));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "railgun_parent_cycle_check_failed"));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, reportRecovery));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));

    cycleProcessor.Append(candidateValid);
    cycleProcessor.Append(Instruction.Create(OpCodes.Newarr, railgun));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, pending));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, 256));
    cycleProcessor.Append(Instruction.Create(OpCodes.Newarr, railgun));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visited));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pending));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Br, outerCondition));

    cycleProcessor.Append(outerBody);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Sub));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pending));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, current));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, current));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brfalse,
        outerContinue));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visitedIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Br, visitedCondition));

    cycleProcessor.Append(visitedBody);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, current));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, referenceEquals));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brtrue,
        outerContinue));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Add));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visitedIndex));
    cycleProcessor.Append(visitedCondition);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Blt, visitedBody));

    cycleProcessor.Append(currentUnique);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, referenceEquals));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brtrue,
        cycleDetected));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, 256));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Bge,
        traversalLimit));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visited));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, current));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Add));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, parentIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Br, parentCondition));

    cycleProcessor.Append(parentBody);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldfld, parentsField));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, parentIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Callvirt, getParentItem));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, parent));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, parent));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brfalse,
        parentContinue));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, 256));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Bge,
        traversalLimit));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pending));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, parent));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Add));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, pendingCount));
    cycleProcessor.Append(parentContinue);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Add));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, parentIndex));
    cycleProcessor.Append(parentCondition);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, current));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldfld, parentsField));
    cycleProcessor.Append(Instruction.Create(OpCodes.Callvirt, getParentCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Blt, parentBody));

    cycleProcessor.Append(outerContinue);
    cycleProcessor.Append(outerCondition);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Bgt, outerBody));
    cycleProcessor.Append(Instruction.Create(OpCodes.Br, safeCompletion));

    cycleProcessor.Append(cycleDetected);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldfld, parentsField));
    cycleProcessor.Append(Instruction.Create(OpCodes.Callvirt, getParentCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, reportRecovery));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));

    cycleProcessor.Append(traversalLimit);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldfld, parentsField));
    cycleProcessor.Append(Instruction.Create(OpCodes.Callvirt, getParentCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, reportRecovery));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));

    cycleProcessor.Append(safeCompletion);
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));

    cycleProcessor.Append(cycleHandlerStart);
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "railgun_parent_cycle_check_failed"));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, reportRecovery));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));
    cycleProcessor.Append(cycleReturn);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ret));
    cycleCheck.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = module.TypeSystem.Object,
        TryStart = cycleTryStart,
        TryEnd = cycleHandlerStart,
        HandlerStart = cycleHandlerStart,
        HandlerEnd = cycleReturn,
    });
    cycleCheck.Body.MaxStackSize = 4;

    MethodDefinition lockAll = RequireMethod(
        railgun,
        "LockAll",
        parameterCount: 0);
    Instruction originalLockAllStart = lockAll.Body.Instructions[0];
    Instruction originalLockAllReturn = RequireSingleReturn(lockAll);
    ILProcessor lockProcessor = lockAll.Body.GetILProcessor();
    Instruction beginLockTraversal = Instruction.Create(OpCodes.Ldarg_0);
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Ldarg_0));
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Ldfld, lockTraversalActive));
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Brfalse, beginLockTraversal));
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Ret));
    lockProcessor.InsertBefore(originalLockAllStart, beginLockTraversal);
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Ldc_I4_1));
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Stfld, lockTraversalActive));
    lockProcessor.InsertBefore(
        originalLockAllReturn,
        Instruction.Create(OpCodes.Ldarg_0));
    lockProcessor.InsertBefore(
        originalLockAllReturn,
        Instruction.Create(OpCodes.Ldc_I4_0));
    lockProcessor.InsertBefore(
        originalLockAllReturn,
        Instruction.Create(OpCodes.Stfld, lockTraversalActive));
    lockAll.Body.MaxStackSize = Math.Max(lockAll.Body.MaxStackSize, 2);

    MethodDefinition update = RequireMethod(
        railgun,
        "Update",
        parameterCount: 2);
    Instruction[] updateInstructions = update.Body.Instructions.ToArray();
    Instruction parentAdd = updateInstructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.Name == "Add"
        && called.DeclaringType.FullName == parentsField.FieldType.FullName
        && updateInstructions
            .Skip(Math.Max(0, Array.IndexOf(updateInstructions, instruction) - 4))
            .Take(4)
            .Any(previous =>
                previous.OpCode == OpCodes.Ldfld
                && previous.Operand is FieldReference field
                && field.FullName == parentsField.FullName));
    int parentAddIndex = Array.IndexOf(updateInstructions, parentAdd);
    VariableDefinition selectedCandidate = updateInstructions
        .Take(parentAddIndex)
        .Reverse()
        .Select(instruction => instruction.Operand)
        .OfType<VariableDefinition>()
        .First(variable => variable.VariableType.FullName == railgun.FullName);
    Instruction selectedStore = updateInstructions
        .Take(parentAddIndex)
        .Where(instruction =>
            instruction.Operand == selectedCandidate
            && (instruction.OpCode == OpCodes.Stloc
                || instruction.OpCode == OpCodes.Stloc_S))
        .Last();
    int selectedStoreIndex = Array.IndexOf(updateInstructions, selectedStore);
    if (selectedStoreIndex == 0
        || updateInstructions[selectedStoreIndex - 1].Operand
            is not VariableDefinition activeCandidate
        || activeCandidate.VariableType.FullName != railgun.FullName)
    {
        throw new InvalidOperationException(
            "Could not identify the active Railgun candidate local.");
    }

    Instruction loopContinue = updateInstructions[selectedStoreIndex + 1];
    Instruction geometryGuard = updateInstructions
        .Take(selectedStoreIndex)
        .Where(instruction => instruction.Operand == loopContinue)
        .Last();
    int geometryGuardIndex = Array.IndexOf(updateInstructions, geometryGuard);
    Instruction mutationStart = updateInstructions[geometryGuardIndex + 1];
    if (mutationStart.OpCode != OpCodes.Ldnull)
    {
        throw new InvalidOperationException(
            "Unexpected Railgun candidate mutation start.");
    }

    ILProcessor updateProcessor = update.Body.GetILProcessor();
    foreach (Instruction injected in new[]
             {
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Ldloc, activeCandidate),
                 Instruction.Create(OpCodes.Call, cycleCheck),
                 Instruction.Create(OpCodes.Brtrue, loopContinue),
             })
    {
        updateProcessor.InsertBefore(mutationStart, injected);
    }
    update.Body.MaxStackSize = Math.Max(update.Body.MaxStackSize, 2);
}

static void PatchWarlordAbilityDiagnostic(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    const string helperTypeName =
        "Magicka.CommunityPatch.WarlordAbilityDiagnostic";
    if (types.ContainsKey(helperTypeName))
    {
        throw new InvalidOperationException(
            helperTypeName + " already exists.");
    }

    TypeDefinition characterTemplate = RequireType(
        types,
        "Magicka.GameLogic.Entities.CharacterTemplate");
    TypeDefinition ability = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.Ability");
    TypeDefinition melee = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.Melee");
    TypeDefinition warlord = RequireType(
        types,
        "Magicka.GameLogic.Entities.Bosses.WarlordCharacter");
    TypeDefinition nonPlayerCharacter = RequireType(
        types,
        "Magicka.GameLogic.Entities.NonPlayerCharacter");
    TypeDefinition patchTelemetry = RequireType(
        types,
        "Magicka.CommunityPatch.PatchTelemetry");

    FieldDefinition disposedField = characterTemplate.Fields.Single(field =>
        field.Name == "mDisposed"
        && !field.IsStatic
        && field.FieldType.FullName == "System.Boolean");
    if (characterTemplate.Methods.Any(method =>
            method.Name == "CommunityPatchIsDisposed"
            && method.Parameters.Count == 0))
    {
        throw new InvalidOperationException(
            "CharacterTemplate.CommunityPatchIsDisposed already exists.");
    }

    MethodDefinition isDisposed = new MethodDefinition(
        "CommunityPatchIsDisposed",
        MethodAttributes.Assembly | MethodAttributes.HideBySig,
        module.TypeSystem.Boolean);
    characterTemplate.Methods.Add(isDisposed);
    ILProcessor disposedProcessor = isDisposed.Body.GetILProcessor();
    disposedProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    disposedProcessor.Append(Instruction.Create(OpCodes.Ldfld, disposedField));
    disposedProcessor.Append(Instruction.Create(OpCodes.Ret));
    isDisposed.Body.MaxStackSize = 1;

    MethodDefinition getTemplateAbilities = RequireMethod(
        characterTemplate,
        "get_Abilities",
        parameterCount: 0);
    MethodDefinition getTemplateId = RequireMethod(
        characterTemplate,
        "get_ID",
        parameterCount: 0);
    MethodDefinition getTemplateName = RequireMethod(
        characterTemplate,
        "get_Name",
        parameterCount: 0);
    MethodDefinition getNpcAbilities = RequireMethod(
        nonPlayerCharacter,
        "get_Abilities",
        parameterCount: 0);
    MethodDefinition sendRuntimeGuard = RequireMethod(
        patchTelemetry,
        "SendRuntimeGuard",
        parameterCount: 6);

    MethodReference getObjectType = FindMethodReference(
        module,
        "System.Object",
        "GetType",
        parameterCount: 0,
        returnType: "System.Type");
    MethodReference getTypeFullName = FindMethodReference(
        module,
        "System.Type",
        "get_FullName",
        parameterCount: 0,
        returnType: "System.String");
    MethodReference referenceEquals = FindMethodReference(
        module,
        "System.Object",
        "ReferenceEquals",
        parameterCount: 2,
        returnType: "System.Boolean");
    MethodReference stringBuilderConstructor = FindMethodReference(
        module,
        "System.Text.StringBuilder",
        ".ctor",
        parameterCount: 0,
        returnType: "System.Void");
    MethodReference appendString = FindMethodReference(
        module,
        "System.Text.StringBuilder",
        "Append",
        parameterCount: 1,
        returnType: "System.Text.StringBuilder",
        parameterType: "System.String");
    TypeReference stringBuilderType = appendString.DeclaringType;
    MethodReference stringBuilderToString = CreateInstanceMethodReference(
        "ToString",
        stringBuilderType,
        module.TypeSystem.String);
    MethodReference appendBoolean = CreateInstanceMethodReference(
        "Append",
        stringBuilderType,
        stringBuilderType,
        module.TypeSystem.Boolean);
    MethodReference appendInteger = CreateInstanceMethodReference(
        "Append",
        stringBuilderType,
        stringBuilderType,
        module.TypeSystem.Int32);

    TypeDefinition helperType = new TypeDefinition(
        "Magicka.CommunityPatch",
        "WarlordAbilityDiagnostic",
        TypeAttributes.NotPublic
        | TypeAttributes.Abstract
        | TypeAttributes.Sealed
        | TypeAttributes.BeforeFieldInit
        | TypeAttributes.Class,
        module.TypeSystem.Object);
    module.Types.Add(helperType);
    MethodDefinition inspect = new MethodDefinition(
        "Inspect",
        MethodAttributes.Assembly
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        module.TypeSystem.Void);
    inspect.Parameters.Add(new ParameterDefinition(
        "template",
        ParameterAttributes.None,
        characterTemplate));
    inspect.Parameters.Add(new ParameterDefinition(
        "abilities",
        ParameterAttributes.None,
        getNpcAbilities.ReturnType));
    helperType.Methods.Add(inspect);

    VariableDefinition primaryAbility = new VariableDefinition(ability);
    VariableDefinition primaryType = new VariableDefinition(module.TypeSystem.String);
    VariableDefinition details = new VariableDefinition(stringBuilderType);
    inspect.Body.Variables.Add(primaryAbility);
    inspect.Body.Variables.Add(primaryType);
    inspect.Body.Variables.Add(details);
    inspect.Body.InitLocals = true;
    ILProcessor processor = inspect.Body.GetILProcessor();

    Instruction tryStart = Instruction.Create(OpCodes.Nop);
    Instruction inspectPrimary = Instruction.Create(OpCodes.Ldloc, primaryAbility);
    Instruction invalidPrimary = Instruction.Create(OpCodes.Ldloc, primaryAbility);
    Instruction readPrimaryType = Instruction.Create(OpCodes.Ldloc, primaryAbility);
    Instruction primaryTypeReady = Instruction.Create(OpCodes.Newobj, stringBuilderConstructor);
    Instruction appendEmptyTemplateId = Instruction.Create(OpCodes.Ldloc, details);
    Instruction templateIdReady = Instruction.Create(OpCodes.Ldloc, details);
    Instruction templateNotDisposed = Instruction.Create(OpCodes.Ldc_I4_0);
    Instruction emptyTemplateName = Instruction.Create(OpCodes.Ldstr, string.Empty);
    Instruction sendDiagnostic = Instruction.Create(OpCodes.Call, sendRuntimeGuard);
    Instruction handlerStart = Instruction.Create(OpCodes.Pop);
    Instruction returnInstruction = Instruction.Create(OpCodes.Ret);

    processor.Append(tryStart);
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Stloc, primaryAbility));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Brfalse, inspectPrimary));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldlen));
    processor.Append(Instruction.Create(OpCodes.Conv_I4));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Ble, inspectPrimary));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Ldelem_Ref));
    processor.Append(Instruction.Create(OpCodes.Stloc, primaryAbility));
    processor.Append(inspectPrimary);
    processor.Append(Instruction.Create(OpCodes.Isinst, melee));
    processor.Append(Instruction.Create(OpCodes.Brfalse, invalidPrimary));
    processor.Append(Instruction.Create(OpCodes.Leave, returnInstruction));

    processor.Append(invalidPrimary);
    processor.Append(Instruction.Create(OpCodes.Brtrue, readPrimaryType));
    processor.Append(Instruction.Create(OpCodes.Ldstr, "null"));
    processor.Append(Instruction.Create(OpCodes.Stloc, primaryType));
    processor.Append(Instruction.Create(OpCodes.Br, primaryTypeReady));
    processor.Append(readPrimaryType);
    processor.Append(Instruction.Create(OpCodes.Callvirt, getObjectType));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getTypeFullName));
    processor.Append(Instruction.Create(OpCodes.Stloc, primaryType));

    processor.Append(primaryTypeReady);
    processor.Append(Instruction.Create(OpCodes.Stloc, details));
    AppendStringBuilderText(processor, details, appendString, "template_null=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Ceq));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendBoolean));
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(processor, details, appendString, ";template_disposed=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Brfalse, templateNotDisposed));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Call, isDisposed));
    Instruction appendDisposed = Instruction.Create(OpCodes.Callvirt, appendBoolean);
    processor.Append(Instruction.Create(OpCodes.Br, appendDisposed));
    processor.Append(templateNotDisposed);
    processor.Append(appendDisposed);
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(processor, details, appendString, ";template_id=");
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Brfalse, appendEmptyTemplateId));
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getTemplateId));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendInteger));
    processor.Append(Instruction.Create(OpCodes.Pop));
    processor.Append(Instruction.Create(OpCodes.Br, templateIdReady));
    processor.Append(appendEmptyTemplateId);
    processor.Append(Instruction.Create(OpCodes.Ldstr, string.Empty));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendString));
    processor.Append(Instruction.Create(OpCodes.Pop));

    processor.Append(templateIdReady);
    processor.Append(Instruction.Create(OpCodes.Ldstr, ";abilities_null="));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendString));
    processor.Append(Instruction.Create(OpCodes.Pop));
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Ceq));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendBoolean));
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(processor, details, appendString, ";ability_count=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    Instruction appendZeroLength = Instruction.Create(OpCodes.Ldc_I4_0);
    Instruction appendLength = Instruction.Create(OpCodes.Callvirt, appendInteger);
    processor.Append(Instruction.Create(OpCodes.Brfalse, appendZeroLength));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldlen));
    processor.Append(Instruction.Create(OpCodes.Conv_I4));
    processor.Append(Instruction.Create(OpCodes.Br, appendLength));
    processor.Append(appendZeroLength);
    processor.Append(appendLength);
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(processor, details, appendString, ";primary_null=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldloc, primaryAbility));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Ceq));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendBoolean));
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(
        processor,
        details,
        appendString,
        ";shares_template_abilities=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    Instruction noSharedTemplateAbilities = Instruction.Create(OpCodes.Ldc_I4_0);
    processor.Append(Instruction.Create(
        OpCodes.Brfalse,
        noSharedTemplateAbilities));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getTemplateAbilities));
    processor.Append(Instruction.Create(OpCodes.Call, referenceEquals));
    Instruction appendSharesArray = Instruction.Create(OpCodes.Callvirt, appendBoolean);
    processor.Append(Instruction.Create(OpCodes.Br, appendSharesArray));
    processor.Append(noSharedTemplateAbilities);
    processor.Append(appendSharesArray);
    processor.Append(Instruction.Create(OpCodes.Pop));

    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "magicka_patch_warlord_ability_diagnostic"));
    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "warlord_primary_ability_not_melee"));
    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "NonPlayerCharacter.Abilities"));
    processor.Append(Instruction.Create(OpCodes.Ldloc, primaryType));
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Callvirt, stringBuilderToString));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Brfalse, emptyTemplateName));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getTemplateName));
    processor.Append(Instruction.Create(OpCodes.Br, sendDiagnostic));
    processor.Append(emptyTemplateName);
    processor.Append(sendDiagnostic);
    processor.Append(Instruction.Create(OpCodes.Leave, returnInstruction));
    processor.Append(handlerStart);
    processor.Append(Instruction.Create(OpCodes.Leave, returnInstruction));
    processor.Append(returnInstruction);
    inspect.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = module.TypeSystem.Object,
        TryStart = tryStart,
        TryEnd = handlerStart,
        HandlerStart = handlerStart,
        HandlerEnd = returnInstruction,
    });
    inspect.Body.MaxStackSize = 6;

    MethodDefinition applyTemplate = RequireMethod(
        warlord,
        "ApplyTemplate",
        parameterCount: 2);
    Instruction baseApply = applyTemplate.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference called
        && called.Name == "ApplyTemplate"
        && called.DeclaringType.FullName == nonPlayerCharacter.FullName);
    Instruction originalCast = applyTemplate.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Isinst
        && instruction.Operand is TypeReference type
        && type.FullName == melee.FullName);
    if (applyTemplate.Body.Instructions.IndexOf(baseApply)
        >= applyTemplate.Body.Instructions.IndexOf(originalCast))
    {
        throw new InvalidOperationException(
            "Unexpected WarlordCharacter.ApplyTemplate instruction order.");
    }

    ILProcessor applyProcessor = applyTemplate.Body.GetILProcessor();
    Instruction cursor = baseApply;
    foreach (Instruction injected in new[]
             {
                 Instruction.Create(OpCodes.Ldarg_1),
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Call, getNpcAbilities),
                 Instruction.Create(OpCodes.Call, inspect),
             })
    {
        applyProcessor.InsertAfter(cursor, injected);
        cursor = injected;
    }
    applyTemplate.Body.MaxStackSize = Math.Max(
        applyTemplate.Body.MaxStackSize,
        2);
}

static void AppendStringBuilderText(
    ILProcessor processor,
    VariableDefinition builder,
    MethodReference appendString,
    string text)
{
    processor.Append(Instruction.Create(OpCodes.Ldloc, builder));
    processor.Append(Instruction.Create(OpCodes.Ldstr, text));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendString));
    processor.Append(Instruction.Create(OpCodes.Pop));
}

static MethodReference FindMethodReference(
    ModuleDefinition module,
    string declaringType,
    string name,
    int parameterCount,
    string returnType,
    string? parameterType = null)
{
    MethodReference[] matches = AllTypes(module)
        .SelectMany(type => type.Methods)
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Where(method => method.DeclaringType.FullName == declaringType
            && method.Name == name
            && method.Parameters.Count == parameterCount
            && method.ReturnType.FullName == returnType
            && (parameterType == null
                || (method.Parameters.Count == 1
                    && method.Parameters[0].ParameterType.FullName
                        == parameterType)))
        .GroupBy(method => method.FullName, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToArray();
    if (matches.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one referenced method " + declaringType + "." + name
            + ", found " + matches.Length + ".");
    }
    return matches[0];
}

static MethodReference CreateInstanceMethodReference(
    string name,
    TypeReference declaringType,
    TypeReference returnType,
    params TypeReference[] parameterTypes)
{
    MethodReference method = new MethodReference(
        name,
        returnType,
        declaringType)
    {
        HasThis = true,
        CallingConvention = MethodCallingConvention.Default,
    };
    foreach (TypeReference parameterType in parameterTypes)
    {
        method.Parameters.Add(new ParameterDefinition(parameterType));
    }
    return method;
}

static void RepairCharacterTemplateStaticCaches(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    (string TypeName, string FieldName)[] existingCacheOwners =
    [
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonSpirit",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonFlamer",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonCross",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonZombie",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonBug",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonUndead",
            "sTemplates"),
    ];
    foreach ((string typeName, string fieldName) in existingCacheOwners)
    {
        TypeDefinition owner = RequireType(types, typeName);
        FieldDefinition field = RequireStaticCharacterTemplateField(
            owner,
            fieldName);
        MethodDefinition disposeCache = RequireMethod(
            owner,
            "DisposeCache",
            parameterCount: 0);
        AppendStaticFieldReset(disposeCache, field);
    }

    (string TypeName, string FieldName)[] newCacheOwners =
    [
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.MutateBeastman",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.OtherworldlyDischarge",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonElemental",
            "sTemplate"),
    ];
    List<MethodDefinition> addedDisposeMethods = new List<MethodDefinition>();
    foreach ((string typeName, string fieldName) in newCacheOwners)
    {
        TypeDefinition owner = RequireType(types, typeName);
        FieldDefinition field = RequireStaticCharacterTemplateField(
            owner,
            fieldName);
        if (owner.Methods.Any(method =>
                method.Name == "DisposeCache"
                && method.Parameters.Count == 0))
        {
            throw new InvalidOperationException(
                owner.FullName + ".DisposeCache already exists.");
        }

        MethodDefinition disposeCache = new MethodDefinition(
            "DisposeCache",
            MethodAttributes.Assembly
            | MethodAttributes.Static
            | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        owner.Methods.Add(disposeCache);
        ILProcessor processor = disposeCache.Body.GetILProcessor();
        processor.Append(Instruction.Create(OpCodes.Ldnull));
        processor.Append(Instruction.Create(OpCodes.Stsfld, field));
        processor.Append(Instruction.Create(OpCodes.Ret));
        disposeCache.Body.MaxStackSize = 1;
        addedDisposeMethods.Add(disposeCache);
    }

    TypeDefinition magick = RequireType(
        types,
        "Magicka.GameLogic.Spells.Magick");
    MethodDefinition disposeMagicks = RequireMethod(
        magick,
        "DisposeMagicks",
        parameterCount: 0);
    Instruction disposeMagicksReturn = RequireSingleReturn(disposeMagicks);
    ILProcessor magickProcessor = disposeMagicks.Body.GetILProcessor();
    foreach (MethodDefinition disposeCache in addedDisposeMethods)
    {
        magickProcessor.InsertBefore(
            disposeMagicksReturn,
            Instruction.Create(OpCodes.Call, disposeCache));
    }

    TypeDefinition characterTemplate = RequireType(
        types,
        "Magicka.GameLogic.Entities.CharacterTemplate");
    FieldDefinition avatarTemplates = characterTemplate.Fields.Single(field =>
        field.Name == "sCachedAvatarTemplates"
        && field.IsStatic
        && field.FieldType.FullName
            == "System.Collections.Generic.Dictionary`2<System.String,"
               + "Magicka.GameLogic.Entities.CharacterTemplate>");
    MethodDefinition initializeAvatarCache = RequireMethod(
        characterTemplate,
        "InitialisePlayerAvatarCache",
        parameterCount: 1);
    MethodReference clearAvatarTemplates = initializeAvatarCache.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(method =>
            method.Name == "Clear"
            && method.Parameters.Count == 0
            && method.DeclaringType.FullName == avatarTemplates.FieldType.FullName);
    MethodDefinition clearCache = RequireMethod(
        characterTemplate,
        "ClearCache",
        parameterCount: 0);
    if (clearCache.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Ldsfld
            && instruction.Operand is FieldReference field
            && field.FullName == avatarTemplates.FullName))
    {
        throw new InvalidOperationException(
            "CharacterTemplate.ClearCache already clears avatar templates.");
    }

    Instruction clearCacheReturn = RequireSingleReturn(clearCache);
    ILProcessor clearCacheProcessor = clearCache.Body.GetILProcessor();
    clearCacheProcessor.InsertBefore(
        clearCacheReturn,
        Instruction.Create(OpCodes.Ldsfld, avatarTemplates));
    clearCacheProcessor.InsertBefore(
        clearCacheReturn,
        Instruction.Create(OpCodes.Callvirt, clearAvatarTemplates));
    clearCache.Body.MaxStackSize = Math.Max(clearCache.Body.MaxStackSize, 1);
}

static FieldDefinition RequireStaticCharacterTemplateField(
    TypeDefinition owner,
    string fieldName)
{
    FieldDefinition field = owner.Fields.Single(candidate =>
        candidate.Name == fieldName
        && candidate.IsStatic
        && (candidate.FieldType.FullName
                == "Magicka.GameLogic.Entities.CharacterTemplate"
            || candidate.FieldType.FullName
                == "Magicka.GameLogic.Entities.CharacterTemplate[]"));
    return field;
}

static void AppendStaticFieldReset(
    MethodDefinition method,
    FieldDefinition field)
{
    if (!method.IsStatic || !method.HasBody)
    {
        throw new InvalidOperationException(
            "Expected a static cache teardown method: " + method.FullName);
    }

    if (method.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Stsfld
            && instruction.Operand is FieldReference stored
            && stored.FullName == field.FullName))
    {
        throw new InvalidOperationException(
            method.FullName + " already resets " + field.FullName + ".");
    }

    Instruction returnInstruction = RequireSingleReturn(method);
    ILProcessor processor = method.Body.GetILProcessor();
    processor.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Ldnull));
    processor.InsertBefore(
        returnInstruction,
        Instruction.Create(OpCodes.Stsfld, field));
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 1);
}

static Instruction RequireSingleReturn(MethodDefinition method)
{
    Instruction[] returns = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ret)
        .ToArray();
    if (returns.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one return in " + method.FullName
            + ", found " + returns.Length + ".");
    }

    return returns[0];
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
