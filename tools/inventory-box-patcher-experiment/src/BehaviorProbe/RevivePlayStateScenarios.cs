using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class RevivePlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        RevivePlayStateHarness harness = new RevivePlayStateHarness(
            magicka,
            runtimePatchEnabled);
        report.Add("revive.execute_state", harness.Execute());
        report.Add("revive.update_state", harness.Update());
    }
}

internal sealed class RevivePlayStateHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.RevivePlayStatePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo playStateField;
    private readonly MethodInfo recentPlayStateGetter;
    private readonly MethodInfo execute;
    private readonly MethodInfo update;

    internal RevivePlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type revive = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Revive",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");

        playStateField = revive.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayStateGetter = recent == null
            ? null
            : recent.GetGetMethod();
        execute = revive.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { vector, playState, typeof(float) },
            null);
        update = revive.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannel, typeof(float) },
            null);
        if (execute == null || execute.ReturnType != typeof(bool))
            throw new MissingMethodException(
                revive.FullName,
                "Execute(Vector3, PlayState, Single)");
        if (update == null || update.ReturnType != typeof(void))
            throw new MissingMethodException(revive.FullName, "Update");
        if (recentPlayStateGetter == null)
            throw new MissingMethodException(
                playState.FullName,
                "get_RecentPlayState");
    }

    internal ScenarioResult Execute()
    {
        return Inspect(execute, "ExecuteTranspiler", 2, 2);
    }

    internal ScenarioResult Update()
    {
        return Inspect(update, "UpdateTranspiler", 3, 7);
    }

    private ScenarioResult Inspect(
        MethodInfo method,
        string transpilerName,
        int replacedReads,
        int manualCurrentReads)
    {
        List<CodeInstruction> instructions = Decode(method);
        int originalCurrentReads = Count(
            instructions,
            OpCodes.Call,
            recentPlayStateGetter);
        int expectedCurrentReads = playStateField == null
            ? manualCurrentReads
            : originalCurrentReads + replacedReads;
        if (runtimePatchEnabled)
            instructions = Transform(instructions, transpilerName);
        int stores = Count(instructions, OpCodes.Stfld, playStateField);
        int legacyReads = Count(
            instructions,
            OpCodes.Ldfld,
            playStateField);
        int currentReads = Count(
            instructions,
            OpCodes.Call,
            recentPlayStateGetter);
        string actual = "stores:" + stores +
            ",legacy_reads:" + legacyReads +
            ",current_reads:" + currentReads;
        string expected = "stores:0" +
            ",legacy_reads:0,current_reads:" + expectedCurrentReads;
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static List<CodeInstruction> Transform(
        List<CodeInstruction> instructions,
        string transpilerName)
    {
        Type patch = Type.GetType(PatchTypeName, true);
        MethodInfo transpiler = patch.GetMethod(
            transpilerName,
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patch.FullName, transpilerName);
        object transformed = transpiler.Invoke(
            null,
            new object[] { instructions });
        return new List<CodeInstruction>(
            (IEnumerable<CodeInstruction>)transformed);
    }

    private static int Count(
        List<CodeInstruction> instructions,
        OpCode opcode,
        MemberInfo member)
    {
        if (member == null)
            return 0;
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            MemberInfo operand = instructions[index].operand as MemberInfo;
            if (instructions[index].opcode == opcode &&
                operand != null &&
                operand.Module == member.Module &&
                operand.MetadataToken == member.MetadataToken)
                count++;
        }
        return count;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadReviveBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(RevivePlayStateHarness),
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
