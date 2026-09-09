using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ConflagrationPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ConflagrationPlayStateHarness harness =
            new ConflagrationPlayStateHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "conflagration.vector_state",
            harness.VectorExecute());
        report.Add(
            "conflagration.direction_state",
            harness.DirectionExecute());
        report.Add(
            "conflagration.owner_state",
            harness.OwnerExecute());
        report.Add(
            "conflagration.update_state",
            harness.Update());
    }
}

internal sealed class ConflagrationPlayStateHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.ConflagrationPlayStatePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo playStateField;
    private readonly MethodInfo recentPlayStateGetter;
    private readonly MethodInfo vectorExecute;
    private readonly MethodInfo directionExecute;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo update;

    internal ConflagrationPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type conflagration = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                "Conflagration",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");

        playStateField = conflagration.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayStateGetter = recent == null
            ? null
            : recent.GetGetMethod();
        vectorExecute = RequireMethod(
            conflagration,
            "Execute",
            new Type[] { vector, playState },
            typeof(bool));
        directionExecute = RequireMethod(
            conflagration,
            "Execute",
            new Type[] { vector, vector, playState },
            typeof(bool));
        ownerExecute = RequireMethod(
            conflagration,
            "Execute",
            new Type[] { owner, playState },
            typeof(bool));
        update = RequireMethod(
            conflagration,
            "Update",
            new Type[] { dataChannel, typeof(float) },
            typeof(void));
        if (recentPlayStateGetter == null)
            throw new MissingMethodException(
                playState.FullName,
                "get_RecentPlayState");
    }

    internal ScenarioResult VectorExecute()
    {
        return Inspect(
            vectorExecute,
            "SecondArgumentTranspiler",
            0,
            0);
    }

    internal ScenarioResult DirectionExecute()
    {
        return Inspect(
            directionExecute,
            "ThirdArgumentTranspiler",
            0,
            0);
    }

    internal ScenarioResult OwnerExecute()
    {
        return Inspect(
            ownerExecute,
            "SecondArgumentTranspiler",
            0,
            0);
    }

    internal ScenarioResult Update()
    {
        return Inspect(update, "UpdateTranspiler", 0, 4);
    }

    private ScenarioResult Inspect(
        MethodInfo method,
        string transpilerName,
        int expectedStores,
        int expectedCurrentReads)
    {
        List<CodeInstruction> instructions = Decode(method);
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
        string expected = "stores:" + expectedStores +
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

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadConflagrationBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(ConflagrationPlayStateHarness),
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
