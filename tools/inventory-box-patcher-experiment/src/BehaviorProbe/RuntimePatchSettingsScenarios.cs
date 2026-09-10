using System;
using System.IO;
using System.Reflection;
using Magicka.CommunityPatch.Runtime;

internal static class RuntimePatchSettingsScenarios
{
    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        Type type = runtime ? typeof(RuntimePatchSettings) :
            magicka.GetType("Magicka.CommunityPatch.PatchSettings", false);
        bool available = type != null;
        ScenarioResult presence = new ScenarioResult(
            available,
            available ? "settings:available" : "settings:missing",
            "settings:available");
        report.Add("patch_settings.available", presence);
        if (!runtime)
        {
            report.Add("patch_settings.defaults", presence);
            report.Add("patch_settings.round_trip", presence);
            return;
        }

        string directory = Path.Combine(Path.GetTempPath(),
            "magicka-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "patch-settings.ini");
        try
        {
            RuntimePatchSettings defaults = RuntimePatchSettings.LoadFrom(path);
            bool defaultPass = defaults.UsageSharing && defaults.CrashReports &&
                defaults.CheckForUpdates && !defaults.AutoUpdate &&
                !defaults.UseMagicka1ControllerScheme &&
                defaults.Version == RuntimePatchMetadata.Version;
            report.Add("patch_settings.defaults", new ScenarioResult(
                defaultPass, Describe(defaults),
                "usage:true,crash:true,updates:true,auto:false,controller1:false"));

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "[MagickaCommunityPatch]\n" +
                "usage_sharing=no\ncrash_reports=0\n" +
                "check_for_updates=false\nauto_update=yes\n" +
                "use_magicka_1_controller_scheme=1\nlanguage=en\n" +
                "unknown=ignored\n");
            RuntimePatchSettings loaded = RuntimePatchSettings.LoadFrom(path);
            loaded.Language = "de\r\nignored";
            loaded.SaveTo(path);
            RuntimePatchSettings saved = RuntimePatchSettings.LoadFrom(path);
            bool roundTrip = !loaded.UsageSharing && !loaded.CrashReports &&
                !loaded.CheckForUpdates && loaded.AutoUpdate &&
                loaded.UseMagicka1ControllerScheme &&
                saved.Language == "deignored" &&
                File.ReadAllText(path).IndexOf("unknown=ignored") < 0;
            report.Add("patch_settings.round_trip", new ScenarioResult(
                roundTrip, Describe(saved) + ",language:" + saved.Language,
                "usage:false,crash:false,updates:false,auto:true," +
                    "controller1:true,language:deignored"));
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch { }
        }
    }

    private static string Describe(RuntimePatchSettings value)
    {
        return "usage:" + value.UsageSharing.ToString().ToLowerInvariant() +
            ",crash:" + value.CrashReports.ToString().ToLowerInvariant() +
            ",updates:" + value.CheckForUpdates.ToString().ToLowerInvariant() +
            ",auto:" + value.AutoUpdate.ToString().ToLowerInvariant() +
            ",controller1:" + value.UseMagicka1ControllerScheme
                .ToString().ToLowerInvariant();
    }
}
