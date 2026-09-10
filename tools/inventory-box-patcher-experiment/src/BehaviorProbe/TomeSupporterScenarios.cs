using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;
using Magicka.CommunityPatch.Runtime;

internal static class TomeSupporterScenarios
{
    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            report.AddNotApplicable("tome_supporters.hook",
                "legacy executable has no Paradox popup system");
            report.AddNotApplicable("tome_supporters.hitbox",
                "legacy executable has no Paradox popup system");
            return;
        }

        bool hook = runtime || ManualHookPresent(magicka);
        report.Add("tome_supporters.hook", new ScenarioResult(hook,
            hook ? "hook:present" : "hook:missing", "hook:present"));
        if (!runtime)
        {
            report.Add("tome_supporters.hitbox", new ScenarioResult(hook,
                hook ? "hitbox:present" : "hitbox:missing", "hitbox:present"));
            return;
        }
        bool edges =
            TomeSupporterPatch.IsVersionTextHitValues(1920, 1080,
                16, 1044, 20, 200f) &&
            TomeSupporterPatch.IsVersionTextHitValues(1920, 1080,
                216, 1064, 20, 200f) &&
            !TomeSupporterPatch.IsVersionTextHitValues(1920, 1080,
                15, 1044, 20, 200f) &&
            !TomeSupporterPatch.IsVersionTextHitValues(1920, 1080,
                217, 1044, 20, 200f) &&
            !TomeSupporterPatch.IsVersionTextHitValues(1920, 1080,
                16, 1043, 20, 200f) &&
            !TomeSupporterPatch.IsVersionTextHitValues(1920, 1080,
                16, 1065, 20, 200f);
        report.Add("tome_supporters.hitbox", new ScenarioResult(edges,
            edges ? "edges:bounded" : "edges:mismatch", "edges:bounded"));
    }

    private static bool ManualHookPresent(Assembly magicka)
    {
        Type tome = magicka.GetType("Magicka.GameLogic.UI.Tome", true);
        MethodInfo method = null;
        MethodInfo[] methods = tome.GetMethods(BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
            if (methods[index].Name == "ControllerMouseAction" &&
                methods[index].GetParameters().Length == 4)
                method = methods[index];
        DynamicMethod target = new DynamicMethod("ReadTomeMouseBody",
            typeof(void), Type.EmptyTypes, typeof(TomeSupporterScenarios), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        for (int index = 0; index < decoded.Count; index++)
        {
            MethodInfo called = decoded[index].GetCodeInstruction().operand
                as MethodInfo;
            if (called != null && called.Name == "IsVersionTextHit")
                return true;
        }
        return false;
    }
}
