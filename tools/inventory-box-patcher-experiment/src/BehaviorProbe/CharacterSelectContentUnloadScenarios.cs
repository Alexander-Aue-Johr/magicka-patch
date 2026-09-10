using System;
using System.Reflection;
using Harmony;

internal static class CharacterSelectContentUnloadScenarios
{
    private const string Owner =
        "org.magickacommunitypatch.character-select-content-unload";

    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason = "legacy executable predates current Tome pipeline";
            report.AddNotApplicable("character_select_unload.render_lifetime", reason);
            report.AddNotApplicable("character_select_unload.adjacent_transition", reason);
            report.AddNotApplicable("character_select_unload.unrelated_menu", reason);
            return;
        }

        Type menu = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect", true);
        MethodInfo onUnload = menu.GetMethod("OnUnload",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
        Type tome = magicka.GetType("Magicka.GameLogic.UI.Tome", true);
        bool manual = tome.GetMethod("IsMenuReferencedByRenderPipeline",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.Public | BindingFlags.DeclaredOnly) != null;
        bool installed = runtime && HasPrefix(onUnload);
        bool lifetime = manual || installed;
        report.Add("character_select_unload.render_lifetime",
            new ScenarioResult(lifetime,
                lifetime ? "guarded" : "immediate", "guarded"));

        if (!runtime)
        {
            report.Add("character_select_unload.adjacent_transition",
                new ScenarioResult(manual,
                    manual ? "retained" : "released", "retained"));
            report.Add("character_select_unload.unrelated_menu",
                new ScenarioResult(true, "released", "released"));
            return;
        }

        object menuObject = new object();
        Array stack = new object[] { menuObject, new object(), new object() };
        bool adjacent = Magicka.CommunityPatch.Runtime
            .CharacterSelectContentUnloadPatch.IsStackReference(
                stack, 1, menuObject, true);
        bool unrelated = !Magicka.CommunityPatch.Runtime
            .CharacterSelectContentUnloadPatch.IsStackReference(
                stack, 1, new object(), true);
        report.Add("character_select_unload.adjacent_transition",
            new ScenarioResult(adjacent,
                adjacent ? "retained" : "released", "retained"));
        report.Add("character_select_unload.unrelated_menu",
            new ScenarioResult(unrelated,
                unrelated ? "released" : "retained", "released"));
    }

    private static bool HasPrefix(MethodInfo method)
    {
        Patches patches = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-character-unload")
            .GetPatchInfo(method);
        if (patches == null) return false;
        foreach (Patch prefix in patches.Prefixes)
            if (prefix.owner == Owner) return true;
        return false;
    }
}
