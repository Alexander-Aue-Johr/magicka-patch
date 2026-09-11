using System;
using System.Reflection;

internal static class HybridInputScenarios
{
    internal static void Run(Assembly magicka, bool runtimePatchEnabled,
        BehaviorReport report)
    {
        bool manual = magicka.GetType(
            "Magicka.CommunityPatch.HybridInputSupport", false) != null;
        bool available = manual || runtimePatchEnabled;
        Type helper = typeof(Magicka.CommunityPatch.Runtime.HybridInputPatch);
        MethodInfo placement = helper.GetMethod("GetIconPlacement");
        MethodInfo label = helper.GetMethod("HudLabel");
        bool mapping = true;
        int[] expected = new int[] { 3, 0, 2, 1, 0, 2, 1, 3 };
        for (int index = 0; index < 8; index++)
        {
            object[] args = new object[] { index, 0, 0f, 0f };
            placement.Invoke(null, args);
            mapping &= (int)args[1] == expected[index];
            mapping &= label.Invoke(null, new object[] { index, false }) != null;
            mapping &= label.Invoke(null, new object[] { index, true }) != null;
        }
        report.Add("controller.hybrid_hud_available", new ScenarioResult(
            available && (!runtimePatchEnabled || mapping),
            "available:" + available + ",mapping:" + mapping,
            "available:True,mapping:True"));
    }
}
