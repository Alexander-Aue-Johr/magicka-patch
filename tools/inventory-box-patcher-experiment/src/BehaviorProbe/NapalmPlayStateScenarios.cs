using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class NapalmPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        NapalmPlayStateHarness harness = new NapalmPlayStateHarness(
            magicka,
            runtimePatchEnabled);
        report.Add(
            "napalm.execute_state_release",
            harness.Execute());
        report.Add(
            "napalm.current_state_update",
            harness.Update());
    }
}

internal sealed class NapalmPlayStateHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo legacyPlayState;
    private readonly MethodInfo recentPlayState;
    private readonly MethodInfo execute;
    private readonly MethodInfo update;

    internal NapalmPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type napalm = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Napalm",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");

        legacyPlayState = napalm.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayState = recent == null ? null : recent.GetGetMethod();
        if (recentPlayState == null)
            throw new MissingMethodException(
                playState.FullName,
                "get_RecentPlayState");

        execute = RequireMethod(
            napalm,
            "Execute",
            new Type[] { owner, playState },
            typeof(bool));
        update = RequireMethod(
            napalm,
            "Update",
            new Type[] { dataChannel, typeof(float) },
            typeof(void));
    }

    internal ScenarioResult Execute()
    {
        List<CodeInstruction> instructions = Decode(execute);
        if (runtimePatchEnabled)
            ApplyTranspiler("ExecuteTranspiler", instructions);
        int stores = Count(instructions, OpCodes.Stfld, legacyPlayState);
        return Result("stores:" + stores, "stores:0");
    }

    internal ScenarioResult Update()
    {
        List<CodeInstruction> instructions = Decode(update);
        if (runtimePatchEnabled)
            ApplyTranspiler("UpdateTranspiler", instructions);
        int legacyReads = Count(
            instructions,
            OpCodes.Ldfld,
            legacyPlayState);
        int currentReads = Count(
            instructions,
            OpCodes.Call,
            recentPlayState);
        return Result(
            "legacy_reads:" + legacyReads +
                ",current_reads:" + currentReads,
            "legacy_reads:0,current_reads:10");
    }

    private static int Count(
        List<CodeInstruction> instructions,
        OpCode opcode,
        object operand)
    {
        if (operand == null)
            return 0;
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode == opcode &&
                Object.Equals(instructions[index].operand, operand))
                count++;
        }
        return count;
    }

    private static ScenarioResult Result(string actual, string expected)
    {
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        Type[] parameters,
        Type returnType)
    {
        MethodInfo method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != returnType)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static List<CodeInstruction> Decode(MethodInfo method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadNapalmBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(NapalmPlayStateHarness),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result =
            new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static void ApplyTranspiler(
        string name,
        List<CodeInstruction> instructions)
    {
        MethodInfo method = typeof(Magicka.CommunityPatch.Runtime
            .NapalmPlayStatePatch).GetMethod(name);
        if (method == null)
            throw new MissingMethodException(
                typeof(Magicka.CommunityPatch.Runtime
                    .NapalmPlayStatePatch).FullName,
                name);
        object transformed = method.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange(
            (IEnumerable<CodeInstruction>)transformed);
    }
}
