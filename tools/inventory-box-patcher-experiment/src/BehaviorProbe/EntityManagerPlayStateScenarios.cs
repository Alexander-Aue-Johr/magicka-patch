using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class EntityManagerPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The legacy EntityManager cache-lifetime contract differs.";
            report.AddNotApplicable(
                "entity_manager_state.retention",
                reason);
            report.AddNotApplicable(
                "entity_manager_state.cache_state",
                reason);
            return;
        }
        Type manager = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        ConstructorInfo constructor = manager.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { playState },
            null);
        if (constructor == null)
            throw new MissingMethodException(manager.FullName, ".ctor");
        FieldInfo field = manager.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        MethodInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();
        List<CodeInstruction> body = Decode(constructor);
        if (runtimePatchEnabled)
        {
            Type patch = typeof(
                Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.EntityManagerPlayStatePatch",
                    true);
            MethodInfo transpiler = patch.GetMethod(
                "Transpiler",
                BindingFlags.Static | BindingFlags.Public);
            body = new List<CodeInstruction>(
                (IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null,
                    new object[] { body }));
        }

        int stores = Count(body, OpCodes.Stfld, field);
        int reads = Count(body, OpCodes.Ldfld, field);
        int currentReads = Count(body, OpCodes.Call, recent);
        report.Add(
            "entity_manager_state.retention",
            Result("stores", stores, 0));
        string actual = "legacy_reads:" + reads +
            ",current_reads:" + currentReads;
        const string expected = "legacy_reads:0,current_reads:3";
        report.Add(
            "entity_manager_state.cache_state",
            new ScenarioResult(actual == expected, actual, expected));
    }

    private static int Count(
        List<CodeInstruction> body,
        OpCode opcode,
        object operand)
    {
        if (operand == null)
            return 0;
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            if (body[index].opcode == opcode &&
                Object.Equals(body[index].operand, operand))
                count++;
        }
        return count;
    }

    private static ScenarioResult Result(
        string name,
        int actualValue,
        int expectedValue)
    {
        string actual = name + ":" + actualValue;
        string expected = name + ":" + expectedValue;
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadEntityManagerConstructor",
            typeof(void),
            Type.EmptyTypes,
            typeof(EntityManagerPlayStateScenarios),
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
}
