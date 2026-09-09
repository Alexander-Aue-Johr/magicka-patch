using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class GameSceneLightUpdateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        GameSceneLightUpdateHarness harness =
            new GameSceneLightUpdateHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "game_scene.light_update_current_state",
            harness.UsesCurrentPlayState());
        report.Add(
            "game_scene.light_update_call_preserved",
            harness.PreservesLightUpdateCall());
    }
}

internal sealed class GameSceneLightUpdateHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.GameSceneLightUpdatePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly FieldInfo capturedPlayStateField;
    private readonly MethodInfo recentPlayStateGetter;
    private readonly MethodInfo updateLightsMethod;
    private readonly List<CodeInstruction> instructions;

    internal GameSceneLightUpdateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        Type gameScene = magicka.GetType("Magicka.Levels.GameScene", true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type scene = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        capturedPlayStateField = RuntimeReflection.RequireField(
            gameScene,
            "mPlayState");
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayStateGetter = recent == null
            ? null
            : recent.GetGetMethod(true);
        if (recentPlayStateGetter == null)
            throw new MissingMethodException(
                playState.FullName,
                "get_RecentPlayState");
        updateLightsMethod = FindUpdateLights(scene);

        MethodInfo destroy = gameScene.GetMethod(
            "Destroy",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(bool) },
            null);
        if (destroy == null)
            throw new MissingMethodException(gameScene.FullName, "Destroy");
        DynamicMethod reader = new DynamicMethod(
            "ReadGameSceneLightUpdateBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(GameSceneLightUpdateScenarios),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            reader.GetILGenerator(),
            destroy);
        instructions = new List<CodeInstruction>();
        for (int index = 0; index < decoded.Count; index++)
            instructions.Add(decoded[index].GetCodeInstruction());

        if (runtimePatchEnabled)
            instructions = ApplyRuntimeTranspiler(instructions);
    }

    internal ScenarioResult UsesCurrentPlayState()
    {
        int capturedReads = CountOperand(OpCodes.Ldfld, capturedPlayStateField);
        int currentReads = CountOperand(OpCodes.Call, recentPlayStateGetter);
        string actual = "captured:" + capturedReads +
            ",current:" + currentReads;
        const string expected = "captured:0,current:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult PreservesLightUpdateCall()
    {
        int calls = CountOperand(OpCodes.Callvirt, updateLightsMethod) +
            CountOperand(OpCodes.Call, updateLightsMethod);
        string actual = "calls:" + calls;
        const string expected = "calls:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private List<CodeInstruction> ApplyRuntimeTranspiler(
        List<CodeInstruction> source)
    {
        Type patch = Type.GetType(PatchTypeName, false);
        MethodInfo transpiler = patch == null
            ? null
            : patch.GetMethod(
                "Transpiler",
                BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            return source;
        object transformed = transpiler.Invoke(
            null,
            new object[] { source });
        return new List<CodeInstruction>(
            (IEnumerable<CodeInstruction>)transformed);
    }

    private int CountOperand(OpCode opcode, object operand)
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

    private static MethodInfo FindUpdateLights(Type scene)
    {
        MethodInfo found = null;
        MethodInfo[] methods = scene.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "UpdateLights")
                continue;
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (parameters.Length != 4)
                continue;
            if (found != null)
                throw new AmbiguousMatchException(
                    scene.FullName + ".UpdateLights");
            found = methods[index];
        }
        if (found == null || found.ReturnType != typeof(void))
            throw new MissingMethodException(scene.FullName, "UpdateLights");
        return found;
    }
}
