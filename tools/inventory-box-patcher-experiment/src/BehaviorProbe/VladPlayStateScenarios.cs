using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class VladPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        VladPlayStateHarness harness = new VladPlayStateHarness(
            magicka,
            runtimePatchEnabled);
        report.Add(
            "vlad.constructor_state_release",
            harness.Constructor());
        report.Add(
            "vlad.current_state_initialize",
            harness.Initialize());
    }
}

internal sealed class VladPlayStateHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo legacyPlayState;
    private readonly MethodInfo recentPlayState;
    private readonly MethodBase constructor;
    private readonly MethodInfo initialize;

    internal VladPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type vlad = magicka.GetType(
            "Magicka.GameLogic.Entities.Bosses.Vlad",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type matrix = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Matrix").MakeByRefType();

        legacyPlayState = RuntimeReflection.RequireField(vlad, "mPlayState");
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayState = recent == null ? null : recent.GetGetMethod();
        if (recentPlayState == null)
            throw new MissingMethodException(
                playState.FullName,
                "get_RecentPlayState");

        constructor = vlad.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            new Type[] { playState },
            null);
        initialize = vlad.GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { matrix },
            null);
        if (constructor == null || initialize == null)
            throw new MissingMethodException(vlad.FullName);
    }

    internal ScenarioResult Constructor()
    {
        List<CodeInstruction> instructions = Decode(constructor);
        if (runtimePatchEnabled)
            ApplyTranspiler("ConstructorTranspiler", instructions);
        int stores = Count(instructions, OpCodes.Stfld, legacyPlayState);
        return Result("stores:" + stores, "stores:0");
    }

    internal ScenarioResult Initialize()
    {
        List<CodeInstruction> instructions = Decode(initialize);
        if (runtimePatchEnabled)
            ApplyTranspiler("InitializeTranspiler", instructions);
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
            "legacy_reads:0,current_reads:2");
    }

    private static int Count(
        List<CodeInstruction> instructions,
        OpCode opcode,
        object operand)
    {
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

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadVladBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(VladPlayStateHarness),
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
            .VladPlayStatePatch).GetMethod(name);
        if (method == null)
            throw new MissingMethodException(
                typeof(Magicka.CommunityPatch.Runtime
                    .VladPlayStatePatch).FullName,
                name);
        object transformed = method.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange(
            (IEnumerable<CodeInstruction>)transformed);
    }
}
