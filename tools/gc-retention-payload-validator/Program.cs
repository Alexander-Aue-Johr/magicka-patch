using Mono.Cecil;
using Mono.Cecil.Cil;

const string RuntimeAssemblyName = "Magicka.GcDiagnostics";
const string RegistryTypeName = "Magicka.GcDiagnostics.RetentionRegistry";
const string RegistryStateTypeName = "Magicka.GcDiagnostics.RetentionState";

try
{
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: PayloadValidator <payload-directory>");
    return 2;
}

string payloadDirectory = Path.GetFullPath(args[0]);
string magickaPath = Path.Combine(payloadDirectory, "Magicka.exe");
string polygonHeadPath = Path.Combine(payloadDirectory, "PolygonHead.dll");
string runtimePath = Path.Combine(
    payloadDirectory,
    "Magicka.GcDiagnostics.dll");

foreach (string requiredPath in new[]
         {
             magickaPath,
             polygonHeadPath,
             runtimePath,
         })
{
    if (!File.Exists(requiredPath))
    {
        throw new FileNotFoundException(
            "Required payload file is missing.",
            requiredPath);
    }
}

DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(payloadDirectory);
ReaderParameters readerParameters = new ReaderParameters
{
    AssemblyResolver = resolver,
    InMemory = true,
    ReadSymbols = false,
};

using AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(
    runtimePath,
    readerParameters);
ValidateRuntime(runtime);
TypeDefinition runtimeRegistry = runtime.MainModule.GetType(
    RegistryTypeName) ?? throw new InvalidDataException(
        "Runtime registry type is missing after validation.");

using AssemblyDefinition magicka = AssemblyDefinition.ReadAssembly(
    magickaPath,
    readerParameters);
using AssemblyDefinition polygonHead = AssemblyDefinition.ReadAssembly(
    polygonHeadPath,
    readerParameters);

ValidateClr2Assembly(runtime);
ValidateClr2Assembly(magicka);
ValidateClr2Assembly(polygonHead);
ValidateExceptionHandlerNestingOrder(magicka);
ValidateNetworkServerForcedSyncExit(magicka);
ValidateRuntimeCompatibilityGuards(magicka);
ValidateArrayEqualsNullGuard(magicka);
ValidateAvatarFindInteractableNullGuard(magicka);
ValidateCharacterCastSpellGamerNullGuard(magicka);
ValidateCharacterSelectDisposedIconGuard(magicka);
ValidateNetworkClientRulesetTeardownGuard(magicka);
ValidateGraphicsStartupErrors(magicka);
ValidateGcDiagnosticsStartupCheck(magicka);
ValidateLevelHashMissingFile(magicka);
ValidateTelemetryPatchVersion(magicka);
ValidatePlayerGameDeinitialize(magicka);
ValidateEntityCollisionCallbackCleanup(magicka);
ValidateCollectionLocks(magicka);
ValidateLightSceneDetach(magicka, polygonHead);
ValidateRainSceneDetach(magicka);
ValidateShadowBlobsSceneDetach(magicka);
ValidatePhysicsManagerClearReferences(magicka);
ValidateMeteorShowerRemoveReferences(magicka);
ValidateBlizzardRemoveReferences(magicka);
ValidateControllerAvatarDetach(magicka);
ValidateCharacterTemplateStaticCaches(magicka);
ValidateWarlordAbilityDiagnostic(magicka);
ValidateRailgunParentCycleRepair(magicka);
ValidateJudgementSprayConditionCacheRepair(magicka);

Dictionary<string, int> magickaCalls = ValidateInstrumentedAssembly(
    magicka,
    runtimeRegistry,
    minimumCalls: new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["BeginEpoch"] = 1,
        ["Register"] = 6,
        ["MarkActive"] = 210,
        ["MarkResidentActive"] = 17,
        ["MarkDeactivated"] = 27,
        ["MarkMustCollect"] = 22,
        ["MarkMustDetach"] = 64,
        ["Checkpoint"] = 1,
    });
ValidateMagickaLifecycleCoverage(magicka);
RequireResolvedBranchTargets(magicka);
Dictionary<string, int> polygonHeadCalls = ValidateInstrumentedAssembly(
    polygonHead,
    runtimeRegistry,
    minimumCalls: new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Register"] = 1,
        ["MarkMustCollect"] = 1,
    });
RequireEntryLifecycleHook(
    polygonHead,
    "PolygonHead.Models.BiTreeModel",
    "Dispose",
    parameterCount: 0,
    isStatic: false,
    helperName: "MarkMustCollect");

Console.WriteLine(
    "Validated CLR-2 diagnostic payload: "
    + Path.GetFileName(magickaPath)
    + ", "
    + Path.GetFileName(polygonHeadPath)
    + ", "
    + Path.GetFileName(runtimePath));
Console.WriteLine(
    "Magicka hooks: " + FormatCounts(magickaCalls));
Console.WriteLine(
    "PolygonHead hooks: " + FormatCounts(polygonHeadCalls));
return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("Payload validation failed: " + exception.Message);
    return 1;
}

static void ValidateRuntime(AssemblyDefinition runtime)
{
    if (runtime.Name.Name != RuntimeAssemblyName)
    {
        throw new InvalidDataException(
            "Unexpected runtime assembly name: " + runtime.Name.Name);
    }

    if (runtime.MainModule.RuntimeVersion != "v2.0.50727")
    {
        throw new InvalidDataException(
            "Runtime helper does not target CLR 2.0: "
            + runtime.MainModule.RuntimeVersion);
    }

    TypeDefinition? registry = runtime.MainModule.GetType(RegistryTypeName);
    if (registry is null)
    {
        throw new InvalidDataException(
            "Runtime registry type is missing: " + RegistryTypeName);
    }

    TypeDefinition? registryState = runtime.MainModule.GetType(
        RegistryStateTypeName);
    if (registryState is null)
    {
        throw new InvalidDataException(
            "Runtime registry state type is missing: " + RegistryStateTypeName);
    }

    FieldDefinition[] registryVersionFields = registryState.Fields
        .Where(field => field.Name == "RegistryVersion")
        .ToArray();
    if (registryVersionFields.Length != 1
        || !registryVersionFields[0].IsStatic
        || registryVersionFields[0].FieldType.FullName != "System.Int64")
    {
        throw new InvalidDataException(
            "Expected one static System.Int64 "
            + RegistryStateTypeName
            + ".RegistryVersion field for analyzer/manifest"
            + " consistency checks; found "
            + registryVersionFields.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            + ".");
    }

    string[] requiredMethods =
    [
        "BeginEpoch",
        "Register",
        "MarkActive",
        "MarkResidentActive",
        "MarkDeactivated",
        "MarkMustCollect",
        "MarkMustDetach",
        "Checkpoint",
    ];
    foreach (string methodName in requiredMethods)
    {
        MethodDefinition[] matches = registry.Methods.Where(
                method => method.Name == methodName)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected one runtime method {methodName}, found {matches.Length}.");
        }
    }

    ValidateAnalyzerFindingTelemetry(runtime, registryState);
    ValidateRecurringRetentionCheckpoints(registryState);
}

static void ValidateRecurringRetentionCheckpoints(
    TypeDefinition registryState)
{
    MethodDefinition finish = registryState.Methods.Single(method =>
        method.Name == "FinishAnalysis"
        && method.Parameters.Count == 0);
    Instruction[] instructions = finish.Body.Instructions.ToArray();

    bool disablesRegistry = instructions.Any(instruction =>
        instruction.OpCode == OpCodes.Stsfld
        && instruction.Operand is FieldReference field
        && field.DeclaringType.FullName == RegistryStateTypeName
        && field.Name == "Enabled");
    if (disablesRegistry)
    {
        throw new InvalidDataException(
            "FinishAnalysis still disables the retention registry after one run.");
    }

    string[] resetFields =
    [
        "PublishedManifestPath",
        "LastPublishedVersion",
        "LastPublishedGen2",
        "CheckpointLifecycle",
        "CheckpointUtcTicks",
        "SuppressedCheckpointCount",
        "DroppedWatchCount",
        "TrackingClosed",
    ];
    HashSet<string> storedFields = instructions
        .Where(instruction => instruction.OpCode == OpCodes.Stsfld)
        .Select(instruction => instruction.Operand as FieldReference)
        .Where(field => field is not null
            && field.DeclaringType.FullName == RegistryStateTypeName)
        .Select(field => field!.Name)
        .ToHashSet(StringComparer.Ordinal);
    string[] missingResets = resetFields
        .Where(field => !storedFields.Contains(field))
        .ToArray();
    if (missingResets.Length != 0)
    {
        throw new InvalidDataException(
            "FinishAnalysis is missing recurring-cycle resets: "
            + string.Join(", ", missingResets) + ".");
    }

    bool resetsAnalysisStarted = instructions.Any(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "System.Threading.Interlocked"
        && called.Name == "Exchange");
    if (!resetsAnalysisStarted)
    {
        throw new InvalidDataException(
            "FinishAnalysis does not reset the analysis start guard.");
    }

    int deleteLogIndex = Array.FindLastIndex(
        instructions,
        instruction => instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == RegistryStateTypeName
            && called.Name == "TryDeleteFile");
    int reopenTrackingIndex = Array.FindLastIndex(
        instructions,
        instruction => instruction.OpCode == OpCodes.Stsfld
            && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName == RegistryStateTypeName
            && field.Name == "TrackingClosed");
    if (deleteLogIndex < 0 || reopenTrackingIndex <= deleteLogIndex)
    {
        throw new InvalidDataException(
            "FinishAnalysis reopens tracking before completed-cycle files are removed.");
    }
}

static void ValidateAnalyzerFindingTelemetry(
    AssemblyDefinition runtime,
    TypeDefinition registryState)
{
    const string helperTypeName =
        "Magicka.GcDiagnostics.AnalyzerFindingTelemetry";
    TypeDefinition? helper = runtime.MainModule.GetType(helperTypeName);
    if (helper is null)
    {
        throw new InvalidDataException(
            "Analyzer finding telemetry helper is missing: " + helperTypeName);
    }

    MethodDefinition append = helper.Methods.Single(method =>
        method.Name == "TryAppendFinding"
        && method.Parameters.Count == 4);
    MethodDefinition sender = registryState.Methods.Single(method =>
        method.Name == "SendAnalyzerTelemetry"
        && method.Parameters.Count == 4);
    bool callsBoundedAppend = sender.Body.Instructions.Any(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == helperTypeName
        && called.Name == append.Name);
    if (!callsBoundedAppend)
    {
        throw new InvalidDataException(
            "SendAnalyzerTelemetry does not append complete bounded findings.");
    }

    string[] requiredFields =
    [
        "finding_group_count",
        "serialized_finding_count",
        "omitted_finding_count",
        "telemetry_truncated",
    ];
    HashSet<string> stringConstants = sender.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
        .Select(instruction => instruction.Operand as string)
        .Where(value => value is not null)
        .Select(value => value!)
        .ToHashSet(StringComparer.Ordinal);
    string[] missingFields = requiredFields
        .Where(field => !stringConstants.Contains(field))
        .ToArray();
    if (missingFields.Length != 0)
    {
        throw new InvalidDataException(
            "SendAnalyzerTelemetry is missing serialization fields: "
            + string.Join(", ", missingFields) + ".");
    }

    bool cutsStringBuilder = sender.Body.Instructions.Any(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "System.Text.StringBuilder"
        && called.Name == "set_Length");
    if (cutsStringBuilder)
    {
        throw new InvalidDataException(
            "SendAnalyzerTelemetry still cuts a finding at a character offset.");
    }
}

static Dictionary<string, int> ValidateInstrumentedAssembly(
    AssemblyDefinition assembly,
    TypeDefinition runtimeRegistry,
    IReadOnlyDictionary<string, int> minimumCalls)
{
    AssemblyNameReference[] runtimeReferences = assembly.MainModule
        .AssemblyReferences
        .Where(reference => reference.Name == RuntimeAssemblyName)
        .ToArray();
    if (runtimeReferences.Length != 1)
    {
        throw new InvalidDataException(
            $"{assembly.Name.Name} has {runtimeReferences.Length} references"
            + $" to {RuntimeAssemblyName}; expected one.");
    }

    if (assembly.MainModule.RuntimeVersion != "v2.0.50727")
    {
        throw new InvalidDataException(
            assembly.Name.Name + " no longer targets CLR 2.0.");
    }

    Dictionary<string, int> calls = new Dictionary<string, int>(
        StringComparer.Ordinal);
    foreach (TypeDefinition type in AllTypes(assembly.MainModule))
    {
        foreach (MethodDefinition method in type.Methods.Where(
                     method => method.HasBody))
        {
            ValidateBodyReferences(method);
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference called
                    || called.DeclaringType.FullName != RegistryTypeName)
                {
                    continue;
                }

                ValidateRuntimeCall(called, runtimeRegistry, method);

                calls.TryGetValue(called.Name, out int count);
                calls[called.Name] = count + 1;
            }
        }
    }

    foreach ((string methodName, int minimum) in minimumCalls)
    {
        calls.TryGetValue(methodName, out int actual);
        if (actual < minimum)
        {
            throw new InvalidDataException(
                $"{assembly.Name.Name} has {actual} {methodName} hooks;"
                + $" expected at least {minimum}.");
        }
    }

    return calls;
}

static void ValidateRuntimeCall(
    MethodReference called,
    TypeDefinition runtimeRegistry,
    MethodDefinition caller)
{
    if (called.DeclaringType.Scope is not AssemblyNameReference scope
        || scope.Name != RuntimeAssemblyName)
    {
        throw new InvalidDataException(
            "Injected call has the wrong assembly scope: "
            + called.FullName
            + " in "
            + caller.FullName);
    }

    MethodDefinition[] matches = runtimeRegistry.Methods.Where(method =>
            method.Name == called.Name
            && method.ReturnType.FullName == called.ReturnType.FullName
            && method.Parameters.Select(parameter =>
                    parameter.ParameterType.FullName)
                .SequenceEqual(called.Parameters.Select(parameter =>
                    parameter.ParameterType.FullName)))
        .ToArray();
    if (matches.Length != 1)
    {
        throw new InvalidDataException(
            "Injected call does not match exactly one runtime method: "
            + called.FullName
            + " in "
            + caller.FullName);
    }
}

static void ValidateBodyReferences(MethodDefinition method)
{
    HashSet<Instruction> instructions = new HashSet<Instruction>(
        method.Body.Instructions);
    foreach (Instruction instruction in method.Body.Instructions)
    {
        if ((instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget
             || instruction.OpCode.OperandType == OperandType.InlineBrTarget)
            && instruction.Operand is not Instruction)
        {
            throw new InvalidDataException(
                "Unresolved branch target in " + method.FullName
                + " at IL_" + instruction.Offset.ToString("x4") + ".");
        }

        if (instruction.OpCode.OperandType == OperandType.InlineSwitch
            && instruction.Operand is not Instruction[])
        {
            throw new InvalidDataException(
                "Unresolved switch targets in " + method.FullName
                + " at IL_" + instruction.Offset.ToString("x4") + ".");
        }

        if (instruction.Operand is Instruction target
            && !instructions.Contains(target))
        {
            throw new InvalidDataException(
                "Invalid branch target in " + method.FullName);
        }

        if (instruction.Operand is Instruction[] targets
            && targets.Any(target => !instructions.Contains(target)))
        {
            throw new InvalidDataException(
                "Invalid switch target in " + method.FullName);
        }
    }

    foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
    {
        Instruction?[] boundaries =
        [
            handler.TryStart,
            handler.TryEnd,
            handler.HandlerStart,
            handler.HandlerEnd,
            handler.FilterStart,
        ];
        foreach (Instruction? boundary in boundaries)
        {
            if (boundary is not null && !instructions.Contains(boundary))
            {
                throw new InvalidDataException(
                    "Invalid exception-handler boundary in " + method.FullName);
            }
        }
    }
}

static void ValidateMagickaLifecycleCoverage(AssemblyDefinition assembly)
{
    (string TypeName, string MethodName, int ParameterCount, bool IsStatic,
        string HelperName)[] methods =
    [
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.WaveEntity",
            "Deinitialize", 0, false, "MarkDeactivated"),
        (
            "Magicka.GameLogic.Entities.Items.Pickable",
            "Deinitialize", 0, false, "MarkDeactivated"),
        (
            "Magicka.GameLogic.Entities.Bosses.GenericBoss",
            "DeInitialize", 0, false, "MarkDeactivated"),
        (
            "Magicka.GameLogic.Entities.Bosses.PropBoss",
            "DeInitialize", 0, false, "MarkDeactivated"),
        (
            "Magicka.GameLogic.Entities.Bosses.CthulhuMist",
            "Deactivate", 0, false, "MarkDeactivated"),
        (
            "Magicka.GameLogic.Entities.Items.Item",
            "Reinitialize", 1, false, "MarkActive"),
        (
            "Magicka.GameLogic.Entities.Barrier",
            "ReturnToCache", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.WaveEntity",
            "ReturnToCache", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Entities.DamageablePhysicsEntity",
            "ReturnToCache", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Entities.Snare",
            "ReturnToCache", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Entities.Gib",
            "ReturnGib", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Entities.Avatar",
            "AddToCache", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Entities.Items.BookOfMagick",
            "AddToCache", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Entities.Bosses.GenericBoss",
            "AddToCache", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell",
            "ReturnToCache", 1, true, "MarkMustDetach"),
        (
            "Magicka.GameLogic.Spells.SpellEffects.PushSpell",
            "ReturnToCache", 1, true, "MarkMustDetach"),
    ];

    foreach ((
                 string typeName,
                 string methodName,
                 int parameterCount,
                 bool isStatic,
                 string helperName) in methods)
    {
        if (!isStatic && helperName == "MarkDeactivated")
        {
            RequireEntryLifecycleHook(
                assembly,
                typeName,
                methodName,
                parameterCount,
                isStatic,
                helperName);
        }
        else
        {
            RequireReturnLifecycleHook(
                assembly,
                typeName,
                methodName,
                parameterCount,
                isStatic,
                helperName);
        }
    }

    RequireDirectCacheInsertionHook(
        assembly,
        "Magicka.GameLogic.Entities.NonPlayerCharacter",
        "Die",
        parameterCount: 0);
    RequireDirectCacheInsertionHook(
        assembly,
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Confuse",
        "OnRemove",
        parameterCount: 0);
    RequireDirectCacheInsertionHook(
        assembly,
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Confuse",
        "Execute",
        parameterCount: 3);
    RequireDirectCacheInsertionHook(
        assembly,
        "Magicka.GameLogic.Entities.TeslaField",
        "Deinitialize",
        parameterCount: 0);

    RequireActiveCacheSourceReturns(
        assembly,
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Confuse",
        "GetInstance",
        parameterCount: 0);
    RequireActiveCacheSourceReturns(
        assembly,
        "Magicka.GameLogic.Entities.TeslaField",
        "GetFromCache",
        parameterCount: 1);

    (string TypeName, string MethodName, int ParameterCount)[]
        residentPoolGetters =
    [
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity",
            "GetInstance", 0),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity",
            "GetSpecificInstance", 1),
        (
            "Magicka.GameLogic.Entities.SpellMine",
            "GetInstance", 0),
        (
            "Magicka.GameLogic.Entities.MissileEntity",
            "GetInstance", 1),
        (
            "Magicka.GameLogic.Entities.MissileEntity",
            "GetSpecificInstance", 1),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease/GreaseField",
            "GetInstance", 1),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease/GreaseField",
            "GetSpecificInstance", 1),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.VortexEntity",
            "GetInstance", 0),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.VortexEntity",
            "GetSpecificInstance", 1),
        (
            "Magicka.GameLogic.Entities.Items.Item",
            "GetPickableIntstance", 0),
        (
            "Magicka.GameLogic.Entities.Items.Item",
            "GetCachedWeapon", 1),
        (
            "Magicka.GameLogic.Entities.SprayEntity",
            "GetInstance", 0),
        (
            "Magicka.GameLogic.Entities.SprayEntity",
            "GetSpecificInstance", 1),
        (
            "Magicka.GameLogic.Entities.ElementalEgg",
            "GetInstance", 1),
    ];
    foreach ((
                 string typeName,
                 string methodName,
                 int parameterCount) in residentPoolGetters)
    {
        RequireActiveCacheSourceReturns(
            assembly,
            typeName,
            methodName,
            parameterCount,
            helperName: "MarkResidentActive");
    }

    RequireInstanceResidentSourceReturns(
        assembly,
        "Magicka.GameLogic.Entities.Avatar",
        "GetMissileInstance",
        parameterCount: 0,
        returnTypeName: "Magicka.GameLogic.Entities.MissileEntity");

    foreach (string typeName in residentPoolGetters
                 .Select(getter => getter.TypeName)
                 .Distinct(StringComparer.Ordinal))
    {
        RequireEntryLifecycleHook(
            assembly,
            typeName,
            "Deinitialize",
            parameterCount: 0,
            isStatic: false,
            helperName: "MarkDeactivated");
    }

    RequireEntryLifecycleHook(
        assembly,
        "Magicka.Levels.Level",
        "Dispose",
        parameterCount: 0,
        isStatic: false,
        helperName: "MarkMustCollect");

    MethodDefinition playStateDispose = RequireMethod(
        assembly,
        "Magicka.GameLogic.GameStates.PlayState",
        "Dispose",
        parameterCount: 0);
    RequireEarlyLifecycleCall(
        playStateDispose,
        "MarkMustCollect",
        "Magicka.GameLogic.GameStates.PlayState.Dispose",
        maximumInstructionIndex: 8);
    RequireEarlyLifecycleCall(
        playStateDispose,
        "MarkMustCollect",
        "Magicka.GameLogic.GameStates.PlayState.Dispose.Scene",
        maximumInstructionIndex: 8);
}

static void RequireResolvedBranchTargets(AssemblyDefinition assembly)
{
    foreach (MethodDefinition method in AllTypes(assembly.MainModule)
                 .SelectMany(type => type.Methods)
                 .Where(method => method.HasBody))
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if ((instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget
                 || instruction.OpCode.OperandType == OperandType.InlineBrTarget)
                && instruction.Operand is not Instruction)
            {
                throw new InvalidDataException(
                    "Invalid branch target in " + method.FullName
                    + " at IL_" + instruction.Offset.ToString("x4") + ".");
            }
        }
    }
}

static void ValidateClr2Assembly(AssemblyDefinition assembly)
{
    if (assembly.MainModule.RuntimeVersion != "v2.0.50727")
    {
        throw new InvalidDataException(
            assembly.Name.Name + " no longer targets CLR 2.0.");
    }

    AssemblyNameReference[] selfReferences = assembly.MainModule
        .AssemblyReferences
        .Where(reference => string.Equals(
            reference.FullName,
            assembly.Name.FullName,
            StringComparison.Ordinal))
        .ToArray();
    if (selfReferences.Length != 0)
    {
        throw new InvalidDataException(
            assembly.Name.Name + " references its own assembly identity: "
            + string.Join(", ", selfReferences.Select(reference => reference.FullName)));
    }

    string[] forbiddenReferenceNames =
    [
        "System.Private.CoreLib",
        "System.Runtime",
        "netstandard",
    ];
    AssemblyNameReference[] forbidden = assembly.MainModule.AssemblyReferences
        .Where(reference => forbiddenReferenceNames.Contains(
            reference.Name,
            StringComparer.Ordinal))
        .ToArray();
    if (forbidden.Length != 0)
    {
        throw new InvalidDataException(
            assembly.Name.Name + " contains CLR 4+ assembly references: "
            + string.Join(", ", forbidden.Select(reference => reference.FullName)));
    }

    foreach (MethodDefinition method in AllTypes(assembly.MainModule)
                 .SelectMany(type => type.Methods)
                 .Where(method => method.HasBody))
    {
        ValidateBodyReferences(method);
        ValidateClr2FrameworkCalls(method);
    }
}

static void ValidateClr2FrameworkCalls(MethodDefinition method)
{
    foreach (MethodReference called in method.Body.Instructions
                 .Select(instruction => instruction.Operand)
                 .OfType<MethodReference>())
    {
        if (called.DeclaringType.FullName == "System.Threading.Monitor"
            && called.Name == "Enter"
            && called.Parameters.Count == 2
            && called.Parameters[0].ParameterType.FullName == "System.Object"
            && called.Parameters[1].ParameterType.FullName == "System.Boolean&")
        {
            throw new InvalidDataException(
                "CLR-4 Monitor.Enter(object, ref bool) call in "
                + method.FullName + ".");
        }
    }
}

static void ValidateCollectionLocks(AssemblyDefinition magicka)
{
    (string TypeName, string MethodName, int ParameterCount)[] lockMethods =
    [
        ("Magicka.StaticList`1", "Add", 1),
        ("Magicka.StaticList`1", "Insert", 2),
        ("Magicka.StaticWeakList`1", "Add", 1),
        ("Magicka.StaticWeakList`1", "Insert", 2),
        ("Magicka.StaticWeakList`1", "Expand", 1),
    ];
    IReadOnlyDictionary<string, TypeDefinition> types = AllTypes(magicka.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    foreach ((string typeName, string methodName, int parameterCount) in lockMethods)
    {
        MethodDefinition method = types[typeName].Methods.Single(candidate =>
            candidate.Name == methodName
            && candidate.Parameters.Count == parameterCount);
        int enterCalls = method.Body.Instructions.Count(instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Threading.Monitor"
            && called.Name == "Enter"
            && called.Parameters.Count == 1);
        int exitCalls = method.Body.Instructions.Count(instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Threading.Monitor"
            && called.Name == "Exit"
            && called.Parameters.Count == 1);
        bool hasConditionalFinally = method.Body.ExceptionHandlers.Any(handler =>
            handler.HandlerType == ExceptionHandlerType.Finally)
            && method.Body.Instructions.Any(instruction =>
                instruction.OpCode == OpCodes.Brfalse
                || instruction.OpCode == OpCodes.Brfalse_S);
        if (enterCalls != 1 || exitCalls != 1 || !hasConditionalFinally)
        {
            throw new InvalidDataException(
                "Unexpected CLR-2 lock shape in " + method.FullName + ".");
        }
    }
}

static void ValidateRuntimeCompatibilityGuards(AssemblyDefinition magicka)
{
    TypeDefinition guards = AllTypes(magicka.MainModule).Single(type =>
        type.FullName == "Magicka.CommunityPatch.RuntimeCompatibilityGuards");
    MethodDefinition queue = guards.Methods.Single(method =>
        method.Name == "QueueParadoxStorePriceUpdate"
        && method.Parameters.Count == 1);
    TypeReference delegateType = queue.Parameters[0].ParameterType;
    if (delegateType.FullName != "System.Threading.ThreadStart"
        || delegateType.Scope is not AssemblyNameReference scope
        || scope.Name != "mscorlib"
        || scope.Version != new Version(2, 0, 0, 0))
    {
        throw new InvalidDataException(
            "The Paradox price worker delegate is not CLR-2-compatible: "
            + delegateType.FullName + " from " + delegateType.Scope + ".");
    }

    MethodDefinition worker = guards.Methods.Single(method =>
        method.Name == "RunParadoxStorePriceUpdate"
        && method.Parameters.Count == 1);
    bool castsThreadStart = worker.Body.Instructions.Any(instruction =>
        instruction.OpCode == OpCodes.Castclass
        && instruction.Operand is TypeReference type
        && type.FullName == "System.Threading.ThreadStart");
    bool invokesThreadStart = worker.Body.Instructions.Any(instruction =>
        instruction.OpCode == OpCodes.Callvirt
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "System.Threading.ThreadStart"
        && called.Name == "Invoke");
    if (!castsThreadStart || !invokesThreadStart)
    {
        throw new InvalidDataException(
            "The Paradox price worker does not cast and invoke ThreadStart.");
    }
}

static void ValidateArrayEqualsNullGuard(AssemblyDefinition magicka)
{
    MethodDefinition arrayEquals = AllTypes(magicka.MainModule)
        .Single(type => type.FullName == "Magicka.Helper")
        .Methods.Single(method => method.Name == "ArrayEquals"
                                  && method.Parameters.Count == 2);
    Instruction[] instructions = arrayEquals.Body.Instructions.ToArray();
    if (instructions.Length < 8
        || instructions[0].OpCode != OpCodes.Ldarg_0
        || instructions[1].OpCode.FlowControl != FlowControl.Cond_Branch
        || instructions[2].OpCode != OpCodes.Ldarg_1
        || instructions[3].OpCode.FlowControl != FlowControl.Cond_Branch
        || instructions[1].Operand is not Instruction firstNullExit
        || instructions[3].Operand != firstNullExit
        || firstNullExit.OpCode != OpCodes.Ldc_I4_0
        || firstNullExit.Next?.OpCode != OpCodes.Ret)
    {
        throw new InvalidDataException(
            "Helper.ArrayEquals does not reject either null input.");
    }
}

static void ValidateAvatarFindInteractableNullGuard(AssemblyDefinition magicka)
{
    MethodDefinition method = AllTypes(magicka.MainModule)
        .Single(type => type.FullName == "Magicka.GameLogic.Entities.Avatar")
        .Methods.Single(candidate => candidate.Name == "FindInteractable"
                                     && candidate.Parameters.Count == 1);
    Instruction[] instructions = method.Body.Instructions.ToArray();
    Instruction[] nullBranches = instructions.Take(24)
        .Where(instruction => instruction.OpCode.FlowControl
                                  == FlowControl.Cond_Branch
                              && instruction.Operand is Instruction target
                              && target.OpCode == OpCodes.Ldnull
                              && target.Next?.OpCode == OpCodes.Ret)
        .ToArray();
    string[] expectedGetters =
    [
        "Magicka.GameLogic.GameStates.PlayState::get_Level",
        "Magicka.Levels.Level::get_CurrentScene",
        "Magicka.Levels.GameScene::get_Triggers",
    ];
    string[] entryGetters = instructions.Take(24)
        .Where(instruction => instruction.Operand is MethodReference called
                              && expectedGetters.Any(expected =>
                                  called.FullName.Contains(
                                      expected,
                                      StringComparison.Ordinal)))
        .Select(instruction => ((MethodReference)instruction.Operand).Name)
        .ToArray();
    if (nullBranches.Length != 4
        || entryGetters.Length != expectedGetters.Length)
    {
        throw new InvalidDataException(
            "Avatar.FindInteractable does not guard the captured scene chain.");
    }
}

static void ValidateCharacterCastSpellGamerNullGuard(AssemblyDefinition magicka)
{
    MethodDefinition helper = AllTypes(magicka.MainModule)
        .Single(type => type.FullName
                        == "Magicka.CommunityPatch.RuntimeCompatibilityGuards")
        .Methods.Single(candidate =>
            candidate.Name == "CanRecordLocalSpellUsage"
            && candidate.Parameters.Count == 1);
    if (helper.Parameters[0].ParameterType.FullName != "Magicka.Gamers.Gamer"
        || !helper.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Brfalse
            && instruction.Operand is Instruction target
            && target.OpCode == OpCodes.Ldc_I4_0
            && target.Next?.OpCode == OpCodes.Ret)
        || !helper.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Isinst
            && instruction.Operand is TypeReference type
            && type.FullName == "Magicka.Gamers.NetworkGamer"))
    {
        throw new InvalidDataException(
            "CanRecordLocalSpellUsage does not reject detached and remote Gamers.");
    }

    MethodDefinition method = AllTypes(magicka.MainModule)
        .Single(type => type.FullName == "Magicka.GameLogic.Entities.Character")
        .Methods.Single(candidate => candidate.Name == "CastSpell"
                                     && candidate.Parameters.Count == 2);
    Instruction usedElements = method.Body.Instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "Magicka.GameLogic.Profile"
        && called.Name == "UsedElements");
    int usedElementsIndex = method.Body.Instructions.IndexOf(usedElements);
    Instruction getGamer = method.Body.Instructions.Take(usedElementsIndex)
        .Last(instruction => instruction.Operand is MethodReference called
                             && called.DeclaringType.FullName
                                 == "Magicka.GameLogic.Player"
                             && called.Name == "get_Gamer");
    Instruction helperCall = getGamer.Next
        ?? throw new InvalidDataException(
            "Character.CastSpell Gamer load has no guard call.");
    Instruction skipBranch = helperCall.Next
        ?? throw new InvalidDataException(
            "Character.CastSpell Gamer guard has no statistics branch.");
    if (helperCall.OpCode != OpCodes.Call
        || helperCall.Operand is not MethodReference calledHelper
        || calledHelper.FullName != helper.FullName
        || skipBranch.OpCode.FlowControl != FlowControl.Cond_Branch
        || skipBranch.OpCode.Code is not Code.Brfalse and not Code.Brfalse_S
        || skipBranch.Operand is not Instruction skipStatistics
        || method.Body.Instructions.IndexOf(skipStatistics) <= usedElementsIndex
        || method.Body.Instructions
            .Skip(method.Body.Instructions.IndexOf(getGamer))
            .Take(3)
            .Any(instruction => instruction.OpCode == OpCodes.Isinst))
    {
        throw new InvalidDataException(
            "Character.CastSpell does not skip only optional statistics when Gamer is null.");
    }
}

static void ValidateCharacterSelectDisposedIconGuard(AssemblyDefinition magicka)
{
    MethodDefinition method = AllTypes(magicka.MainModule)
        .Single(type => type.FullName
                        == "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect")
        .Methods.Single(candidate => candidate.Name == "DrawWidget"
                                     && candidate.Parameters.Count == 1);
    Instruction[] entry = method.Body.Instructions.Take(13).ToArray();
    Instruction finalReturn = method.Body.Instructions.Last();
    if (entry.Length != 13
        || entry[0].OpCode != OpCodes.Ldarg_1
        || entry[1].OpCode != OpCodes.Isinst
        || entry[1].Operand is not TypeReference image
        || image.FullName != "Magicka.GameLogic.UI.UISystem.Image"
        || !entry.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "Magicka.GameLogic.UI.UISystem.Image"
            && called.Name == "get_Texture")
        || !entry.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "Microsoft.Xna.Framework.Graphics.GraphicsResource"
            && called.Name == "get_IsDisposed")
        || entry.Count(instruction =>
            instruction.OpCode.FlowControl == FlowControl.Cond_Branch
            && instruction.Operand == finalReturn) != 2)
    {
        throw new InvalidDataException(
            "SubMenuCharacterSelect.DrawWidget does not skip null or disposed image textures.");
    }
}

static void ValidateNetworkClientRulesetTeardownGuard(AssemblyDefinition magicka)
{
    TypeDefinition compatibility = AllTypes(magicka.MainModule).Single(type =>
        type.FullName
        == "Magicka.CommunityPatch.NetworkLifecycleCompatibility");
    MethodDefinition helper = compatibility.Methods.Single(method =>
        method.Name == "ApplyRulesetUpdate"
        && method.Parameters.Count == 2);
    string[] expectedGetters =
    [
        "Magicka.GameLogic.GameStates.PlayState::get_Level",
        "Magicka.Levels.Level::get_CurrentScene",
        "Magicka.Levels.GameScene::get_RuleSet",
    ];
    if (helper.Parameters[0].ParameterType.FullName
            != "Magicka.GameLogic.GameStates.PlayState"
        || helper.Parameters[1].ParameterType.FullName
            != "Magicka.Network.RulesetMessage&"
        || expectedGetters.Any(expected =>
            !helper.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference called
                && called.FullName.Contains(expected, StringComparison.Ordinal)))
        || helper.Body.Instructions.Count(instruction =>
            instruction.OpCode.FlowControl == FlowControl.Cond_Branch
            && instruction.Operand is Instruction target
            && target.OpCode == OpCodes.Ldstr
            && string.Equals(
                target.Operand as string,
                "client",
                StringComparison.Ordinal))
            != 4
        || !helper.Body.Instructions.Any(instruction =>
            string.Equals(
                instruction.Operand as string,
                "ruleset_update_ignored_not_ready",
                StringComparison.Ordinal))
        || !helper.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "Magicka.Levels.IRuleset"
            && called.Name == "NetworkUpdate"))
    {
        throw new InvalidDataException(
            "ApplyRulesetUpdate does not guard and report the full play-state chain.");
    }

    MethodDefinition readMessage = AllTypes(magicka.MainModule)
        .Single(type => type.FullName == "Magicka.Network.NetworkClient")
        .Methods.Single(method => method.Name == "ReadMessage"
                                  && method.Parameters.Count == 2);
    Instruction[] helperCalls = readMessage.Body.Instructions.Where(
        instruction => instruction.OpCode == OpCodes.Call
                       && instruction.Operand is MethodReference called
                       && called.FullName == helper.FullName).ToArray();
    Instruction helperCall = helperCalls.Length == 1
        ? helperCalls[0]
        : throw new InvalidDataException(
            "NetworkClient.ReadMessage has an unexpected RulesetUpdate guard call count.");
    if (helperCall.Previous?.OpCode.Code
            is not Code.Ldloca and not Code.Ldloca_S
        || helperCall.Previous?.Previous?.OpCode.Code
            is not Code.Ldloc and not Code.Ldloc_S
        || readMessage.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "Magicka.Levels.IRuleset"
            && called.Name == "NetworkUpdate"))
    {
        throw new InvalidDataException(
            "NetworkClient.ReadMessage does not route RulesetUpdate through the teardown guard.");
    }
}

static void ValidateGraphicsStartupErrors(AssemblyDefinition magicka)
{
    MethodDefinition constructor = AllTypes(magicka.MainModule)
        .Single(type => type.FullName == "Magicka.Game")
        .Methods.Single(method => method.IsConstructor
                                  && !method.IsStatic
                                  && method.Parameters.Count == 0);
    if (constructor.Body.Instructions.Count(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "Microsoft.Xna.Framework.GraphicsDeviceManager"
            && called.Name == "ApplyChanges") != 1)
    {
        throw new InvalidDataException(
            "Game constructor graphics startup call changed unexpectedly.");
    }
    MethodDefinition writeReport = AllTypes(magicka.MainModule)
        .Single(type => type.FullName == "Magicka.Program")
        .Methods.Single(method => method.Name == "WriteReport"
                                  && method.Parameters.Count == 2);
    Instruction[] entry = writeReport.Body.Instructions.Take(48).ToArray();
    if (!entry.Any(instruction =>
            instruction.OpCode == OpCodes.Isinst
            && instruction.Operand is TypeReference type
            && type.FullName == "System.ArgumentException")
        || !entry.Any(instruction =>
            string.Equals(
                instruction.Operand as string,
                "Microsoft.Xna.Framework",
                StringComparison.Ordinal))
        || !entry.Any(instruction =>
            string.Equals(
                instruction.Operand as string,
                "Microsoft.Xna.Framework.NoSuitableGraphicsDeviceException",
                StringComparison.Ordinal))
        || !entry.Any(instruction =>
            (instruction.Operand as string)?.Contains(
                "graphics adapter to a monitor",
                StringComparison.Ordinal) == true)
        || !entry.Any(instruction =>
            (instruction.Operand as string)?.Contains(
                "suitable graphics device",
                StringComparison.Ordinal) == true)
        || entry.Count(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Windows.Forms.MessageBox"
            && called.Name == "Show") != 2)
    {
        throw new InvalidDataException(
            "Program.WriteReport does not show both actionable graphics startup errors.");
    }
}

static void ValidateGcDiagnosticsStartupCheck(AssemblyDefinition magicka)
{
    MethodDefinition main = AllTypes(magicka.MainModule)
        .Single(type => type.FullName == "Magicka.Program")
        .Methods.Single(method => method.Name == "Main"
                                  && method.Parameters.Count == 1);
    Instruction[] instructions = main.Body.Instructions.ToArray();
    int setDirectory = Array.FindIndex(instructions, instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "System.IO.Directory"
        && called.Name == "SetCurrentDirectory");
    int fileExists = Array.FindIndex(instructions, instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "System.IO.File"
        && called.Name == "Exists");
    int startupTelemetry = Array.FindIndex(instructions, instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName
            == "Magicka.CommunityPatch.PatchTelemetry"
        && called.Name == "SendStartup");
    if (setDirectory < 0
        || fileExists <= setDirectory
        || startupTelemetry <= fileExists
        || !instructions.Skip(setDirectory).Take(fileExists - setDirectory + 1)
            .Any(instruction =>
            string.Equals(
                instruction.Operand as string,
                "Magicka.GcDiagnostics.dll",
                StringComparison.Ordinal))
        || !instructions.Skip(fileExists).Take(16).Any(instruction =>
            (instruction.Operand as string)?.Contains(
                "remaining memory leaks and out-of-memory errors",
                StringComparison.Ordinal) == true)
        || !instructions.Skip(fileExists).Take(16).Any(instruction =>
            instruction.OpCode == OpCodes.Ldc_I4_1
            && instruction.Next?.OpCode == OpCodes.Ret))
    {
        throw new InvalidDataException(
            "Program.Main does not stop early with the GC diagnostics installation message.");
    }
}

static void ValidateLevelHashMissingFile(AssemblyDefinition magicka)
{
    MethodDefinition method = AllTypes(magicka.MainModule)
        .Single(type => type.FullName
                        == "Magicka.Levels.Campaign.LevelManager")
        .Methods.Single(candidate => candidate.Name == "ComputeHashes"
                                     && candidate.Parameters.Count == 0);
    ExceptionHandler handler = method.Body.ExceptionHandlers.Single(candidate =>
        candidate.HandlerType == ExceptionHandlerType.Catch
        && candidate.CatchType?.FullName == "System.IO.FileNotFoundException");
    int start = method.Body.Instructions.IndexOf(handler.HandlerStart);
    int end = method.Body.Instructions.IndexOf(handler.HandlerEnd);
    Instruction[] body = method.Body.Instructions.Skip(start)
        .Take(end - start)
        .ToArray();
    if (!body.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "System.IO.FileNotFoundException"
            && called.Name == "get_FileName")
        || !body.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "System.Windows.Forms.MessageBox"
            && called.Name == "Show")
        || !body.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Environment"
            && called.Name == "Exit"
            && called.Parameters.Count == 1)
        || body.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "Magicka.CommunityPatch.PatchTelemetry"))
    {
        throw new InvalidDataException(
            "LevelManager.ComputeHashes does not report the missing file locally without telemetry.");
    }
}

static void ValidateNetworkServerForcedSyncExit(AssemblyDefinition magicka)
{
    TypeDefinition networkServer = AllTypes(magicka.MainModule).Single(type =>
        type.FullName == "Magicka.Network.NetworkServer");
    MethodDefinition readMessage = networkServer.Methods.Single(method =>
        method.Name == "ReadMessage"
        && method.Parameters.Count == 2);
    Instruction[] instructions = readMessage.Body.Instructions.ToArray();
    Instruction[] sendCalls = instructions.Where(instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "Magicka.Network.NetworkServer"
            && called.Name == "SendForcedSyncMessageToClient"
            && called.Parameters.Count == 2
            && called.Parameters[0].ParameterType.FullName
                == "SteamWrapper.SteamID"
            && called.Parameters[1].ParameterType.FullName == "System.Boolean")
        .ToArray();
    if (sendCalls.Length != 1)
    {
        throw new InvalidDataException(
            "Expected one SteamID forced-sync send in NetworkServer.ReadMessage;"
            + " found " + sendCalls.Length + ".");
    }

    ExceptionHandler ioHandler = readMessage.Body.ExceptionHandlers.Single(
        handler => handler.HandlerType == ExceptionHandlerType.Catch
            && handler.CatchType?.FullName == "System.IO.IOException");
    Instruction expectedExit = ioHandler.TryEnd.Previous
        ?? throw new InvalidDataException(
            "NetworkServer.ReadMessage main try exit is missing.");
    Instruction? branch = sendCalls[0].Next;
    while (branch?.OpCode == OpCodes.Nop)
    {
        branch = branch.Next;
    }
    if (branch is null
        || branch.OpCode.FlowControl != FlowControl.Branch
        || branch.Operand != expectedExit)
    {
        throw new InvalidDataException(
            "RequestForcedPlayerStatusSync does not exit NetworkServer.ReadMessage"
            + " after sending its response.");
    }
}

static void RequireEntryLifecycleHook(
    AssemblyDefinition assembly,
    string typeName,
    string methodName,
    int parameterCount,
    bool isStatic,
    string helperName)
{
    MethodDefinition method = RequireMethod(
        assembly,
        typeName,
        methodName,
        parameterCount);
    if (method.IsStatic != isStatic)
    {
        throw new InvalidDataException(
            "Unexpected static flag for " + method.FullName);
    }

    Instruction[] instructions = method.Body.Instructions.ToArray();
    string expectedLifecycle = typeName + "." + methodName;
    if (instructions.Length < 3
        || instructions[0].OpCode != OpCodes.Ldarg_0
        || instructions[1].OpCode != OpCodes.Ldstr
        || !string.Equals(
            instructions[1].Operand as string,
            expectedLifecycle,
            StringComparison.Ordinal)
        || !CallsRuntimeHelper(instructions[2], helperName))
    {
        throw new InvalidDataException(
            "Missing entry lifecycle hook for "
            + expectedLifecycle
            + " in "
            + method.FullName);
    }

    int hookCount = 0;
    for (int index = 1; index < instructions.Length; index++)
    {
        if (CallsRuntimeHelper(instructions[index], helperName)
            && instructions[index - 1].OpCode == OpCodes.Ldstr
            && string.Equals(
                instructions[index - 1].Operand as string,
                expectedLifecycle,
                StringComparison.Ordinal))
        {
            hookCount++;
        }
    }

    if (hookCount != 1)
    {
        throw new InvalidDataException(
            method.FullName + " has " + hookCount
            + " entry hooks; expected exactly one.");
    }
}

static void RequireReturnLifecycleHook(
    AssemblyDefinition assembly,
    string typeName,
    string methodName,
    int parameterCount,
    bool isStatic,
    string helperName,
    string? lifecycle = null)
{
    MethodDefinition method = RequireMethod(
        assembly,
        typeName,
        methodName,
        parameterCount);
    if (method.IsStatic != isStatic)
    {
        throw new InvalidDataException(
            "Unexpected static flag for " + method.FullName);
    }

    if (isStatic
        && (method.Parameters.Count != 1
            || method.Parameters[0].ParameterType.FullName != typeName))
    {
        throw new InvalidDataException(
            "Unexpected pool-sink parameter for " + method.FullName);
    }

    Instruction[] instructions = method.Body.Instructions.ToArray();
    int returnCount = instructions.Count(
        instruction => instruction.OpCode == OpCodes.Ret);
    int hookCount = 0;
    string expectedLifecycle =
        lifecycle ?? typeName + "." + methodName;
    for (int index = 2; index < instructions.Length; index++)
    {
        if (!CallsRuntimeHelper(instructions[index], helperName))
        {
            continue;
        }

        if (instructions[index - 1].OpCode != OpCodes.Ldstr)
        {
            throw new InvalidDataException(
                "Lifecycle helper has no lifecycle string in "
                + method.FullName);
        }

        string? actualLifecycle = instructions[index - 1].Operand as string;
        if (string.Equals(
                actualLifecycle,
                expectedLifecycle + ".CacheInsert",
                StringComparison.Ordinal))
        {
            if (!IsStaticCacheInsertionHook(
                    method,
                    instructions,
                    index))
            {
                throw new InvalidDataException(
                    "Malformed direct static cache insertion hook in "
                    + method.FullName);
            }

            continue;
        }

        hookCount++;
        if (instructions[index - 2].OpCode != OpCodes.Ldarg_0
            || !string.Equals(
                actualLifecycle,
                expectedLifecycle,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Malformed lifecycle hook in " + method.FullName);
        }
    }

    if (hookCount != returnCount || hookCount == 0)
    {
        throw new InvalidDataException(
            method.FullName + " has " + hookCount
            + " lifecycle hooks for " + returnCount + " returns.");
    }
}

static bool IsStaticCacheInsertionHook(
    MethodDefinition method,
    IReadOnlyList<Instruction> instructions,
    int helperIndex)
{
    if (helperIndex < 5
        || instructions[helperIndex - 3].Operand
            is not MethodReference collectionCall
        || !IsCacheInsertionCall(collectionCall)
        || instructions[helperIndex - 2].OpCode != OpCodes.Ldarg_0)
    {
        return false;
    }

    int callIndex = helperIndex - 3;
    int firstIndex = Math.Max(0, callIndex - 8);
    int cacheLoadIndex = -1;
    int capturedOwnerIndex = -1;
    for (int index = firstIndex; index < callIndex; index++)
    {
        Instruction instruction = instructions[index];
        if (instruction.OpCode == OpCodes.Ldsfld
            && instruction.Operand is FieldReference cacheField
            && IsCacheOrPoolField(cacheField.Name)
            && CacheFieldHoldsOwner(
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

    if (cacheLoadIndex < firstIndex)
    {
        return false;
    }

    bool directOwnerLoad = callIndex >= 1
        && instructions[callIndex - 1].OpCode == OpCodes.Ldarg_0;
    bool capturedOwnerLoad =
        capturedOwnerIndex > cacheLoadIndex
        && capturedOwnerIndex < callIndex;
    return directOwnerLoad || capturedOwnerLoad;
}

static void RequireDirectCacheInsertionHook(
    AssemblyDefinition assembly,
    string typeName,
    string methodName,
    int parameterCount,
    string helperName = "MarkActive")
{
    MethodDefinition method = RequireMethod(
        assembly,
        typeName,
        methodName,
        parameterCount);
    if (method.IsStatic)
    {
        throw new InvalidDataException(
            "Expected an instance cache insertion in " + method.FullName);
    }

    Instruction[] instructions = method.Body.Instructions.ToArray();
    string expectedLifecycle = typeName + "." + methodName + ".CacheInsert";
    int hookCount = 0;
    for (int index = 5; index < instructions.Length; index++)
    {
        if (!CallsRuntimeHelper(instructions[index], "MarkMustDetach")
            || instructions[index - 1].OpCode != OpCodes.Ldstr
            || !string.Equals(
                instructions[index - 1].Operand as string,
                expectedLifecycle,
                StringComparison.Ordinal))
        {
            continue;
        }

        hookCount++;
        Instruction cacheLoad = instructions[index - 5];
        Instruction cachedObjectLoad = instructions[index - 4];
        Instruction cacheCall = instructions[index - 3];
        Instruction trackedObjectLoad = instructions[index - 2];
        if (cacheLoad.OpCode != OpCodes.Ldsfld
            || cacheLoad.Operand is not FieldReference cacheField
            || !IsCacheOrPoolField(cacheField.Name)
            || !CacheFieldHoldsOwner(cacheField, method.DeclaringType)
            || cachedObjectLoad.OpCode != OpCodes.Ldarg_0
            || cacheCall.Operand is not MethodReference collectionCall
            || !IsCacheInsertionCall(collectionCall)
            || trackedObjectLoad.OpCode != OpCodes.Ldarg_0)
        {
            throw new InvalidDataException(
                "Malformed direct cache insertion hook in " + method.FullName);
        }
    }

    if (hookCount != 1)
    {
        throw new InvalidDataException(
            method.FullName + " has " + hookCount
            + " direct cache insertion hooks; expected exactly one.");
    }
}

static void RequireActiveCacheSourceReturns(
    AssemblyDefinition assembly,
    string typeName,
    string methodName,
    int parameterCount,
    string helperName = "MarkActive")
{
    MethodDefinition method = RequireMethod(
        assembly,
        typeName,
        methodName,
        parameterCount);
    if (!method.IsStatic
        || method.ReturnType.FullName != typeName)
    {
        throw new InvalidDataException(
            "Unexpected cache-source signature for " + method.FullName);
    }

    Instruction[] instructions = method.Body.Instructions.ToArray();
    bool readsCache = instructions.Any(
        instruction => (instruction.OpCode == OpCodes.Ldsfld
                        || instruction.OpCode == OpCodes.Ldsflda)
                       && instruction.Operand is FieldReference field
                       && IsCacheOrPoolField(field.Name));
    if (!readsCache)
    {
        throw new InvalidDataException(
            "Cache-source method does not read a cache field: "
            + method.FullName);
    }

    int returnCount = instructions.Count(
        instruction => instruction.OpCode == OpCodes.Ret);
    int hookCount = 0;
    string expectedLifecycle = typeName + "." + methodName;
    for (int index = 2; index + 1 < instructions.Length; index++)
    {
        if (!CallsRuntimeHelper(instructions[index], helperName))
        {
            continue;
        }

        hookCount++;
        if (instructions[index - 2].OpCode != OpCodes.Dup
            || instructions[index - 1].OpCode != OpCodes.Ldstr
            || !string.Equals(
                instructions[index - 1].Operand as string,
                expectedLifecycle,
                StringComparison.Ordinal)
            || instructions[index + 1].OpCode != OpCodes.Ret)
        {
            throw new InvalidDataException(
                "Malformed active cache-source hook in " + method.FullName);
        }
    }

    if (hookCount != returnCount || hookCount == 0)
    {
        throw new InvalidDataException(
            method.FullName + " has " + hookCount
            + " active hooks for " + returnCount + " cache-source returns.");
    }
}

static void RequireInstanceResidentSourceReturns(
    AssemblyDefinition assembly,
    string typeName,
    string methodName,
    int parameterCount,
    string returnTypeName)
{
    MethodDefinition method = RequireMethod(
        assembly,
        typeName,
        methodName,
        parameterCount);
    if (method.IsStatic
        || method.ReturnType.FullName != returnTypeName)
    {
        throw new InvalidDataException(
            "Unexpected resident instance-source signature for "
            + method.FullName);
    }

    Instruction[] instructions = method.Body.Instructions.ToArray();
    bool readsInstanceCache = instructions.Any(
        instruction => (instruction.OpCode == OpCodes.Ldfld
                        || instruction.OpCode == OpCodes.Ldflda)
                       && instruction.Operand is FieldReference field
                       && IsCacheOrPoolField(field.Name));
    if (!readsInstanceCache)
    {
        throw new InvalidDataException(
            "Resident instance-source method does not read a cache field: "
            + method.FullName);
    }

    int returnCount = instructions.Count(
        instruction => instruction.OpCode == OpCodes.Ret);
    int hookCount = 0;
    string expectedLifecycle = typeName + "." + methodName;
    for (int index = 2; index + 1 < instructions.Length; index++)
    {
        if (!CallsRuntimeHelper(instructions[index], "MarkResidentActive"))
        {
            continue;
        }

        hookCount++;
        if (instructions[index - 2].OpCode != OpCodes.Dup
            || instructions[index - 1].OpCode != OpCodes.Ldstr
            || !string.Equals(
                instructions[index - 1].Operand as string,
                expectedLifecycle,
                StringComparison.Ordinal)
            || instructions[index + 1].OpCode != OpCodes.Ret)
        {
            throw new InvalidDataException(
                "Malformed resident instance-source hook in "
                + method.FullName);
        }
    }

    if (hookCount != returnCount || hookCount == 0)
    {
        throw new InvalidDataException(
            method.FullName + " has " + hookCount
            + " resident hooks for " + returnCount + " returns.");
    }
}

static bool IsCacheInsertionCall(MethodReference method)
{
    return method.Name == "Add"
           || method.Name == "Enqueue"
           || method.Name == "Push";
}

static bool IsCacheOrPoolField(string fieldName)
{
    return (fieldName.Contains("cache", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("pool", StringComparison.OrdinalIgnoreCase))
           && !fieldName.Contains(
               "active",
               StringComparison.OrdinalIgnoreCase);
}

static bool CacheFieldHoldsOwner(
    FieldReference field,
    TypeDefinition owner)
{
    return field.FieldType is GenericInstanceType collection
           && collection.GenericArguments.Any(
               argument => argument.FullName == owner.FullName);
}

static void RequireEarlyLifecycleCall(
    MethodDefinition method,
    string helperName,
    string lifecycle,
    int maximumInstructionIndex)
{
    Instruction[] instructions = method.Body.Instructions.ToArray();
    for (int index = 1;
         index < instructions.Length && index <= maximumInstructionIndex;
         index++)
    {
        if (CallsRuntimeHelper(instructions[index], helperName)
            && instructions[index - 1].OpCode == OpCodes.Ldstr
            && string.Equals(
                instructions[index - 1].Operand as string,
                lifecycle,
                StringComparison.Ordinal))
        {
            return;
        }
    }

    throw new InvalidDataException(
        "Missing early " + helperName + " hook for " + lifecycle
        + " in " + method.FullName);
}

static bool CallsRuntimeHelper(Instruction instruction, string helperName)
{
    return instruction.Operand is MethodReference called
           && called.DeclaringType.FullName == RegistryTypeName
           && called.Name == helperName;
}

static void ValidateLightSceneDetach(
    AssemblyDefinition magicka,
    AssemblyDefinition polygonHead)
{
    TypeDefinition light = AllTypes(polygonHead.MainModule).Single(
        type => type.FullName == "PolygonHead.Lights.Light");
    TypeDefinition scene = AllTypes(polygonHead.MainModule).Single(
        type => type.FullName == "PolygonHead.Scene");
    FieldDefinition sceneField = light.Fields.Single(
        field => field.Name == "mScene"
                 && field.FieldType.FullName == scene.FullName);
    MethodDefinition removeLight = scene.Methods.Single(
        method => method.Name == "RemoveLight"
                  && method.Parameters.Count == 1);
    MethodDefinition onRemove = RequireMethod(
        polygonHead,
        light.FullName,
        "OnRemove",
        parameterCount: 0);

    Instruction[] cleanup = onRemove.Body.Instructions.ToArray();
    int loadSceneIndex = Array.FindIndex(
        cleanup,
        instruction => instruction.OpCode == OpCodes.Ldfld
                       && IsReferenceTo(instruction.Operand, sceneField));
    int clearSceneIndex = Array.FindIndex(
        cleanup,
        instruction => instruction.OpCode == OpCodes.Stfld
                       && IsReferenceTo(instruction.Operand, sceneField));
    int removeLightIndex = Array.FindIndex(
        cleanup,
        instruction => IsCallTo(instruction, removeLight));
    if (loadSceneIndex < 0
        || clearSceneIndex <= loadSceneIndex
        || clearSceneIndex == 0
        || cleanup[clearSceneIndex - 1].OpCode != OpCodes.Ldnull
        || removeLightIndex <= clearSceneIndex)
    {
        throw new InvalidDataException(
            "Light.OnRemove does not capture, clear, and remove its scene"
            + " in the required order.");
    }

    foreach ((string Name, int ParameterCount) caller in new[]
             {
                 ("Disable", 2),
                 ("Update", 4),
             })
    {
        MethodDefinition method = RequireMethod(
            polygonHead,
            light.FullName,
            caller.Name,
            caller.ParameterCount);
        int onRemoveCalls = method.Body.Instructions.Count(
            instruction => IsCallTo(instruction, onRemove));
        int duplicateRemoveCalls = method.Body.Instructions.Count(
            instruction => IsCallTo(instruction, removeLight));
        if (onRemoveCalls != 1 || duplicateRemoveCalls != 0)
        {
            throw new InvalidDataException(
                method.FullName + " must call Light.OnRemove once and must"
                + " not remove the scene a second time.");
        }
    }

    MethodDefinition dynamicOnRemove = RequireMethod(
        magicka,
        "Magicka.Graphics.Lights.DynamicLight",
        "OnRemove",
        parameterCount: 0);
    Instruction[] dynamicBody = dynamicOnRemove.Body.Instructions.ToArray();
    int baseCleanupIndex = Array.FindIndex(
        dynamicBody,
        instruction => IsCallTo(instruction, onRemove));
    int cacheInsertIndex = Array.FindIndex(
        dynamicBody,
        instruction => instruction.Operand is MethodReference called
                       && called.Name == "Enqueue"
                       && called.DeclaringType.FullName.Contains(
                           "Magicka.Graphics.Lights.DynamicLight",
                           StringComparison.Ordinal));
    if (baseCleanupIndex < 0 || cacheInsertIndex <= baseCleanupIndex)
    {
        throw new InvalidDataException(
            "DynamicLight.OnRemove must detach through base.OnRemove before"
            + " publishing the light to its cache.");
    }
}

static void ValidateRainSceneDetach(AssemblyDefinition magicka)
{
    IReadOnlyDictionary<string, TypeDefinition> types =
        AllTypes(magicka.MainModule).ToDictionary(
            type => type.FullName,
            StringComparer.Ordinal);
    TypeDefinition rain =
        types["Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Rain"];
    TypeDefinition thunderstorm =
        types["Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Thunderstorm"];
    TypeDefinition gameScene = types["Magicka.Levels.GameScene"];
    FieldDefinition sceneField = rain.Fields.Single(field =>
        field.Name == "mScene"
        && field.FieldType.FullName == gameScene.FullName);
    FieldDefinition casterField = rain.Fields.Single(field =>
        field.Name == "mCaster"
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.ISpellCaster");
    FieldDefinition ownerField = thunderstorm.Fields.Single(field =>
        field.Name == "mOwner"
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.ISpellCaster");
    FieldDefinition rainField = thunderstorm.Fields.Single(field =>
        field.Name == "mRain"
        && field.FieldType.FullName == rain.FullName);
    MethodDefinition setLightTargetIntensity = RequireMethod(
        magicka,
        gameScene.FullName,
        "set_LightTargetIntensity",
        parameterCount: 1);
    MethodDefinition rainOnRemove = RequireMethod(
        magicka,
        rain.FullName,
        "OnRemove",
        parameterCount: 0);
    Instruction[] rainBody = rainOnRemove.Body.Instructions.ToArray();

    int sceneLoadIndex = Array.FindIndex(
        rainBody,
        instruction => instruction.OpCode == OpCodes.Ldfld
                       && IsReferenceTo(instruction.Operand, sceneField));
    int sceneClearIndex = FindNullFieldStore(rainBody, sceneField);
    int casterClearIndex = FindNullFieldStore(rainBody, casterField);
    int lightRestoreIndex = Array.FindIndex(
        rainBody,
        instruction => IsCallTo(instruction, setLightTargetIntensity));
    if (sceneLoadIndex < 0
        || sceneClearIndex <= sceneLoadIndex
        || casterClearIndex <= sceneClearIndex
        || lightRestoreIndex <= casterClearIndex)
    {
        throw new InvalidDataException(
            "Rain.OnRemove must capture its scene, clear mScene and mCaster,"
            + " and then restore the preserved scene light.");
    }

    if (!rainBody.Skip(casterClearIndex + 1)
            .Take(lightRestoreIndex - casterClearIndex - 1)
            .Any(instruction => instruction.OpCode == OpCodes.Brfalse
                                || instruction.OpCode == OpCodes.Brfalse_S))
    {
        throw new InvalidDataException(
            "Rain.OnRemove does not guard a missing preserved scene.");
    }

    MethodDefinition thunderstormOnRemove = RequireMethod(
        magicka,
        thunderstorm.FullName,
        "OnRemove",
        parameterCount: 0);
    Instruction[] thunderstormBody =
        thunderstormOnRemove.Body.Instructions.ToArray();
    if (FindNullFieldStore(thunderstormBody, ownerField) < 0)
    {
        throw new InvalidDataException(
            "Thunderstorm.OnRemove does not clear mOwner.");
    }

    if (thunderstormBody.Any(instruction =>
            instruction.OpCode == OpCodes.Stfld
            && IsReferenceTo(instruction.Operand, rainField)))
    {
        throw new InvalidDataException(
            "Thunderstorm.OnRemove must preserve its permanent mRain"
            + " singleton dependency.");
    }
}

static void ValidateShadowBlobsSceneDetach(AssemblyDefinition magicka)
{
    TypeDefinition shadowBlobs = AllTypes(magicka.MainModule).Single(type =>
        type.FullName == "Magicka.GameLogic.UI.ShadowBlobs");
    FieldDefinition scene = shadowBlobs.Fields.Single(field =>
        field.Name == "mScene"
        && field.FieldType.FullName == "PolygonHead.Scene");
    MethodDefinition detach = shadowBlobs.Methods.Single(method =>
        method.Name == "CommunityPatchDetachScene"
        && !method.IsStatic
        && method.Parameters.Count == 1
        && method.Parameters[0].ParameterType.FullName == "PolygonHead.Scene");
    Instruction[] detachBody = detach.Body.Instructions.ToArray();
    if (FindNullFieldStore(detachBody, scene) < 0
        || !detachBody.Any(instruction =>
            instruction.OpCode == OpCodes.Bne_Un
            || instruction.OpCode == OpCodes.Bne_Un_S))
    {
        throw new InvalidDataException(
            "ShadowBlobs scene detach is not conditional on the expected scene.");
    }

    MethodDefinition playStateDispose = RequireMethod(
        magicka,
        "Magicka.GameLogic.GameStates.PlayState",
        "Dispose",
        parameterCount: 0);
    FieldDefinition playStateScene = AllTypes(magicka.MainModule)
        .Single(type =>
            type.FullName == "Magicka.GameLogic.GameStates.GameState")
        .Fields.Single(field =>
            field.Name == "mScene"
            && field.FieldType.FullName == "PolygonHead.Scene");
    Instruction[] disposeBody = playStateDispose.Body.Instructions.ToArray();
    int detachCall = Array.FindIndex(
        disposeBody,
        instruction => IsCallTo(instruction, detach));
    int sceneReset = Array.FindIndex(
        disposeBody,
        instruction => instruction.OpCode == OpCodes.Stfld
            && IsReferenceTo(instruction.Operand, playStateScene));
    if (detachCall < 0 || sceneReset <= detachCall)
    {
        throw new InvalidDataException(
            "PlayState.Dispose does not detach ShadowBlobs before clearing"
            + " its scene.");
    }
}

static void ValidatePhysicsManagerClearReferences(AssemblyDefinition magicka)
{
    MethodDefinition clear = RequireMethod(
        magicka,
        "Magicka.Physics.PhysicsManager",
        "Clear",
        parameterCount: 0);
    Instruction[] body = clear.Body.Instructions.ToArray();
    int skinSnapshot = Array.FindIndex(
        body,
        instruction => instruction.OpCode == OpCodes.Newobj
            && instruction.Operand is MethodReference constructor
            && constructor.DeclaringType.FullName
                == "System.Collections.Generic.List`1<"
                   + "JigLibX.Collision.CollisionSkin>");
    int removeBody = Array.FindIndex(
        body,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "JigLibX.Physics.PhysicsSystem"
            && called.Name == "RemoveBody");
    int clearBodySkin = Array.FindIndex(
        body,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "JigLibX.Physics.Body"
            && called.Name == "set_CollisionSkin"
            && instruction.Previous?.OpCode == OpCodes.Ldnull);
    int clearBodyTag = Array.FindIndex(
        body,
        instruction => instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName == "JigLibX.Physics.Body"
            && field.Name == "Tag"
            && instruction.Previous?.OpCode == OpCodes.Ldnull);
    int removeSkin = Array.FindIndex(
        body,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "JigLibX.Collision.CollisionSystem"
            && called.Name == "RemoveCollisionSkin");
    string[] requiredSkinCleanup =
    [
        "set_Owner",
        "set_CollisionSystem",
        "set_Tag",
        "get_Collisions",
        "get_NonCollidables",
        "RemoveAllPrimitives",
    ];
    int[] skinCleanupIndices = requiredSkinCleanup.Select(name =>
        Array.FindIndex(
            body,
            instruction => instruction.Operand is MethodReference called
                && called.DeclaringType.FullName
                    == "JigLibX.Collision.CollisionSkin"
                && called.Name == name)).ToArray();
    if (skinSnapshot < 0
        || removeBody <= skinSnapshot
        || clearBodySkin <= removeBody
        || clearBodyTag <= removeBody
        || removeSkin < 0
        || skinCleanupIndices.Any(index => index < 0 || index >= removeSkin))
    {
        throw new InvalidDataException(
            "PhysicsManager.Clear does not preserve and clear body/skin"
            + " references before losing the simulator collections.");
    }
}

static void ValidateMeteorShowerRemoveReferences(AssemblyDefinition magicka)
{
    TypeDefinition meteor = AllTypes(magicka.MainModule).Single(type =>
        type.FullName
            == "Magicka.GameLogic.Entities.Abilities.SpecialAbilities."
               + "MeteorShower");
    MethodDefinition onRemove = meteor.Methods.Single(method =>
        method.Name == "OnRemove" && method.Parameters.Count == 0);
    Instruction[] body = onRemove.Body.Instructions.ToArray();
    FieldDefinition scene = meteor.Fields.Single(field => field.Name == "mScene");
    FieldDefinition owner = meteor.Fields.Single(field => field.Name == "mOwner");
    FieldDefinition rumble = meteor.Fields.Single(field => field.Name == "mRumble");
    int clearScene = FindNullFieldStore(body, scene);
    int clearOwner = FindNullFieldStore(body, owner);
    int clearRumble = FindNullFieldStore(body, rumble);
    int restoreLight = Array.FindIndex(
        body,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "Magicka.Levels.GameScene"
            && called.Name == "set_LightTargetIntensity");
    int stopRumble = Array.FindIndex(
        body,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "Microsoft.Xna.Framework.Audio.Cue"
            && called.Name == "Stop");
    if (clearScene < 0
        || clearOwner < 0
        || clearRumble < 0
        || restoreLight <= clearScene
        || stopRumble <= clearRumble)
    {
        throw new InvalidDataException(
            "MeteorShower.OnRemove does not clear singleton references"
            + " before light and rumble cleanup.");
    }
}

static void ValidateBlizzardRemoveReferences(AssemblyDefinition magicka)
{
    TypeDefinition blizzard = AllTypes(magicka.MainModule).Single(type =>
        type.FullName
            == "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Blizzard");
    MethodDefinition onRemove = blizzard.Methods.Single(method =>
        method.Name == "OnRemove" && method.Parameters.Count == 0);
    Instruction[] body = onRemove.Body.Instructions.ToArray();
    FieldDefinition scene = blizzard.Fields.Single(field => field.Name == "mScene");
    FieldDefinition caster = blizzard.Fields.Single(field => field.Name == "mCaster");
    FieldDefinition ambience = blizzard.Fields.Single(field =>
        field.Name == "mAmbience");
    int clearScene = FindNullFieldStore(body, scene);
    int clearCaster = FindNullFieldStore(body, caster);
    int clearAmbience = FindNullFieldStore(body, ambience);
    int stopAmbience = Array.FindIndex(
        body,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "Microsoft.Xna.Framework.Audio.Cue"
            && called.Name == "Stop");
    if (clearScene < 0
        || clearCaster < 0
        || clearAmbience < 0
        || stopAmbience <= clearAmbience)
    {
        throw new InvalidDataException(
            "Blizzard.OnRemove does not clear singleton references before"
            + " ambience cleanup.");
    }
}

static void ValidateControllerAvatarDetach(AssemblyDefinition magicka)
{
    TypeDefinition controller = AllTypes(magicka.MainModule).Single(type =>
        type.FullName == "Magicka.GameLogic.Controls.Controller");
    FieldDefinition controllerAvatar = controller.Fields.Single(field =>
        field.Name == "mAvatar"
        && field.FieldType.FullName == "Magicka.GameLogic.Entities.Avatar");
    MethodDefinition detach = controller.Methods.Single(method =>
        method.Name == "CommunityPatchDetachAvatar"
        && method.Parameters.Count == 1
        && method.Parameters[0].ParameterType.FullName
            == controllerAvatar.FieldType.FullName);
    Instruction[] detachBody = detach.Body.Instructions.ToArray();
    if (FindNullFieldStore(detachBody, controllerAvatar) < 0
        || !detachBody.Any(instruction =>
            instruction.OpCode == OpCodes.Bne_Un
            || instruction.OpCode == OpCodes.Bne_Un_S))
    {
        throw new InvalidDataException(
            "Controller Avatar detach is not conditional on the expected"
            + " Avatar.");
    }

    MethodDefinition setter = RequireMethod(
        magicka,
        "Magicka.GameLogic.Player",
        "set_Avatar",
        parameterCount: 1);
    Instruction[] setterBody = setter.Body.Instructions.ToArray();
    int getOldAvatar = Array.FindIndex(
        setterBody,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.WeakReference"
            && called.Name == "get_Target");
    int detachCall = Array.FindIndex(
        setterBody,
        instruction => IsCallTo(instruction, detach));
    int setNewAvatar = Array.FindLastIndex(
        setterBody,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.WeakReference"
            && called.Name == "set_Target");
    bool nullOnly = setterBody
        .Skip(getOldAvatar + 1)
        .Take(Math.Max(0, detachCall - getOldAvatar - 1))
        .Any(instruction => instruction.OpCode == OpCodes.Brtrue
            || instruction.OpCode == OpCodes.Brtrue_S);
    if (getOldAvatar < 0
        || detachCall <= getOldAvatar
        || setNewAvatar <= detachCall
        || !nullOnly)
    {
        throw new InvalidDataException(
            "Player.set_Avatar does not conditionally detach the previous"
            + " Avatar before clearing its WeakReference.");
    }
}

static void ValidateTelemetryPatchVersion(AssemblyDefinition magicka)
{
    TypeDefinition patchTelemetry = AllTypes(magicka.MainModule).Single(type =>
        type.FullName == "Magicka.CommunityPatch.PatchTelemetry");
    MethodDefinition sendAsync = patchTelemetry.Methods.Single(method =>
        method.Name == "SendAsync"
        && method.IsStatic
        && method.Parameters.Count == 2
        && method.Parameters[1].ParameterType.FullName
            == "System.Collections.Generic.Dictionary`2<System.String,System.String>"
        && method.HasBody);
    MethodDefinition getPatchVersion = patchTelemetry.Methods.Single(method =>
        method.Name == "GetPatchVersion"
        && method.IsStatic
        && method.Parameters.Count == 0
        && method.ReturnType.FullName == "System.String");
    Instruction[] body = sendAsync.Body.Instructions.ToArray();
    int keyIndex = Array.FindIndex(
        body,
        instruction => instruction.OpCode == OpCodes.Ldstr
            && string.Equals(
                instruction.Operand as string,
                "patch_version",
                StringComparison.Ordinal));
    int queueStateIndex = Array.FindIndex(
        body,
        instruction => instruction.OpCode == OpCodes.Newobj
            && instruction.Operand is MethodReference constructor
            && constructor.Name == ".ctor"
            && constructor.DeclaringType.FullName
                == "Magicka.CommunityPatch.PatchTelemetry/TelemetrySendState");
    Instruction? patchStart = keyIndex > 0 ? body[keyIndex - 1] : null;
    bool patchBlockIsReachable = patchStart is not null
        && body.Take(keyIndex).Any(instruction =>
            (instruction.OpCode == OpCodes.Brfalse
             || instruction.OpCode == OpCodes.Brfalse_S)
            && ReferenceEquals(instruction.Operand, patchStart));
    if (keyIndex <= 0
        || keyIndex + 2 >= body.Length
        || body[keyIndex - 1].OpCode != OpCodes.Ldarg_1
        || !IsCallTo(body[keyIndex + 1], getPatchVersion)
        || body[keyIndex + 2].OpCode != OpCodes.Callvirt
        || body[keyIndex + 2].Operand is not MethodReference setItem
        || setItem.Name != "set_Item"
        || setItem.DeclaringType.FullName
            != sendAsync.Parameters[1].ParameterType.FullName
        || setItem.Parameters.Count != 2
        || setItem.Parameters[0].ParameterType is not GenericParameter key
        || key.Position != 0
        || setItem.Parameters[1].ParameterType is not GenericParameter value
        || value.Position != 1
        || queueStateIndex <= keyIndex + 2
        || !patchBlockIsReachable)
    {
        throw new InvalidDataException(
            "PatchTelemetry.SendAsync does not reachably add patch_version"
            + " before queueing the event.");
    }

    MethodDefinition addCommonProperties = patchTelemetry.Methods.Single(
        method => method.Name == "AddCommonProperties"
            && method.IsStatic
            && method.Parameters.Count == 1);
    MethodDefinition sendBlocking = patchTelemetry.Methods.Single(method =>
        method.Name == "SendBlocking"
            && method.IsStatic
            && method.Parameters.Count == 3);
    Instruction[] blockingBody = sendBlocking.Body.Instructions.ToArray();
    int buildJsonIndex = Array.FindIndex(
        blockingBody,
        instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == patchTelemetry.FullName
            && called.Name == "BuildPostHogJson");
    if (buildJsonIndex <= 0
        || !IsCallTo(blockingBody[buildJsonIndex - 1], addCommonProperties))
    {
        throw new InvalidDataException(
            "PatchTelemetry.SendBlocking does not add common properties"
            + " at the serialization boundary.");
    }

    MethodDefinition sendStartup = patchTelemetry.Methods.Single(method =>
        method.Name == "SendStartup"
        && method.IsStatic
        && method.Parameters.Count == 0
        && method.HasBody);
    if (!sendStartup.Body.ExceptionHandlers.Any(handler =>
            handler.HandlerType == ExceptionHandlerType.Catch
            && handler.CatchType?.FullName == "System.Object"
            && handler.TryStart == sendStartup.Body.Instructions[0]
            && handler.HandlerEnd == sendStartup.Body.Instructions[^1]))
    {
        throw new InvalidDataException(
            "PatchTelemetry.SendStartup is not isolated from telemetry"
            + " failures.");
    }
}

static void ValidateExceptionHandlerNestingOrder(
    AssemblyDefinition assembly)
{
    foreach (MethodDefinition method in AllTypes(assembly.MainModule)
                 .SelectMany(type => type.Methods)
                 .Where(method => method.HasBody
                     && method.Body.ExceptionHandlers.Count > 1))
    {
        List<Instruction> instructions = method.Body.Instructions.ToList();
        for (int innerIndex = 0;
             innerIndex < method.Body.ExceptionHandlers.Count;
             innerIndex++)
        {
            ExceptionHandler inner = method.Body.ExceptionHandlers[innerIndex];
            int innerStart = instructions.IndexOf(inner.TryStart);
            int innerEnd = instructions.IndexOf(inner.TryEnd);
            for (int outerIndex = 0;
                 outerIndex < method.Body.ExceptionHandlers.Count;
                 outerIndex++)
            {
                if (outerIndex == innerIndex)
                {
                    continue;
                }
                ExceptionHandler outer = method.Body.ExceptionHandlers[outerIndex];
                int outerStart = instructions.IndexOf(outer.TryStart);
                int outerEnd = instructions.IndexOf(outer.TryEnd);
                bool strictlyNested = outerStart <= innerStart
                    && innerEnd <= outerEnd
                    && (outerStart < innerStart || innerEnd < outerEnd);
                if (strictlyNested && innerIndex > outerIndex)
                {
                    throw new InvalidDataException(
                        method.FullName
                        + " stores a nested exception clause after its enclosing clause;"
                        + " CLR 2 can reject the method during JIT compilation.");
                }
            }
        }
    }
}

static void ValidatePlayerGameDeinitialize(AssemblyDefinition magicka)
{
    IReadOnlyDictionary<string, TypeDefinition> types =
        AllTypes(magicka.MainModule).ToDictionary(
            type => type.FullName,
            StringComparer.Ordinal);
    TypeDefinition player = types["Magicka.GameLogic.Player"];
    TypeDefinition textBox = types["Magicka.Graphics.TextBox"];
    TypeDefinition notifierButton = types["Magicka.Graphics.NotifierButton"];
    FieldDefinition obtainedTextBox = player.Fields.Single(field =>
        field.Name == "mObtainedTextBox"
        && field.FieldType.FullName == textBox.FullName);
    FieldDefinition textBoxOwner = textBox.Fields.Single(field =>
        field.Name == "mOwner"
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.Entity");
    FieldDefinition textBoxScene = textBox.Fields.Single(field =>
        field.Name == "mScene"
        && field.FieldType.FullName == "PolygonHead.Scene");
    MethodDefinition releaseLevelReferences = RequireMethod(
        magicka,
        textBox.FullName,
        "ReleaseLevelReferences",
        parameterCount: 0);
    Instruction[] releaseBody =
        releaseLevelReferences.Body.Instructions.ToArray();
    if (FindNullFieldStore(releaseBody, textBoxOwner) < 0
        || FindNullFieldStore(releaseBody, textBoxScene) < 0)
    {
        throw new InvalidDataException(
            "TextBox.ReleaseLevelReferences must clear mOwner and mScene.");
    }

    FieldDefinition notifier = player.Fields.Single(field =>
        field.Name == "mNotifierButton"
        && field.FieldType.FullName == notifierButton.FullName);
    FieldDefinition notifierOwner = notifierButton.Fields.Single(field =>
        field.Name == "mOwner"
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.Entity");
    FieldDefinition notifierDialog = notifierButton.Fields.Single(field =>
        field.Name == "mDialogAttach"
        && field.FieldType.FullName == textBox.FullName);
    FieldDefinition notifierAlpha = notifierButton.Fields.Single(field =>
        field.Name == "mAlpha"
        && field.FieldType.FullName == "System.Single");
    FieldDefinition notifierTargetAlpha = notifierButton.Fields.Single(field =>
        field.Name == "mTargetAlpha"
        && field.FieldType.FullName == "System.Single");
    MethodDefinition releaseNotifierReferences = RequireMethod(
        magicka,
        notifierButton.FullName,
        "ReleaseLevelReferences",
        parameterCount: 0);
    Instruction[] notifierReleaseBody =
        releaseNotifierReferences.Body.Instructions.ToArray();
    if (FindNullFieldStore(notifierReleaseBody, notifierOwner) < 0
        || FindNullFieldStore(notifierReleaseBody, notifierDialog) < 0
        || FindFloatZeroFieldStore(notifierReleaseBody, notifierAlpha) < 0
        || FindFloatZeroFieldStore(
            notifierReleaseBody,
            notifierTargetAlpha) < 0)
    {
        throw new InvalidDataException(
            "NotifierButton.ReleaseLevelReferences must hide the notifier"
            + " and clear mOwner and mDialogAttach.");
    }

    MethodDefinition deinitializeGame = RequireMethod(
        magicka,
        player.FullName,
        "DeinitializeGame",
        parameterCount: 0);
    Instruction[] body = deinitializeGame.Body.Instructions.ToArray();
    if (body.Length != 13
        || body[0].OpCode != OpCodes.Ldarg_0
        || body[1].OpCode != OpCodes.Ldfld
        || !IsReferenceTo(body[1].Operand, obtainedTextBox)
        || (body[2].OpCode != OpCodes.Brfalse
            && body[2].OpCode != OpCodes.Brfalse_S)
        || body[2].Operand != body[6]
        || body[3].OpCode != OpCodes.Ldarg_0
        || body[4].OpCode != OpCodes.Ldfld
        || !IsReferenceTo(body[4].Operand, obtainedTextBox)
        || !IsCallTo(body[5], releaseLevelReferences)
        || body[6].OpCode != OpCodes.Ldarg_0
        || body[7].OpCode != OpCodes.Ldfld
        || !IsReferenceTo(body[7].Operand, notifier)
        || (body[8].OpCode != OpCodes.Brfalse
            && body[8].OpCode != OpCodes.Brfalse_S)
        || body[8].Operand != body[12]
        || body[9].OpCode != OpCodes.Ldarg_0
        || body[10].OpCode != OpCodes.Ldfld
        || !IsReferenceTo(body[10].Operand, notifier)
        || !IsCallTo(body[11], releaseNotifierReferences)
        || body[12].OpCode != OpCodes.Ret)
    {
        throw new InvalidDataException(
            "Player.DeinitializeGame does not independently guard and release"
            + " the text-box and notifier level references.");
    }

    MethodDefinition onExit = RequireMethod(
        magicka,
        "Magicka.GameLogic.GameStates.PlayState",
        "OnExit",
        parameterCount: 0);
    if (onExit.Body.Instructions.Count(instruction =>
            IsCallTo(instruction, deinitializeGame)) != 1)
    {
        throw new InvalidDataException(
            "PlayState.OnExit must call Player.DeinitializeGame exactly once.");
    }
}

static int FindFloatZeroFieldStore(
    IReadOnlyList<Instruction> instructions,
    FieldDefinition field)
{
    for (int index = 1; index < instructions.Count; index++)
    {
        if (instructions[index - 1].OpCode == OpCodes.Ldc_R4
            && instructions[index - 1].Operand is float value
            && value == 0f
            && instructions[index].OpCode == OpCodes.Stfld
            && IsReferenceTo(instructions[index].Operand, field))
        {
            return index;
        }
    }

    return -1;
}

static int FindNullFieldStore(
    IReadOnlyList<Instruction> instructions,
    FieldDefinition field)
{
    for (int index = 1; index < instructions.Count; index++)
    {
        if (instructions[index - 1].OpCode == OpCodes.Ldnull
            && instructions[index].OpCode == OpCodes.Stfld
            && IsReferenceTo(instructions[index].Operand, field))
        {
            return index;
        }
    }

    return -1;
}

static void ValidateEntityCollisionCallbackCleanup(
    AssemblyDefinition magicka)
{
    IReadOnlyDictionary<string, TypeDefinition> types =
        AllTypes(magicka.MainModule).ToDictionary(
            type => type.FullName,
            StringComparer.Ordinal);
    const string helperTypeName =
        "Magicka.CommunityPatch.CollisionCallbackCleanup";
    TypeDefinition helper = types[helperTypeName];
    FieldDefinition callbackField = helper.Fields.Single(field =>
        field.Name == "sCallbackField"
        && field.IsStatic
        && field.IsInitOnly
        && field.FieldType.FullName == "System.Reflection.FieldInfo");
    MethodDefinition initialize = helper.Methods.Single(method =>
        method.IsConstructor
        && method.IsStatic
        && method.HasBody);
    Instruction[] initializeBody = initialize.Body.Instructions.ToArray();
    bool resolvesExactField = initializeBody.Any(instruction =>
            instruction.OpCode == OpCodes.Ldstr
            && string.Equals(
                instruction.Operand as string,
                "callbackFn",
                StringComparison.Ordinal))
        && initializeBody.Any(instruction =>
            instruction.OpCode == OpCodes.Ldc_I4_S
            && instruction.Operand is sbyte flags
            && flags == 36)
        && initializeBody.Any(instruction =>
            instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Type"
            && called.Name == "GetField"
            && called.Parameters.Count == 2)
        && initializeBody.Any(instruction =>
            instruction.OpCode == OpCodes.Stsfld
            && IsReferenceTo(instruction.Operand, callbackField));
    if (!resolvesExactField
        || initialize.Body.ExceptionHandlers.Count != 1
        || initialize.Body.ExceptionHandlers[0].HandlerType
            != ExceptionHandlerType.Catch
        || initialize.Body.ExceptionHandlers[0].CatchType?.FullName
            != "System.Exception")
    {
        throw new InvalidDataException(
            "CollisionCallbackCleanup does not safely resolve the exact"
            + " private callbackFn field.");
    }

    MethodDefinition clear = helper.Methods.Single(method =>
        method.Name == "Clear"
        && method.IsStatic
        && method.Parameters.Count == 1
        && method.Parameters[0].ParameterType.FullName
            == "JigLibX.Collision.CollisionSkin");
    Instruction[] clearBody = clear.Body.Instructions.ToArray();
    if (clearBody.Count(instruction =>
            instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Reflection.FieldInfo"
            && called.Name == "SetValue"
            && called.Parameters.Count == 2) != 1
        || clear.Body.ExceptionHandlers.Count != 1
        || clear.Body.ExceptionHandlers[0].HandlerType
            != ExceptionHandlerType.Catch
        || clear.Body.ExceptionHandlers[0].CatchType?.FullName
            != "System.Exception")
    {
        throw new InvalidDataException(
            "CollisionCallbackCleanup.Clear must clear the backing delegate"
            + " once and contain reflection failures.");
    }

    TypeDefinition entity = types["Magicka.GameLogic.Entities.Entity"];
    MethodDefinition dispose = entity.Methods.Single(method =>
        method.Name == "Dispose"
        && !method.IsStatic
        && method.Parameters.Count == 0);
    Instruction[] disposeBody = dispose.Body.Instructions.ToArray();
    int cleanupIndex = Array.FindIndex(
        disposeBody,
        instruction => IsCallTo(instruction, clear));
    int clearCollisionsIndex = Array.FindIndex(
        disposeBody,
        instruction => instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "JigLibX.Collision.CollisionSkin"
            && called.Name == "get_Collisions");
    int detachSystemIndex = Array.FindIndex(
        disposeBody,
        instruction => instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "JigLibX.Collision.CollisionSkin"
            && called.Name == "set_CollisionSystem");
    if (cleanupIndex < 0
        || cleanupIndex >= clearCollisionsIndex
        || cleanupIndex >= detachSystemIndex
        || disposeBody.Count(instruction => IsCallTo(instruction, clear)) != 1)
    {
        throw new InvalidDataException(
            "Entity.Dispose must clear collision callbacks exactly once before"
            + " detaching collision state.");
    }
}

static void ValidateCharacterTemplateStaticCaches(
    AssemblyDefinition magicka)
{
    IReadOnlyDictionary<string, TypeDefinition> types =
        AllTypes(magicka.MainModule).ToDictionary(
            type => type.FullName,
            StringComparer.Ordinal);
    (string TypeName, string FieldName)[] cacheOwners =
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
    List<MethodDefinition> disposeMethods = new List<MethodDefinition>();
    foreach ((string typeName, string fieldName) in cacheOwners)
    {
        TypeDefinition owner = types[typeName];
        FieldDefinition field = owner.Fields.Single(candidate =>
            candidate.Name == fieldName
            && candidate.IsStatic
            && (candidate.FieldType.FullName
                    == "Magicka.GameLogic.Entities.CharacterTemplate"
                || candidate.FieldType.FullName
                    == "Magicka.GameLogic.Entities.CharacterTemplate[]"));
        MethodDefinition disposeCache = owner.Methods.Single(method =>
            method.Name == "DisposeCache"
            && method.Parameters.Count == 0
            && method.IsStatic
            && method.HasBody);
        Instruction[] instructions = disposeCache.Body.Instructions.ToArray();
        int resetIndex = Array.FindIndex(
            instructions,
            instruction => instruction.OpCode == OpCodes.Stsfld
                && IsReferenceTo(instruction.Operand, field));
        if (resetIndex <= 0
            || instructions[resetIndex - 1].OpCode != OpCodes.Ldnull
            || instructions.Count(instruction =>
                instruction.OpCode == OpCodes.Stsfld
                && IsReferenceTo(instruction.Operand, field)) != 1)
        {
            throw new InvalidDataException(
                disposeCache.FullName + " does not reset "
                + field.FullName + " exactly once.");
        }

        disposeMethods.Add(disposeCache);
    }

    MethodDefinition disposeMagicks = RequireMethod(
        magicka,
        "Magicka.GameLogic.Spells.Magick",
        "DisposeMagicks",
        parameterCount: 0);
    foreach (MethodDefinition disposeCache in disposeMethods)
    {
        int callCount = disposeMagicks.Body.Instructions.Count(
            instruction => IsCallTo(instruction, disposeCache));
        if (callCount != 1)
        {
            throw new InvalidDataException(
                disposeMagicks.FullName + " calls " + disposeCache.FullName
                + " " + callCount + " time(s); expected exactly once.");
        }
    }

    TypeDefinition characterTemplate =
        types["Magicka.GameLogic.Entities.CharacterTemplate"];
    FieldDefinition avatarTemplates = characterTemplate.Fields.Single(field =>
        field.Name == "sCachedAvatarTemplates"
        && field.IsStatic
        && field.FieldType.FullName
            == "System.Collections.Generic.Dictionary`2<System.String,"
               + "Magicka.GameLogic.Entities.CharacterTemplate>");
    MethodDefinition clearCache = RequireMethod(
        magicka,
        characterTemplate.FullName,
        "ClearCache",
        parameterCount: 0);
    Instruction[] clearInstructions = clearCache.Body.Instructions.ToArray();
    int avatarLoadIndex = Array.FindIndex(
        clearInstructions,
        instruction => instruction.OpCode == OpCodes.Ldsfld
            && IsReferenceTo(instruction.Operand, avatarTemplates));
    if (avatarLoadIndex < 0
        || avatarLoadIndex + 1 >= clearInstructions.Length
        || clearInstructions[avatarLoadIndex + 1].OpCode != OpCodes.Callvirt
        || clearInstructions[avatarLoadIndex + 1].Operand
            is not MethodReference clearCall
        || clearCall.Name != "Clear"
        || clearCall.DeclaringType.FullName != avatarTemplates.FieldType.FullName)
    {
        throw new InvalidDataException(
            "CharacterTemplate.ClearCache does not clear"
            + " sCachedAvatarTemplates.");
    }

    MethodDefinition playStateDispose = RequireMethod(
        magicka,
        "Magicka.GameLogic.GameStates.PlayState",
        "Dispose",
        parameterCount: 0);
    int magickTeardownIndex = Array.FindIndex(
        playStateDispose.Body.Instructions.ToArray(),
        instruction => IsCallTo(instruction, disposeMagicks));
    int templateTeardownIndex = Array.FindIndex(
        playStateDispose.Body.Instructions.ToArray(),
        instruction => IsCallTo(instruction, clearCache));
    if (magickTeardownIndex < 0
        || templateTeardownIndex <= magickTeardownIndex)
    {
        throw new InvalidDataException(
            "PlayState.Dispose must release ability template caches before"
            + " CharacterTemplate.ClearCache.");
    }
}

static void ValidateWarlordAbilityDiagnostic(AssemblyDefinition magicka)
{
    TypeDefinition characterTemplate = AllTypes(magicka.MainModule).Single(
        type => type.FullName
            == "Magicka.GameLogic.Entities.CharacterTemplate");
    FieldDefinition disposedField = characterTemplate.Fields.Single(field =>
        field.Name == "mDisposed"
        && !field.IsStatic
        && field.FieldType.FullName == "System.Boolean");
    MethodDefinition? isDisposed = characterTemplate.Methods.SingleOrDefault(method =>
        method.Name == "CommunityPatchIsDisposed"
        && !method.IsStatic
        && method.Parameters.Count == 0
        && method.ReturnType.FullName == "System.Boolean"
        && method.HasBody);
    if (isDisposed is null)
    {
        throw new InvalidDataException(
            "CharacterTemplate.CommunityPatchIsDisposed is missing.");
    }
    Instruction[] disposedBody = isDisposed.Body.Instructions.ToArray();
    if (disposedBody.Length != 3
        || disposedBody[0].OpCode != OpCodes.Ldarg_0
        || disposedBody[1].OpCode != OpCodes.Ldfld
        || !IsReferenceTo(disposedBody[1].Operand, disposedField)
        || disposedBody[2].OpCode != OpCodes.Ret)
    {
        throw new InvalidDataException(
            "CharacterTemplate.CommunityPatchIsDisposed does not expose"
            + " only the existing disposal flag.");
    }

    TypeDefinition? helper = AllTypes(magicka.MainModule).SingleOrDefault(type =>
        type.FullName
            == "Magicka.CommunityPatch.WarlordAbilityDiagnostic");
    if (helper is null)
    {
        throw new InvalidDataException(
            "Warlord primary-ability diagnostic helper is missing.");
    }
    MethodDefinition inspect = helper.Methods.Single(method =>
        method.Name == "Inspect"
        && method.IsStatic
        && method.Parameters.Count == 2
        && method.Parameters[0].ParameterType.FullName
            == characterTemplate.FullName
        && method.Parameters[1].ParameterType.FullName
            == "Magicka.GameLogic.Entities.Abilities.Ability[]"
        && method.ReturnType.FullName == "System.Void"
        && method.HasBody);
    string[] requiredStrings =
    [
        "magicka_patch_warlord_ability_diagnostic",
        "warlord_primary_ability_not_melee",
        "NonPlayerCharacter.Abilities",
        "template_null=",
        ";template_disposed=",
        ";template_id=",
        ";abilities_null=",
        ";ability_count=",
        ";primary_null=",
        ";shares_template_abilities=",
    ];
    HashSet<string> helperStrings = inspect.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
        .Select(instruction => instruction.Operand as string ?? string.Empty)
        .ToHashSet(StringComparer.Ordinal);
    string[] missingStrings = requiredStrings
        .Where(required => !helperStrings.Contains(required))
        .ToArray();
    if (missingStrings.Length != 0)
    {
        throw new InvalidDataException(
            "Warlord diagnostic is missing fields: "
            + string.Join(", ", missingStrings));
    }

    TypeDefinition patchTelemetry = AllTypes(magicka.MainModule).Single(type =>
        type.FullName == "Magicka.CommunityPatch.PatchTelemetry");
    MethodDefinition sendRuntimeGuard = patchTelemetry.Methods.Single(method =>
        method.Name == "SendRuntimeGuard"
        && method.IsStatic
        && method.Parameters.Count == 6);
    int senderCalls = inspect.Body.Instructions.Count(instruction =>
        IsCallTo(instruction, sendRuntimeGuard));
    if (senderCalls != 1)
    {
        throw new InvalidDataException(
            "Warlord diagnostic calls the bounded telemetry sender "
            + senderCalls + " time(s); expected exactly once.");
    }

    if (inspect.Body.ExceptionHandlers.Count != 1
        || inspect.Body.ExceptionHandlers[0].HandlerType
            != ExceptionHandlerType.Catch
        || inspect.Body.ExceptionHandlers[0].CatchType?.FullName
            != "System.Object")
    {
        throw new InvalidDataException(
            "Warlord diagnostic is not isolated by the required catch-all.");
    }

    MethodDefinition applyTemplate = RequireMethod(
        magicka,
        "Magicka.GameLogic.Entities.Bosses.WarlordCharacter",
        "ApplyTemplate",
        parameterCount: 2);
    Instruction[] applyBody = applyTemplate.Body.Instructions.ToArray();
    int baseApplyIndex = Array.FindIndex(
        applyBody,
        instruction => instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference called
            && called.Name == "ApplyTemplate"
            && called.DeclaringType.FullName
                == "Magicka.GameLogic.Entities.NonPlayerCharacter");
    int inspectIndex = Array.FindIndex(
        applyBody,
        instruction => IsCallTo(instruction, inspect));
    int meleeCastIndex = Array.FindIndex(
        applyBody,
        instruction => instruction.OpCode == OpCodes.Isinst
            && instruction.Operand is TypeReference type
            && type.FullName
                == "Magicka.GameLogic.Entities.Abilities.Melee");
    int bashConstructorIndex = Array.FindIndex(
        applyBody,
        instruction => instruction.OpCode == OpCodes.Newobj
            && instruction.Operand is MethodReference called
            && called.Name == ".ctor"
            && called.DeclaringType.FullName
                == "Magicka.GameLogic.Entities.Bosses.WarlordCharacter/Bash"
            && called.Parameters.Count == 1
            && called.Parameters[0].ParameterType.FullName
                == "Magicka.GameLogic.Entities.Abilities.Melee");
    if (baseApplyIndex < 0
        || inspectIndex <= baseApplyIndex
        || meleeCastIndex <= inspectIndex
        || bashConstructorIndex <= meleeCastIndex
        || applyBody.Count(instruction => IsCallTo(instruction, inspect)) != 1)
    {
        throw new InvalidDataException(
            "Warlord diagnostic does not run exactly once between base"
            + " template application and the preserved Melee/Bash path.");
    }
}

static void ValidateRailgunParentCycleRepair(AssemblyDefinition magicka)
{
    TypeDefinition railgun = AllTypes(magicka.MainModule).Single(type =>
        type.FullName == "Magicka.GameLogic.Spells.Railgun");
    FieldDefinition parents = railgun.Fields.Single(field =>
        field.Name == "mParents"
        && field.FieldType.FullName
            == "System.Collections.Generic.List`1<"
               + railgun.FullName + ">");
    FieldDefinition lockTraversalActive = railgun.Fields.Single(field =>
        field.Name == "mCommunityPatchLockAllActive"
        && field.FieldType.FullName == "System.Boolean");
    MethodDefinition report = railgun.Methods.Single(method =>
        method.Name == "CommunityPatchReportParentCycleRecovery"
        && method.IsStatic
        && method.Parameters.Count == 4
        && method.Parameters[0].ParameterType.FullName == "System.String"
        && method.Parameters.Skip(1).All(parameter =>
            parameter.ParameterType.FullName == "System.Int32")
        && method.ReturnType.FullName == "System.Void"
        && method.HasBody);
    MethodDefinition cycleCheck = railgun.Methods.Single(method =>
        method.Name == "CommunityPatchWouldCreateParentCycle"
        && !method.IsStatic
        && method.Parameters.Count == 1
        && method.Parameters[0].ParameterType.FullName == railgun.FullName
        && method.ReturnType.FullName == "System.Boolean"
        && method.HasBody);

    string[] reportStrings = report.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
        .Select(instruction => instruction.Operand as string ?? string.Empty)
        .ToArray();
    string[] requiredReportStrings =
    [
        "magicka_patch_runtime_recovery",
        "Railgun.mParents",
        "Magicka.GameLogic.Spells.Railgun",
        "visited_count=",
        ";pending_count=",
        ";candidate_parent_count=",
    ];
    if (requiredReportStrings.Any(required =>
            !reportStrings.Contains(required, StringComparer.Ordinal)))
    {
        throw new InvalidDataException(
            "Railgun recovery telemetry is missing required bounded fields.");
    }

    TypeDefinition patchTelemetry = AllTypes(magicka.MainModule).Single(type =>
        type.FullName == "Magicka.CommunityPatch.PatchTelemetry");
    MethodDefinition sendRuntimeGuard = patchTelemetry.Methods.Single(method =>
        method.Name == "SendRuntimeGuard"
        && method.IsStatic
        && method.Parameters.Count == 6);
    if (report.Body.Instructions.Count(instruction =>
            IsCallTo(instruction, sendRuntimeGuard)) != 1
        || !HasCatchAll(report))
    {
        throw new InvalidDataException(
            "Railgun recovery telemetry is not bounded by the shared sender"
            + " and an exception boundary.");
    }

    HashSet<string> cycleReasons = cycleCheck.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ldstr)
        .Select(instruction => instruction.Operand as string ?? string.Empty)
        .ToHashSet(StringComparer.Ordinal);
    string[] requiredReasons =
    [
        "railgun_parent_cycle_prevented",
        "railgun_parent_cycle_check_limit_reached",
        "railgun_parent_cycle_check_failed",
    ];
    if (requiredReasons.Any(reason => !cycleReasons.Contains(reason))
        || !HasCatchAll(cycleCheck)
        || cycleCheck.Body.Instructions.Any(instruction =>
            IsCallTo(instruction, cycleCheck))
        || cycleCheck.Body.Instructions.Count(instruction =>
            instruction.OpCode == OpCodes.Newarr
            && instruction.Operand is TypeReference type
            && type.FullName == railgun.FullName) != 2)
    {
        throw new InvalidDataException(
            "Railgun parent traversal is missing its iterative bounded"
            + " fail-safe behavior.");
    }

    MethodDefinition lockAll = railgun.Methods.Single(method =>
        method.Name == "LockAll"
        && !method.IsStatic
        && method.Parameters.Count == 0
        && method.HasBody);
    Instruction[] lockBody = lockAll.Body.Instructions.ToArray();
    if (lockBody.Length < 14
        || lockBody[0].OpCode != OpCodes.Ldarg_0
        || lockBody[1].OpCode != OpCodes.Ldfld
        || !IsReferenceTo(lockBody[1].Operand, lockTraversalActive)
        || lockBody[2].OpCode != OpCodes.Brfalse
        || lockBody[2].Operand != lockBody[4]
        || lockBody[3].OpCode != OpCodes.Ret
        || lockBody[4].OpCode != OpCodes.Ldarg_0
        || lockBody[5].OpCode != OpCodes.Ldc_I4_1
        || lockBody[6].OpCode != OpCodes.Stfld
        || !IsReferenceTo(lockBody[6].Operand, lockTraversalActive)
        || lockBody[^4].OpCode != OpCodes.Ldarg_0
        || lockBody[^3].OpCode != OpCodes.Ldc_I4_0
        || lockBody[^2].OpCode != OpCodes.Stfld
        || !IsReferenceTo(lockBody[^2].Operand, lockTraversalActive)
        || lockBody[^1].OpCode != OpCodes.Ret
        || lockBody.Count(instruction => IsCallTo(instruction, lockAll)) != 1)
    {
        throw new InvalidDataException(
            "Railgun.LockAll is missing its traversal-scoped recursion guard.");
    }

    MethodDefinition update = railgun.Methods.Single(method =>
        method.Name == "Update"
        && method.Parameters.Count == 2
        && method.HasBody);
    Instruction[] updateBody = update.Body.Instructions.ToArray();
    int cycleCheckIndex = Array.FindIndex(
        updateBody,
        instruction => IsCallTo(instruction, cycleCheck));
    if (cycleCheckIndex < 3
        || updateBody.Count(instruction => IsCallTo(instruction, cycleCheck)) != 1
        || updateBody[cycleCheckIndex - 2].OpCode != OpCodes.Ldarg_0
        || updateBody[cycleCheckIndex - 1].Operand is not VariableDefinition candidate
        || candidate.VariableType.FullName != railgun.FullName
        || updateBody[cycleCheckIndex + 1].OpCode != OpCodes.Brtrue
        || updateBody[cycleCheckIndex - 3].Operand
            != updateBody[cycleCheckIndex + 1].Operand
        || updateBody[cycleCheckIndex + 2].OpCode != OpCodes.Ldnull)
    {
        throw new InvalidDataException(
            "Railgun.Update does not reject an unsafe candidate after"
            + " geometry validation and before mutation.");
    }

    int parentAttachmentIndex = Array.FindIndex(
        updateBody,
        cycleCheckIndex + 1,
        instruction => instruction.Operand is MethodReference called
            && called.Name == "Add"
            && called.DeclaringType.FullName == parents.FieldType.FullName);
    if (parentAttachmentIndex <= cycleCheckIndex)
    {
        throw new InvalidDataException(
            "Railgun parent attachment is not protected by the cycle check.");
    }
}

static void ValidateJudgementSprayConditionCacheRepair(
    AssemblyDefinition magicka)
{
    TypeDefinition judgementSpray = AllTypes(magicka.MainModule).Single(type =>
        type.FullName
            == "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.JudgementSpray");
    TypeDefinition projectileSpell = AllTypes(magicka.MainModule).Single(type =>
        type.FullName
            == "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell");
    TypeDefinition conditionCollection = AllTypes(magicka.MainModule).Single(type =>
        type.FullName
            == "Magicka.GameLogic.Entities.Items.ConditionCollection");
    FieldDefinition cache = projectileSpell.Fields.Single(field =>
        field.Name == "sCachedConditions"
        && field.IsStatic
        && field.FieldType.FullName
            == "System.Collections.Generic.Queue`1<"
               + conditionCollection.FullName + ">");
    MethodDefinition takeConditions = judgementSpray.Methods.Single(method =>
        method.Name == "CommunityPatchTakeConditionCollectionLocked"
        && method.IsStatic
        && method.Parameters.Count == 1
        && method.Parameters[0].ParameterType.FullName == cache.FieldType.FullName
        && method.ReturnType.FullName == conditionCollection.FullName
        && method.HasBody);
    Instruction[] helper = takeConditions.Body.Instructions.ToArray();
    string[] requiredStrings =
    [
        "magicka_patch_runtime_recovery",
        "judgement_spray_condition_cache_empty_recovered",
        "ProjectileSpell.sCachedConditions",
        judgementSpray.FullName,
        "Allocated a replacement ConditionCollection and continued"
        + " projectile spawn.",
        string.Empty,
    ];
    MethodDefinition sendRuntimeGuard = AllTypes(magicka.MainModule)
        .Single(type => type.FullName
            == "Magicka.CommunityPatch.PatchTelemetry")
        .Methods.Single(method =>
            method.Name == "SendRuntimeGuard"
            && method.IsStatic
            && method.Parameters.Count == 6);
    if (helper.Length != 15
        || helper[0].OpCode != OpCodes.Ldarg_0
        || helper[1].OpCode != OpCodes.Callvirt
        || helper[1].Operand is not MethodReference getCount
        || getCount.Name != "get_Count"
        || getCount.DeclaringType.FullName != cache.FieldType.FullName
        || helper[2].OpCode != OpCodes.Brtrue
        || helper[2].Operand != helper[12]
        || !helper.Skip(3).Take(6).Select(instruction =>
            instruction.Operand as string ?? string.Empty)
            .SequenceEqual(requiredStrings, StringComparer.Ordinal)
        || !IsCallTo(helper[9], sendRuntimeGuard)
        || helper[10].OpCode != OpCodes.Newobj
        || helper[10].Operand is not MethodReference constructor
        || constructor.Name != ".ctor"
        || constructor.DeclaringType.FullName != conditionCollection.FullName
        || helper[11].OpCode != OpCodes.Ret
        || helper[12].OpCode != OpCodes.Ldarg_0
        || helper[13].OpCode != OpCodes.Callvirt
        || helper[13].Operand is not MethodReference dequeue
        || dequeue.Name != "Dequeue"
        || dequeue.DeclaringType.FullName != cache.FieldType.FullName
        || helper[14].OpCode != OpCodes.Ret)
    {
        throw new InvalidDataException(
            "JudgementSpray condition-cache helper is missing its bounded"
            + " cached and replacement paths.");
    }

    MethodDefinition spawnProjectile = judgementSpray.Methods.Single(method =>
        method.Name == "SpawnProjectile"
        && method.IsStatic
        && method.Parameters.Count == 5
        && method.HasBody);
    Instruction[] spawn = spawnProjectile.Body.Instructions.ToArray();
    int helperCallIndex = Array.FindIndex(
        spawn,
        instruction => IsCallTo(instruction, takeConditions));
    if (helperCallIndex < 1
        || spawn.Count(instruction => IsCallTo(instruction, takeConditions)) != 1
        || spawn[helperCallIndex - 1].OpCode != OpCodes.Ldsfld
        || !IsReferenceTo(spawn[helperCallIndex - 1].Operand, cache)
        || spawn.Any(instruction =>
            instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference called
            && called.Name == "Dequeue"
            && called.DeclaringType.FullName == cache.FieldType.FullName))
    {
        throw new InvalidDataException(
            "JudgementSpray.SpawnProjectile does not route its locked cache"
            + " acquisition through the recovery helper.");
    }
    int helperOffset = spawn[helperCallIndex].Offset;
    bool insideFinallyProtectedTry = spawnProjectile.Body.ExceptionHandlers.Any(
        handler => handler.HandlerType == ExceptionHandlerType.Finally
            && handler.TryStart.Offset <= helperOffset
            && handler.TryEnd.Offset > helperOffset);
    if (!insideFinallyProtectedTry)
    {
        throw new InvalidDataException(
            "JudgementSpray cache acquisition is no longer protected by"
            + " the original monitor-finally region.");
    }
}

static bool HasCatchAll(MethodDefinition method)
{
    return method.Body.ExceptionHandlers.Count == 1
        && method.Body.ExceptionHandlers[0].HandlerType
            == ExceptionHandlerType.Catch
        && method.Body.ExceptionHandlers[0].CatchType?.FullName
            == "System.Object";
}

static bool IsCallTo(
    Instruction instruction,
    MethodDefinition method)
{
    return (instruction.OpCode == OpCodes.Call
            || instruction.OpCode == OpCodes.Callvirt)
           && IsReferenceTo(instruction.Operand, method);
}

static bool IsReferenceTo(object? operand, MemberReference member)
{
    return operand is MemberReference reference
           && reference.FullName == member.FullName;
}

static MethodDefinition RequireMethod(
    AssemblyDefinition assembly,
    string typeName,
    string methodName,
    int parameterCount)
{
    TypeDefinition type = AllTypes(assembly.MainModule).Single(
        candidate => candidate.FullName == typeName);
    return type.Methods.Single(
        method => method.Name == methodName
                  && method.Parameters.Count == parameterCount
                  && method.HasBody);
}

static string FormatCounts(IReadOnlyDictionary<string, int> counts)
{
    return string.Join(
        ", ",
        counts.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key + "=" + item.Value));
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
