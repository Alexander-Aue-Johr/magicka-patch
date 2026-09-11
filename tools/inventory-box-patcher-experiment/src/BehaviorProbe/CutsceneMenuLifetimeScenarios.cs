using System;
using System.Reflection;

internal static class CutsceneMenuLifetimeScenarios
{
    internal static void Run(Assembly magicka, bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type type = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCutscene", true);
        bool manual = type.GetMethod("Dispose",
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null;
        bool available = manual || runtimePatchEnabled;
        report.Add("cutscene_menu.scoped_content_lifetime", new ScenarioResult(
            available,
            "scoped:" + available,
            "scoped:True"));
    }
}
