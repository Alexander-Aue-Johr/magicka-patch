using System;
using System.Reflection;

internal static class CharacterSelectOfflineLeaveScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The last-offline-player leave path is a current-version patch.";
            report.AddNotApplicable(
                "character_select_offline_leave.contract", reason);
            report.AddNotApplicable(
                "character_select_offline_leave.priority_control", reason);
            return;
        }
        Type owner = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
            true);
        MethodInfo controllerB = FindMethod(owner, "ControllerB", 1);
        MethodInfo manualHelper = owner.GetMethod(
            "CommunityPatchTryLeaveSingleOfflinePlayer",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        bool protectedPath = runtimePatchEnabled || manualHelper != null;
        report.Add(
            "character_select_offline_leave.contract",
            new ScenarioResult(
                protectedPath,
                protectedPath ? "last_player_leave:handled" :
                    "last_player_leave:original_tail",
                "last_player_leave:handled"));
        report.Add(
            "character_select_offline_leave.priority_control",
            new ScenarioResult(
                controllerB != null,
                controllerB == null ? "missing" : "original_handler:present",
                "original_handler:present"));
    }

    private static MethodInfo FindMethod(Type owner, string name, int parameters)
    {
        MethodInfo[] methods = owner.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
            if (methods[index].Name == name &&
                methods[index].GetParameters().Length == parameters)
                return methods[index];
        return null;
    }
}
