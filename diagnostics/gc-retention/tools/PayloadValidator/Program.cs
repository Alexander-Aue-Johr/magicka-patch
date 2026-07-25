using Mono.Cecil;
using Mono.Cecil.Cil;

const string RuntimeAssemblyName = "Magicka.GcDiagnostics";
const string RegistryTypeName = "Magicka.GcDiagnostics.RetentionRegistry";

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

using AssemblyDefinition magicka = AssemblyDefinition.ReadAssembly(
    magickaPath,
    readerParameters);
using AssemblyDefinition polygonHead = AssemblyDefinition.ReadAssembly(
    polygonHeadPath,
    readerParameters);

Dictionary<string, int> magickaCalls = ValidateInstrumentedAssembly(
    magicka,
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
Dictionary<string, int> polygonHeadCalls = ValidateInstrumentedAssembly(
    polygonHead,
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

    FieldDefinition[] registryVersionFields = registry.Fields
        .Where(field => field.Name == "RegistryVersion")
        .ToArray();
    if (registryVersionFields.Length != 1
        || !registryVersionFields[0].IsStatic
        || registryVersionFields[0].FieldType.FullName != "System.Int64")
    {
        throw new InvalidDataException(
            "Expected one static System.Int64 "
            + RegistryTypeName
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
}

static Dictionary<string, int> ValidateInstrumentedAssembly(
    AssemblyDefinition assembly,
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

                MethodDefinition? resolved = called.Resolve();
                if (resolved is null
                    || resolved.Module.Assembly.Name.Name != RuntimeAssemblyName)
                {
                    throw new InvalidDataException(
                        "Could not resolve injected call " + called.FullName
                        + " in " + method.FullName);
                }

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

static void ValidateBodyReferences(MethodDefinition method)
{
    HashSet<Instruction> instructions = new HashSet<Instruction>(
        method.Body.Instructions);
    foreach (Instruction instruction in method.Body.Instructions)
    {
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
