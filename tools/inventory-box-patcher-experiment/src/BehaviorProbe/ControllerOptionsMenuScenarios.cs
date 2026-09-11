using System;
using System.Reflection;

internal static class ControllerOptionsMenuScenarios
{
    internal static void Run(Assembly magicka, bool runtimePatchEnabled,
        BehaviorReport report)
    {
        bool manual = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuOptionsControls",
            false) != null;
        MethodInfo translate = typeof(Magicka.CommunityPatch.Runtime
            .ControllerOptionsMenuPatch).GetMethod("TranslateIndex");
        bool indices = (int)translate.Invoke(null, new object[] { 0 }) == 0 &&
            (int)translate.Invoke(null, new object[] { 1 }) == 1 &&
            (int)translate.Invoke(null, new object[] { 2 }) == 1 &&
            (int)translate.Invoke(null, new object[] { 4 }) == 3;
        bool applicable = magicka.GetName().Version >= new Version(1, 10, 0, 0);
        bool available = manual || (runtimePatchEnabled && applicable);
        report.Add("controller.options_menu_available", new ScenarioResult(
            available == applicable && (!runtimePatchEnabled || !applicable || indices),
            "available:" + available + ",indices:" + indices,
            "available:" + applicable + ",indices:True"));
    }
}
