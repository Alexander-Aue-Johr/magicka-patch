using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class PlayStateCheckpointSendScenarios
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
        MethodInfo initialize = playState == null ? null : playState.GetMethod(
            "Initialize", Members, null, Type.EmptyTypes, null);
        if (initialize == null || magicka.GetName().Version.Minor < 10)
        {
            report.AddNotApplicable(
                "play_state_checkpoint.empty_payload",
                "The current checkpoint-send contract is not present.");
            report.AddNotApplicable(
                "play_state_checkpoint.nonempty_control",
                "The current checkpoint-send contract is not present.");
            return;
        }

        List<CodeInstruction> body = Decode(initialize);
        if (runtimePatchEnabled)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
                .Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.PlayStateCheckpointSendPatch",
                    true);
            patch.GetMethod("FindTarget", Members).Invoke(
                null, new object[] { magicka });
            body = new List<CodeInstruction>((IEnumerable<CodeInstruction>)
                patch.GetMethod("Transpiler").Invoke(null, new object[] { body }));
        }

        int safe = Count(body, "SendCheckpointWithNullEmptyPointer");
        int sends = CountCheckpointSends(body, playState);
        bool manualGuard = !runtimePatchEnabled && sends >= 2;
        bool normalized = runtimePatchEnabled ? safe == 1 : manualGuard;
        report.Add(
            "play_state_checkpoint.empty_payload",
            new ScenarioResult(normalized,
                normalized ? "null-empty" : "raw-empty", "null-empty"));
        bool control = runtimePatchEnabled ? safe == 1 : sends >= 1;
        report.Add(
            "play_state_checkpoint.nonempty_control",
            new ScenarioResult(control,
                control ? "forwarded" : "missing", "forwarded"));
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadCheckpointSendBody", typeof(void), Type.EmptyTypes,
            typeof(PlayStateCheckpointSendScenarios), true);
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

    private static int CountCheckpointSends(
        List<CodeInstruction> body, Type playState)
    {
        FieldInfo checkpoint = playState.GetField("mCheckpointStream", Members);
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodBase method = body[index].operand as MethodBase;
            if (method == null || method.Name != "SendRaw")
                continue;
            int start = Math.Max(0, index - 20);
            for (int previous = index - 1; previous >= start; previous--)
                if (body[previous].opcode == OpCodes.Ldfld &&
                    Object.Equals(body[previous].operand, checkpoint))
                {
                    count++;
                    break;
                }
        }
        return count;
    }
}
