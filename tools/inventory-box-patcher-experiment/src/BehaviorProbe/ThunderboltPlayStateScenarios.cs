using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ThunderboltPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ThunderboltPlayStateHarness harness =
            new ThunderboltPlayStateHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "thunderbolt.vector_state_release",
            harness.VectorExecute());
        report.Add(
            "thunderbolt.owner_state_release",
            harness.OwnerExecute());
        report.Add(
            "thunderbolt.current_state_cast",
            harness.Cast());
    }
}

internal sealed class ThunderboltPlayStateHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo legacyPlayState;
    private readonly MethodInfo recentPlayState;
    private readonly MethodInfo vectorExecute;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo cast;

    internal ThunderboltPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type thunderbolt = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Thunderbolt",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);

        legacyPlayState = thunderbolt.GetField(
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

        vectorExecute = RequireMethod(
            thunderbolt,
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { vector, playState });
        ownerExecute = RequireMethod(
            thunderbolt,
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { owner, playState });
        cast = RequireMethod(
            thunderbolt,
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            new Type[] { vector, vector, owner });
    }

    internal ScenarioResult VectorExecute()
    {
        return InspectStore(vectorExecute);
    }

    internal ScenarioResult OwnerExecute()
    {
        return InspectStore(ownerExecute);
    }

    internal ScenarioResult Cast()
    {
        List<CodeInstruction> instructions = Decode(cast);
        if (runtimePatchEnabled)
            ApplyTranspiler("CastTranspiler", instructions);
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
            "legacy_reads:0,current_reads:11");
    }

    private ScenarioResult InspectStore(MethodInfo method)
    {
        List<CodeInstruction> instructions = Decode(method);
        if (runtimePatchEnabled)
            ApplyTranspiler("ExecuteTranspiler", instructions);
        int stores = Count(instructions, OpCodes.Stfld, legacyPlayState);
        return Result("stores:" + stores, "stores:0");
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
        BindingFlags flags,
        Type[] parameters)
    {
        MethodInfo method = type.GetMethod(
            name,
            flags,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != typeof(bool))
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static List<CodeInstruction> Decode(MethodInfo method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadThunderboltBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(ThunderboltPlayStateHarness),
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
            .ThunderboltPlayStatePatch).GetMethod(name);
        if (method == null)
            throw new MissingMethodException(
                typeof(Magicka.CommunityPatch.Runtime
                    .ThunderboltPlayStatePatch).FullName,
                name);
        object transformed = method.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange(
            (IEnumerable<CodeInstruction>)transformed);
    }
}
