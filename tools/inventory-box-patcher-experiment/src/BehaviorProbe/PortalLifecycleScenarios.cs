using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;
using Harmony.ILCopying;

internal static class PortalLifecycleScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        PortalLifecycleHarness harness = new PortalLifecycleHarness(
            magicka,
            runtimePatchEnabled);
        report.Add("portal.execute_state_release", harness.Execute());
        report.Add("portal.vector_initialize_current_state", harness.VectorInitialize());
        report.Add("portal.message_initialize_current_state", harness.MessageInitialize());
        report.Add("portal.update_current_state", harness.Update());
        report.Add("portal.level_cleanup", harness.LevelCleanup());
    }
}

internal sealed class PortalLifecycleHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.PortalLifecyclePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly Type portal;
    private readonly Type portalEntity;
    private readonly FieldInfo outerPlayState;
    private readonly FieldInfo entityPlayState;
    private readonly FieldInfo singletonField;
    private readonly FieldInfo portalAField;
    private readonly FieldInfo portalBField;
    private readonly MethodInfo recentPlayState;
    private readonly MethodInfo execute;
    private readonly MethodInfo vectorInitialize;
    private readonly MethodInfo messageInitialize;
    private readonly MethodInfo update;
    private readonly MethodInfo manualDispose;

    internal PortalLifecycleHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        portal = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal",
            true);
        portalEntity = portal.GetNestedType(
            "PortalEntity",
            BindingFlags.Public | BindingFlags.NonPublic);
        if (portalEntity == null)
            throw new TypeLoadException(portal.FullName + "+PortalEntity");
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type message = magicka.GetType(
            "Magicka.Network.SpawnPortalMessage",
            true);
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");

        outerPlayState = portal.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        entityPlayState = FindField(portalEntity, "mPlayState");
        singletonField = RequireField(portal, "sSingelton", portal, true);
        portalAField = RequireField(
            portal,
            "sPortalA",
            portalEntity,
            true);
        portalBField = RequireField(
            portal,
            "sPortalB",
            portalEntity,
            true);
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayState = recent == null ? null : recent.GetGetMethod();
        execute = RequireMethod(
            portal,
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { owner, playState },
            typeof(bool));
        vectorInitialize = RequireMethod(
            portalEntity,
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { vector.MakeByRefType() },
            typeof(void));
        messageInitialize = RequireMethod(
            portalEntity,
            "Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            new Type[] { message.MakeByRefType() },
            typeof(void));
        update = RequireMethod(
            portalEntity,
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { dataChannel, typeof(float) },
            typeof(void));
        manualDispose = portal.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (recentPlayState == null)
            throw new MissingMethodException(
                playState.FullName,
                "get_RecentPlayState");
    }

    internal ScenarioResult Execute()
    {
        return Inspect(execute, "ExecuteTranspiler", outerPlayState, 0, 0);
    }

    internal ScenarioResult VectorInitialize()
    {
        return Inspect(
            vectorInitialize,
            "VectorInitializeTranspiler",
            entityPlayState,
            0,
            2);
    }

    internal ScenarioResult MessageInitialize()
    {
        return Inspect(
            messageInitialize,
            "MessageInitializeTranspiler",
            entityPlayState,
            0,
            1);
    }

    internal ScenarioResult Update()
    {
        return Inspect(update, "UpdateTranspiler", entityPlayState, 0, 1);
    }

    internal ScenarioResult LevelCleanup()
    {
        object originalSingleton = singletonField.GetValue(null);
        object originalA = portalAField.GetValue(null);
        object originalB = portalBField.GetValue(null);
        object singleton = NewUninitialized(portal);
        object portalA = NewUninitialized(portalEntity);
        object portalB = NewUninitialized(portalEntity);
        singletonField.SetValue(null, singleton);
        portalAField.SetValue(null, portalA);
        portalBField.SetValue(null, portalB);
        try
        {
            if (manualDispose != null)
            {
                manualDispose.Invoke(singleton, new object[0]);
            }
            else if (runtimePatchEnabled)
            {
                Type patch = Type.GetType(PatchTypeName, false);
                MethodInfo release = patch == null
                    ? null
                    : patch.GetMethod(
                        "ReleasePortalEntities",
                        BindingFlags.Static | BindingFlags.Public);
                if (release != null)
                    release.Invoke(null, new object[0]);
            }

            bool released = portalAField.GetValue(null) == null &&
                portalBField.GetValue(null) == null;
            bool singletonPreserved = ReferenceEquals(
                singletonField.GetValue(null),
                singleton);
            string actual = "portals:" +
                (released ? "released" : "retained") +
                ",singleton:" +
                (singletonPreserved ? "preserved" : "changed");
            const string expected =
                "portals:released,singleton:preserved";
            return new ScenarioResult(
                actual == expected,
                actual,
                expected);
        }
        finally
        {
            singletonField.SetValue(null, originalSingleton);
            portalAField.SetValue(null, originalA);
            portalBField.SetValue(null, originalB);
        }
    }

    private ScenarioResult Inspect(
        MethodBase method,
        string transpilerName,
        FieldInfo field,
        int expectedStores,
        int manualCurrentReads)
    {
        List<CodeInstruction> instructions = Decode(method);
        int expectedCurrentReads = manualCurrentReads;
        if (runtimePatchEnabled)
            instructions = Transform(instructions, transpilerName);
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

    private List<CodeInstruction> Transform(
        List<CodeInstruction> instructions,
        string transpilerName)
    {
        Type patch = Type.GetType(PatchTypeName, false);
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
        return new List<CodeInstruction>(
            (IEnumerable<CodeInstruction>)transformed);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null;
            current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static FieldInfo RequireField(
        Type type,
        string name,
        Type expectedType,
        bool isStatic)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly |
            (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        FieldInfo field = type.GetField(name, flags);
        if (field == null || field.FieldType != expectedType)
            throw new MissingFieldException(type.FullName, name);
        return field;
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
            "ReadPortalLifecycleBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(PortalLifecycleHarness),
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

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
