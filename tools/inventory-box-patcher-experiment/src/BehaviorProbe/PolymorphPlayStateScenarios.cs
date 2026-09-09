using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class PolymorphPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        PolymorphPlayStateHarness harness =
            new PolymorphPlayStateHarness(
                magicka,
                runtimePatchEnabled);
        report.Add("polymorph.owner_state", harness.OwnerExecute());
        report.Add("polymorph.vector_state", harness.VectorExecute());
        report.Add("polymorph.target_state", harness.TargetExecute());
        report.Add("polymorph.remove_state", harness.OnRemove());
    }
}

internal sealed class PolymorphPlayStateHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.PolymorphPlayStatePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo playStateField;
    private readonly MethodInfo recentPlayStateGetter;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo vectorExecute;
    private readonly MethodInfo targetExecute;
    private readonly MethodInfo onRemove;

    internal PolymorphPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type polymorph = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Polymorph",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type entity = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");

        playStateField = polymorph.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayStateGetter = recent == null
            ? null
            : recent.GetGetMethod();
        ownerExecute = RequireMethod(
            polymorph,
            "Execute",
            new Type[] { owner, playState },
            typeof(bool));
        vectorExecute = RequireMethod(
            polymorph,
            "Execute",
            new Type[] { vector, playState },
            typeof(bool));
        targetExecute = RequireMethod(
            polymorph,
            "Execute",
            new Type[] { owner, entity, playState },
            typeof(bool));
        onRemove = RequireMethod(
            polymorph,
            "OnRemove",
            Type.EmptyTypes,
            typeof(void));
        if (recentPlayStateGetter == null)
            throw new MissingMethodException(
                playState.FullName,
                "get_RecentPlayState");
    }

    internal ScenarioResult OwnerExecute()
    {
        return Inspect(
            ownerExecute,
            "SecondArgumentTranspiler",
            0,
            0);
    }

    internal ScenarioResult VectorExecute()
    {
        return Inspect(
            vectorExecute,
            "SecondArgumentTranspiler",
            0,
            0);
    }

    internal ScenarioResult TargetExecute()
    {
        return Inspect(
            targetExecute,
            "ThirdArgumentTranspiler",
            0,
            0);
    }

    internal ScenarioResult OnRemove()
    {
        return Inspect(onRemove, "RemoveTranspiler", 0, 2);
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
            "ReadPolymorphBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(PolymorphPlayStateHarness),
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
