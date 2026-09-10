using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class AvatarInventoryCloseScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        Type avatar = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar", true);
        MethodInfo deinitialize = avatar.GetMethod(
            "Deinitialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
        List<CodeInstruction> body = Decode(deinitialize);
        if (runtimePatchEnabled)
        {
            MethodInfo transpiler = typeof(
                Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.AvatarInventoryClosePatch",
                    true).GetMethod("Transpiler");
            body = new List<CodeInstruction>(
                (IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null, new object[] { body }));
        }

        int wrapper = FindCall(body, "CloseIfAvailable", null);
        int direct = FindCall(
            body, "Close", "Magicka.GameLogic.UI.InventoryBox");
        int guards = direct < 0 ? 0 : CountNullBranchesBefore(body, direct, 14);
        bool safe = wrapper >= 0 || guards >= 2;
        report.Add(
            "avatar_inventory.missing_play_state",
            Result(safe, safe ? "guarded" : "unguarded", "guarded"));
        report.Add(
            "avatar_inventory.missing_inventory",
            Result(safe, safe ? "guarded" : "unguarded", "guarded"));
        bool preserved = (wrapper >= 0 ? 1 : 0) + (direct >= 0 ? 1 : 0) == 1;
        report.Add(
            "avatar_inventory.valid_close",
            Result(preserved, preserved ? "one_close" : "wrong_count", "one_close"));
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadAvatarDeinitialize", typeof(void), Type.EmptyTypes,
            typeof(AvatarInventoryCloseScenarios), true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static int FindCall(
        List<CodeInstruction> body, string name, string declaringType)
    {
        for (int index = 0; index < body.Count; index++)
        {
            MethodBase method = body[index].operand as MethodBase;
            if (method != null && method.Name == name &&
                (declaringType == null ||
                 (method.DeclaringType != null &&
                  method.DeclaringType.FullName == declaringType)))
                return index;
        }
        return -1;
    }

    private static int CountNullBranchesBefore(
        List<CodeInstruction> body, int end, int distance)
    {
        int count = 0;
        int start = Math.Max(0, end - distance);
        for (int index = start; index < end; index++)
            if (body[index].opcode == OpCodes.Brfalse ||
                body[index].opcode == OpCodes.Brfalse_S)
                count++;
        return count;
    }

    private static ScenarioResult Result(
        bool passed, string actual, string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
