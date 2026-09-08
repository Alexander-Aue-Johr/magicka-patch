using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class SummonPhoenixPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        SummonPhoenixPlayStateHarness harness =
            new SummonPhoenixPlayStateHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "summon_phoenix.vector_state",
            harness.VectorExecute());
        report.Add(
            "summon_phoenix.owner_state",
            harness.OwnerExecute());
        report.Add(
            "summon_phoenix.update_state",
            harness.Update());
    }
}

internal sealed class SummonPhoenixPlayStateHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo legacyPlayState;
    private readonly MethodInfo recentPlayState;
    private readonly MethodInfo vectorExecute;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo update;

    internal SummonPhoenixPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type ability = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonPhoenix",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");

        legacyPlayState = RuntimeReflection.RequireField(
            ability,
            "sPlayState");
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayState = recent == null ? null : recent.GetGetMethod();
        if (recentPlayState == null)
            throw new MissingMethodException(
                playState.FullName,
                "get_RecentPlayState");

        vectorExecute = RequireMethod(
            ability,
            "Execute",
            new Type[] { vector, playState },
            typeof(bool));
        ownerExecute = RequireMethod(
            ability,
            "Execute",
            new Type[] { owner, playState },
            typeof(bool));
        update = RequireMethod(
            ability,
            "Update",
            new Type[] { dataChannel, typeof(float) },
            typeof(void));
    }

    internal ScenarioResult VectorExecute()
    {
        return Inspect(
            vectorExecute,
            "VectorExecuteTranspiler",
            3);
    }

    internal ScenarioResult OwnerExecute()
    {
        return Inspect(
            ownerExecute,
            "OwnerExecuteTranspiler",
            3);
    }

    internal ScenarioResult Update()
    {
        return Inspect(update, "UpdateTranspiler", 7);
    }

    private ScenarioResult Inspect(
        MethodInfo method,
        string transpilerName,
        int expectedCurrentReads)
    {
        List<CodeInstruction> instructions = Decode(method);
        if (runtimePatchEnabled)
            ApplyTranspiler(transpilerName, instructions);

        int stores = 0;
        int legacyReads = 0;
        int currentReads = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            CodeInstruction instruction = instructions[index];
            if (instruction.opcode == OpCodes.Stsfld &&
                Object.Equals(instruction.operand, legacyPlayState))
                stores++;
            else if (instruction.opcode == OpCodes.Ldsfld &&
                Object.Equals(instruction.operand, legacyPlayState))
                legacyReads++;
            else if (instruction.opcode == OpCodes.Call &&
                Object.Equals(instruction.operand, recentPlayState))
                currentReads++;
        }

        string actual = "stores:" + stores +
            ",legacy_reads:" + legacyReads +
            ",current_reads:" + currentReads;
        string expected = "stores:0,legacy_reads:0,current_reads:" +
            expectedCurrentReads;
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
            "ReadSummonPhoenixBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(SummonPhoenixPlayStateHarness),
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
            .SummonPhoenixPlayStatePatch).GetMethod(name);
        if (method == null)
            throw new MissingMethodException(
                typeof(Magicka.CommunityPatch.Runtime
                    .SummonPhoenixPlayStatePatch).FullName,
                name);
        object transformed = method.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange(
            (IEnumerable<CodeInstruction>)transformed);
    }
}
