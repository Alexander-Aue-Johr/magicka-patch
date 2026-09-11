using System;
using System.Reflection;

internal static class ModernControllerSchemeScenarios
{
    internal static void Run(Assembly magicka, bool runtimePatchEnabled,
        BehaviorReport report)
    {
        bool manual = magicka.GetType(
            "Magicka.CommunityPatch.Magicka2ControllerSupport", false) != null;
        Type runtime = typeof(Magicka.CommunityPatch.Runtime
            .ModernControllerSchemePatch);
        string[] buttons = new string[] { "A", "B", "X", "Y" };
        string[] normal = new string[] { "Fire", "Earth", "Lightning", "Arcane" };
        string[] modified = new string[] { "Cold", "Shield", "Water", "Life" };
        bool mappings = true;
        for (int index = 0; index < buttons.Length; index++)
        {
            mappings &= (string)runtime.GetMethod("MappedElement").Invoke(
                null, new object[] { buttons[index], false }) == normal[index];
            mappings &= (string)runtime.GetMethod("MappedElement").Invoke(
                null, new object[] { buttons[index], true }) == modified[index];
        }
        bool available = manual || runtimePatchEnabled;
        MethodInfo action = runtime.GetMethod("ShouldInvokeAction");
        bool actionRelease =
            (bool)action.Invoke(null, new object[] { false, true, false }) &&
            !(bool)action.Invoke(null, new object[] { false, true, true }) &&
            !(bool)action.Invoke(null, new object[] { true, true, false });
        report.Add("controller.modern_scheme_available", new ScenarioResult(
            available && (!runtimePatchEnabled || (mappings && actionRelease)),
            "available:" + available + ",mappings:" + mappings +
                ",action_release:" + actionRelease,
            "available:True,mappings:True,action_release:True"));
    }
}
