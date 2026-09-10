using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class PlayStateExitRenderingScenarios
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;

    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState", false);
        MethodInfo target = playState == null ? null : FindTarget(playState);
        if (target == null)
        {
            report.AddNotApplicable(
                "play_state_exit.render_serialization",
                "The PlayState.OnExit load task is not present.");
            report.AddNotApplicable(
                "play_state_exit.control_flow",
                "The PlayState.OnExit load task is not present.");
            return;
        }

        List<CodeInstruction> body = Decode(target);
        if (runtimePatchEnabled)
            body = new List<CodeInstruction>(
                Magicka.CommunityPatch.Runtime.PlayStateExitRenderingPatch
                    .Transpiler(body));

        bool serialized = Count(body, "AwaitDisabledRendering") == 1 &&
            Count(body, "EnableRendering") == 1;
        report.Add(
            "play_state_exit.render_serialization",
            new ScenarioResult(serialized,
                serialized ? "await+enable" : "unsynchronized",
                "await+enable"));

        int returns = 0;
        for (int index = 0; index < body.Count; index++)
            if (body[index].opcode == OpCodes.Ret)
                returns++;
        bool control = returns == 1 && Count(body, "Dispose") >= 1;
        report.Add(
            "play_state_exit.control_flow",
            new ScenarioResult(control, returns + ":" + Count(body, "Dispose"),
                "1:>=1"));
    }

    private static MethodInfo FindTarget(Type playState)
    {
        MethodInfo[] methods = playState.GetMethods(Members);
        for (int index = 0; index < methods.Length; index++)
            if (methods[index].IsStatic &&
                methods[index].Name.StartsWith("<OnExit>b__") &&
                methods[index].GetParameters().Length == 0)
                return methods[index];

        Type[] nested = playState.GetNestedTypes(Members);
        for (int typeIndex = 0; typeIndex < nested.Length; typeIndex++)
        {
            methods = nested[typeIndex].GetMethods(Members);
            for (int index = 0; index < methods.Length; index++)
            {
                if (!methods[index].Name.StartsWith("<OnExit>b__") ||
                    methods[index].GetParameters().Length != 0)
                    continue;
                List<CodeInstruction> body = Decode(methods[index]);
                if (Count(body, "AwaitDisabledRendering") == 1 &&
                    Count(body, "EnableRendering") == 1)
                    return methods[index];
            }
        }
        return null;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadPlayStateExitBody", typeof(void), Type.EmptyTypes,
            typeof(PlayStateExitRenderingScenarios), true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static int Count(List<CodeInstruction> body, string name)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodBase method = body[index].operand as MethodBase;
            if (method != null && method.Name == name)
                count++;
        }
        return count;
    }
}
