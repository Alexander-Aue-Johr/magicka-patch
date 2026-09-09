using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class BossFightOrderingScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        BossFightOrderingHarness harness = new BossFightOrderingHarness(
            magicka,
            runtimePatchEnabled);
        report.Add("boss_fight.setup_state_release", harness.SetupStateRelease());
        report.Add("boss_fight.initialize_current_state", harness.InitializeCurrentState());
        report.Add("boss_fight.reset_current_state", harness.ResetCurrentState());
        report.Add("boss_fight.update_current_state", harness.UpdateCurrentState());
        report.Add("boss_fight.pending_initialize", harness.PendingInitialize());
        report.Add("boss_fight.pending_start", harness.PendingStart());
        report.Add("boss_fight.pending_clear", harness.PendingClear());
        report.Add("boss_fight.original_shape", harness.OriginalShape());
    }
}

internal sealed class BossFightOrderingHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.BossFightOrderingPatch, " +
        "Magicka.CommunityPatch.Runtime";
    private const string StateTypeName =
        "Magicka.CommunityPatch.Runtime.BossFightPendingState, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly Type bossFightType;
    private readonly FieldInfo playStateField;
    private readonly MethodInfo recentPlayState;
    private readonly MethodInfo setup;
    private readonly MethodInfo initialize;
    private readonly MethodInfo start;
    private readonly MethodInfo clear;
    private readonly MethodInfo reset;
    private readonly MethodInfo update;

    internal BossFightOrderingHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        bossFightType = magicka.GetType(
            "Magicka.GameLogic.Entities.Bosses.BossFight",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        playStateField = bossFightType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayState = recent == null ? null : recent.GetGetMethod();
        if (recentPlayState == null)
            throw new MissingMethodException(playState.FullName, "get_RecentPlayState");

        setup = RequireMethod(
            "Setup",
            new Type[] { playState, typeof(float), typeof(float), typeof(float) });
        initialize = FindInitialize();
        start = RequireMethod("Start", Type.EmptyTypes);
        clear = RequireMethod("Clear", Type.EmptyTypes);
        reset = RequireMethod("Reset", Type.EmptyTypes);
        update = RequireMethod(
            "Update",
            new Type[] { dataChannel, typeof(float) });
    }

    internal ScenarioResult SetupStateRelease()
    {
        return InspectState(setup, "SetupTranspiler", 0, 0);
    }

    internal ScenarioResult InitializeCurrentState()
    {
        return InspectState(initialize, "InitializeTranspiler", 0, 2);
    }

    internal ScenarioResult ResetCurrentState()
    {
        return InspectState(reset, "ResetTranspiler", 0, 2);
    }

    internal ScenarioResult UpdateCurrentState()
    {
        return InspectState(update, "UpdateTranspiler", 0, 1);
    }

    internal ScenarioResult PendingInitialize()
    {
        if (!runtimePatchEnabled)
        {
            bool fields = HasField("mPendingBossInitializations") &&
                HasField("mPendingBossAreaHashes") &&
                HasField("mPendingBossUniqueIds");
            bool queueCall = CountCall(
                Decode(initialize),
                "QueuePendingBossInitialization") == 1;
            return Result(
                "fields:" + fields + ",queue_calls:" + queueCall,
                "fields:True,queue_calls:True");
        }

        Type state = RequireRuntimeType(StateTypeName);
        Invoke(state, "Clear");
        object first = new object();
        Invoke(state, "Queue", first, 11, 21);
        Invoke(state, "Queue", first, 12, 22);
        object second = new object();
        Invoke(state, "Queue", second, 13, 23);
        int queued = (int)Invoke(state, "get_Count");
        Invoke(state, "Remove", first);
        int remaining = (int)Invoke(state, "get_Count");
        Invoke(state, "Clear");
        return Result(
            "queued:" + queued + ",remaining:" + remaining,
            "queued:2,remaining:1");
    }

    internal ScenarioResult PendingStart()
    {
        if (!runtimePatchEnabled)
        {
            List<CodeInstruction> body = Decode(start);
            bool field = HasField("mPendingStart");
            bool pendingStore = CountField(body, "mPendingStart", OpCodes.Stfld) == 1;
            return Result(
                "field:" + field + ",stores:" + pendingStore,
                "field:True,stores:True");
        }

        Type state = RequireRuntimeType(StateTypeName);
        Invoke(state, "Clear");
        object boss = new object();
        Invoke(state, "Queue", boss, 1, 2);
        bool runNow = (bool)Invoke(state, "ShouldRunStart", true);
        Invoke(state, "Remove", boss);
        bool replay = (bool)Invoke(state, "TakePendingStartIfReady");
        bool replayAgain = (bool)Invoke(state, "TakePendingStartIfReady");
        Invoke(state, "Clear");
        return Result(
            "run_now:" + runNow + ",replay:" + replay +
                ",replay_again:" + replayAgain,
            "run_now:False,replay:True,replay_again:False");
    }

    internal ScenarioResult PendingClear()
    {
        if (!runtimePatchEnabled)
        {
            bool clearCalls = CountCall(Decode(clear), "Clear") >= 5;
            bool resetCalls = CountCall(Decode(reset), "Clear") >= 5;
            return Result(
                "clear:" + clearCalls + ",reset:" + resetCalls,
                "clear:True,reset:True");
        }

        Type state = RequireRuntimeType(StateTypeName);
        Invoke(state, "Clear");
        object boss = new object();
        Invoke(state, "Queue", boss, 1, 2);
        Invoke(state, "ShouldRunStart", true);
        Invoke(state, "Clear");
        int count = (int)Invoke(state, "get_Count");
        bool replay = (bool)Invoke(state, "TakePendingStartIfReady");
        return Result(
            "count:" + count + ",replay:" + replay,
            "count:0,replay:False");
    }

    internal ScenarioResult OriginalShape()
    {
        List<CodeInstruction> initializeBody = Body(
            initialize,
            "InitializeTranspiler");
        List<CodeInstruction> updateBody = Body(
            update,
            "UpdateTranspiler");
        string actual =
            "locator:" + CountCall(initializeBody, "GetLocator") +
            ",boss_init:" + CountCall(initializeBody, "Initialize") +
            ",boss_update:" + CountCall(updateBody, "UpdateBoss");
        return Result(
            actual,
            "locator:1,boss_init:1,boss_update:1");
    }

    private ScenarioResult InspectState(
        MethodInfo method,
        string transpiler,
        int expectedStores,
        int expectedCurrentReads)
    {
        List<CodeInstruction> instructions = Body(method, transpiler);
        int stores = Count(instructions, OpCodes.Stfld, playStateField);
        int legacyReads = Count(instructions, OpCodes.Ldfld, playStateField);
        int currentReads = Count(instructions, OpCodes.Call, recentPlayState);
        return Result(
            "stores:" + stores + ",legacy_reads:" + legacyReads +
                ",current_reads:" + currentReads,
            "stores:" + expectedStores + ",legacy_reads:0,current_reads:" +
                expectedCurrentReads);
    }

    private List<CodeInstruction> Body(MethodInfo method, string transpilerName)
    {
        List<CodeInstruction> instructions = Decode(method);
        if (!runtimePatchEnabled)
            return instructions;
        Type patch = RequireRuntimeType(PatchTypeName);
        MethodInfo transpiler = patch.GetMethod(
            transpilerName,
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patch.FullName, transpilerName);
        object transformed = transpiler.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange((IEnumerable<CodeInstruction>)transformed);
        return instructions;
    }

    private bool HasField(string name)
    {
        return bossFightType.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly) != null;
    }

    private static object Invoke(Type type, string method, params object[] arguments)
    {
        MethodInfo target = type.GetMethod(
            method,
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (target == null)
            throw new MissingMethodException(type.FullName, method);
        return target.Invoke(null, arguments);
    }

    private static Type RequireRuntimeType(string name)
    {
        Type type = Type.GetType(name, false);
        if (type == null)
            throw new TypeLoadException(name);
        return type;
    }

    private MethodInfo FindInitialize()
    {
        MethodInfo[] methods = bossFightType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name == "Initialize" &&
                methods[index].ReturnType == typeof(void) &&
                parameters.Length == 3 &&
                parameters[1].ParameterType == typeof(int) &&
                parameters[2].ParameterType == typeof(int))
                return methods[index];
        }
        throw new MissingMethodException(bossFightType.FullName, "Initialize");
    }

    private MethodInfo RequireMethod(string name, Type[] parameters)
    {
        MethodInfo method = bossFightType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != typeof(void))
            throw new MissingMethodException(bossFightType.FullName, name);
        return method;
    }

    private static int CountCall(
        List<CodeInstruction> instructions,
        string name)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodBase method = instructions[index].operand as MethodBase;
            if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                method != null && method.Name == name)
                count++;
        }
        return count;
    }

    private static int CountField(
        List<CodeInstruction> instructions,
        string name,
        OpCode opcode)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            FieldInfo field = instructions[index].operand as FieldInfo;
            if (instructions[index].opcode == opcode &&
                field != null && field.Name == name)
                count++;
        }
        return count;
    }

    private static int Count(
        List<CodeInstruction> instructions,
        OpCode opcode,
        MemberInfo operand)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode == opcode &&
                SameMember(instructions[index].operand, operand))
                count++;
        }
        return count;
    }

    private static bool SameMember(object left, MemberInfo right)
    {
        MemberInfo member = left as MemberInfo;
        return member != null && right != null &&
            member.Module == right.Module &&
            member.MetadataToken == right.MetadataToken;
    }

    private static ScenarioResult Result(string actual, string expected)
    {
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static List<CodeInstruction> Decode(MethodInfo method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadBossFightBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(BossFightOrderingHarness),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }
}
