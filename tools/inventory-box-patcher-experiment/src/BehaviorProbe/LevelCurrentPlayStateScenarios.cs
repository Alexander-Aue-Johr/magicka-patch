using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class LevelCurrentPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        LevelCurrentPlayStateHarness harness =
            new LevelCurrentPlayStateHarness(magicka, runtimePatchEnabled);
        report.Add(
            "level_current_state.read_sites",
            harness.ReadSites());
        report.Add(
            "level_current_state.read_count_preserved",
            harness.ReadCountPreserved());
    }
}

internal sealed class LevelCurrentPlayStateHarness
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo playStateField;
    private readonly MethodInfo recentGetter;
    private readonly MethodInfo[] targets;
    private readonly MethodInfo[] transpilers;

    internal LevelCurrentPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type level = magicka.GetType("Magicka.Levels.Level", true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        playStateField = RequireField(level, "mPlayState");
        recentGetter = RequireGetter(playState, "RecentPlayState");

        Type state = level.GetNestedType(
            "State",
            BindingFlags.Public | BindingFlags.NonPublic);
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        targets = new MethodInfo[]
        {
            RequireMethod(
                state,
                "ApplyState",
                new Type[]
                {
                    typeof(List<>).MakeGenericType(typeof(int)),
                    typeof(Action<>).MakeGenericType(typeof(float))
                }),
            RequireMethod(level, "get_PlayState", Type.EmptyTypes),
            RequireMethod(
                level,
                "Update",
                new Type[] { dataChannel, typeof(float) }),
            RequireMethod(level, "ChangeScene", Type.EmptyTypes),
            RequireMethod(level, "ClearTransition", Type.EmptyTypes)
        };

        Type patch = typeof(
            Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.LevelCurrentPlayStatePatch",
                true);
        transpilers = new MethodInfo[]
        {
            patch.GetMethod("StateTranspiler"),
            patch.GetMethod("GetterTranspiler"),
            patch.GetMethod("UpdateTranspiler"),
            patch.GetMethod("ChangeTranspiler"),
            patch.GetMethod("ClearTranspiler")
        };
    }

    internal ScenarioResult ReadSites()
    {
        List<List<CodeInstruction>> bodies = ReadBodies();
        int stored = Count(bodies, OpCodes.Ldfld, playStateField);
        int current = Count(bodies, OpCodes.Call, recentGetter);
        string actual = "stored:" + stored + ",current:" + current;
        return new ScenarioResult(
            stored == 0 && current == 10,
            actual,
            "stored:0,current:10");
    }

    internal ScenarioResult ReadCountPreserved()
    {
        List<List<CodeInstruction>> bodies = ReadBodies();
        int stored = Count(bodies, OpCodes.Ldfld, playStateField);
        int current = Count(bodies, OpCodes.Call, recentGetter);
        int total = stored + current;
        return new ScenarioResult(
            total == 10,
            "reads:" + total,
            "reads:10");
    }

    private List<List<CodeInstruction>> ReadBodies()
    {
        List<List<CodeInstruction>> result =
            new List<List<CodeInstruction>>(targets.Length);
        for (int index = 0; index < targets.Length; index++)
        {
            List<CodeInstruction> body = Decode(targets[index]);
            if (runtimePatchEnabled)
            {
                object transformed = transpilers[index].Invoke(
                    null,
                    new object[] { body });
                body = new List<CodeInstruction>(
                    (IEnumerable<CodeInstruction>)transformed);
            }
            result.Add(body);
        }
        return result;
    }

    private static int Count(
        List<List<CodeInstruction>> bodies,
        OpCode opcode,
        object operand)
    {
        int count = 0;
        for (int bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
        {
            List<CodeInstruction> body = bodies[bodyIndex];
            for (int index = 0; index < body.Count; index++)
            {
                if (body[index].opcode == opcode &&
                    Object.Equals(body[index].operand, operand))
                    count++;
            }
        }
        return count;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, InstanceMembers);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static MethodInfo RequireGetter(Type type, string name)
    {
        PropertyInfo property = type.GetProperty(
            name,
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo getter = property == null ? null : property.GetGetMethod();
        if (getter == null)
            throw new MissingMethodException(type.FullName, "get_" + name);
        return getter;
    }

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        Type[] parameters)
    {
        MethodInfo method = type.GetMethod(
            name,
            InstanceMembers,
            null,
            parameters,
            null);
        if (method == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadLevelCurrentPlayStateBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(LevelCurrentPlayStateHarness),
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
