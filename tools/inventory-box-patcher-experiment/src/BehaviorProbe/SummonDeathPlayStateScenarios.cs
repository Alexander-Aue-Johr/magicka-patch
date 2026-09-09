using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class SummonDeathPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        SummonDeathPlayStateHarness harness =
            new SummonDeathPlayStateHarness(
                magicka,
                runtimePatchEnabled);
        report.Add("summon_death.owner_state", harness.OwnerExecute());
        report.Add("summon_death.vector_state", harness.VectorExecute());
        report.Add("summon_death.spawn_state", harness.Spawn());
        report.Add("summon_death.spawn_shape", harness.SpawnShape());
        report.Add("death_entity.constructor_state", harness.Constructor());
        report.Add("death_entity.initialize_state", harness.Initialize());
        report.Add("death_entity.update_state", harness.Update());
        report.Add("death_entity.deinitialize_state", harness.Deinitialize());
    }
}

internal sealed class SummonDeathPlayStateHarness
{
    private const string OuterPatchName =
        "Magicka.CommunityPatch.Runtime.SummonDeathPlayStatePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly Type outer;
    private readonly Type death;
    private readonly FieldInfo outerPlayState;
    private readonly FieldInfo entityPlayState;
    private readonly MethodInfo recentPlayState;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo vectorExecute;
    private readonly MethodInfo spawn;
    private readonly ConstructorInfo constructor;
    private readonly MethodInfo initialize;
    private readonly MethodInfo update;
    private readonly MethodInfo deinitialize;

    internal SummonDeathPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        outer = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonDeath",
            true);
        death = outer.GetNestedType(
            "MagickDeath",
            BindingFlags.Public | BindingFlags.NonPublic);
        if (death == null)
            throw new TypeLoadException(outer.FullName + "+MagickDeath");
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type character = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type matrix = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Matrix");
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");

        outerPlayState = outer.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        entityPlayState = RuntimeReflection.RequireField(death, "mPlayState");
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayState = recent == null ? null : recent.GetGetMethod();

        ownerExecute = RequireMethod(
            outer,
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { owner, playState },
            typeof(bool));
        vectorExecute = RequireMethod(
            outer,
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { vector, playState },
            typeof(bool));
        spawn = RequireMethod(
            outer,
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            new Type[] { vector },
            typeof(bool));
        constructor = death.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { playState },
            null);
        initialize = RequireMethod(
            death,
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { matrix.MakeByRefType(), playState, character },
            typeof(void));
        update = RequireMethod(
            death,
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { dataChannel, typeof(float) },
            typeof(void));
        deinitialize = RequireMethod(
            death,
            "Deinitialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            Type.EmptyTypes,
            typeof(void));
        if (recentPlayState == null || constructor == null)
            throw new MissingMemberException(
                "SummonDeath play-state contract is incomplete.");
    }

    internal ScenarioResult OwnerExecute()
    {
        return Inspect(
            ownerExecute,
            "OwnerExecuteTranspiler",
            outerPlayState,
            0,
            0);
    }

    internal ScenarioResult VectorExecute()
    {
        return Inspect(
            vectorExecute,
            "VectorExecuteTranspiler",
            outerPlayState,
            0,
            0);
    }

    internal ScenarioResult Spawn()
    {
        return Inspect(
            spawn,
            "SpawnTranspiler",
            outerPlayState,
            0,
            6);
    }

    internal ScenarioResult Constructor()
    {
        return Inspect(
            constructor,
            "DeathConstructorTranspiler",
            entityPlayState,
            0,
            0);
    }

    internal ScenarioResult Initialize()
    {
        return Inspect(
            initialize,
            "DeathInitializeTranspiler",
            entityPlayState,
            0,
            1);
    }

    internal ScenarioResult Update()
    {
        return Inspect(
            update,
            "DeathUpdateTranspiler",
            entityPlayState,
            0,
            12);
    }

    internal ScenarioResult Deinitialize()
    {
        return Inspect(
            deinitialize,
            "DeathDeinitializeTranspiler",
            entityPlayState,
            0,
            1);
    }

    internal ScenarioResult SpawnShape()
    {
        List<CodeInstruction> instructions = Body(
            spawn,
            "SpawnTranspiler");
        string actual =
            "queries:" + CountCall(instructions, "GetEntities", null) +
            ",returns:" + CountCall(instructions, "ReturnEntityList", null) +
            ",nav:" + CountCall(instructions, "GetNearestPosition", null) +
            ",death_init:" + CountCall(
                instructions,
                "Initialize",
                death.FullName) +
            ",add:" + CountCall(instructions, "AddEntity", null);
        const string expected =
            "queries:1,returns:1,nav:1,death_init:1,add:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private ScenarioResult Inspect(
        MethodBase method,
        string transpiler,
        FieldInfo field,
        int expectedStores,
        int expectedCurrentReads)
    {
        List<CodeInstruction> instructions = Body(method, transpiler);
        int stores = Count(instructions, OpCodes.Stfld, field);
        int legacyReads = Count(instructions, OpCodes.Ldfld, field);
        int currentReads = Count(
            instructions,
            OpCodes.Call,
            recentPlayState);
        string actual = "stores:" + stores +
            ",legacy_reads:" + legacyReads +
            ",current_reads:" + currentReads;
        string expected = "stores:" + expectedStores +
            ",legacy_reads:0,current_reads:" + expectedCurrentReads;
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private List<CodeInstruction> Body(
        MethodBase method,
        string transpilerName)
    {
        List<CodeInstruction> instructions = Decode(method);
        if (!runtimePatchEnabled)
            return instructions;
        Type patch = Type.GetType(OuterPatchName, false);
        if (patch == null)
            return instructions;
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
                SameMember(instructions[index].operand, operand))
                count++;
        }
        return count;
    }

    private static bool SameMember(object left, object right)
    {
        MemberInfo leftMember = left as MemberInfo;
        MemberInfo rightMember = right as MemberInfo;
        if (leftMember == null || rightMember == null)
            return Object.Equals(left, right);
        return leftMember.Module == rightMember.Module &&
            leftMember.MetadataToken == rightMember.MetadataToken;
    }

    private static int CountCall(
        List<CodeInstruction> instructions,
        string name,
        string declaringType)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodBase method = instructions[index].operand as MethodBase;
            if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                method != null && method.Name == name &&
                (declaringType == null ||
                    method.DeclaringType.FullName == declaringType))
                count++;
        }
        return count;
    }

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        BindingFlags flags,
        Type[] parameters,
        Type returnType)
    {
        MethodInfo method = type.GetMethod(
            name,
            flags,
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
            "ReadSummonDeathBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(SummonDeathPlayStateHarness),
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
