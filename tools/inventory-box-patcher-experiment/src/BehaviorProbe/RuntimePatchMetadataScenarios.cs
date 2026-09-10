using System;
using System.Reflection;
using Magicka.CommunityPatch.Runtime;

internal static class RuntimePatchMetadataScenarios
{
    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        Type type = runtime ? typeof(RuntimePatchMetadata) :
            magicka.GetType("Magicka.CommunityPatch.CommunityPatchInfo", false);
        if (type == null)
        {
            report.Add("patch_metadata.contract", new ScenarioResult(false,
                "metadata:missing", "metadata:complete"));
            return;
        }
        string name = Read(type, "Name");
        string version = Read(type, "Version");
        string author = Read(type, "Author");
        string display = Read(type, "DisplayName");
        string agent = Read(type, "TelemetryUserAgent");
        string api = Read(type, "LatestReleaseApiUrl");
        string page = Read(type, "LatestReleasePageUrl");
        string patreon = Read(type, "PatreonUrl");
        string tool = Read(type, "ToolFileName");
        PropertyInfo supportersProperty = type.GetProperty(
            "PatreonSupporters", BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic);
        string[] supporters = supportersProperty == null ? null :
            (string[])supportersProperty.GetValue(null, null);
        bool passed = name == "Community Patch" && version == "0.0.60" &&
            author == "Alexander Aue-Johr" &&
            display == "Community Patch 0.0.60 by Alexander Aue-Johr" &&
            agent == "MagickaPatchTelemetry/0.0.60" &&
            api.EndsWith("/releases/latest") && page.EndsWith("/releases/latest") &&
            patreon.StartsWith("https://www.patreon.com/") &&
            tool == "MagickaPatchTool.exe" && supporters != null &&
            supporters.Length == 5;
        report.Add("patch_metadata.contract", new ScenarioResult(passed,
            passed ? "metadata:complete" : "metadata:mismatch",
            "metadata:complete"));
    }

    private static string Read(Type type, string name)
    {
        PropertyInfo property = type.GetProperty(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null)
            return (string)property.GetValue(null, null);
        FieldInfo field = type.GetField(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return field == null ? null : (string)field.GetValue(null);
    }
}
