using System;
using System.Reflection;
using Magicka.CommunityPatch.Runtime;

internal static class RuntimePatchUpdateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtime,
        BehaviorReport report)
    {
        Type type = runtime ? typeof(RuntimePatchUpdateManager) :
            magicka.GetType("Magicka.CommunityPatch.PatchUpdateManager", false);
        if (type == null)
        {
            report.Add("patch_update.asset_parse", Missing());
            report.Add("patch_update.version_compare", Missing());
            return;
        }

        MethodInfo find = Require(type, "FindFilesOnlyAssetUrl", 2);
        object[] arguments = new object[]
        {
            "{\"assets\":[{\"name\":\"notes.txt\",\"browser_download_url\":\"x\"}," +
            "{\"name\":\"magicka-community-patch-0.0.61-files-only.zip\"," +
            "\"browser_download_url\":\"https:\\/\\/example.invalid\\/patch.zip\"}]}",
            String.Empty
        };
        string url = (string)find.Invoke(null, arguments);
        bool parsed = (string)arguments[1] == "0.0.61" &&
            url == "https://example.invalid/patch.zip";
        report.Add("patch_update.asset_parse", new ScenarioResult(
            parsed, "version:" + arguments[1] + ",url:" + url,
            "version:0.0.61,url:https://example.invalid/patch.zip"));

        MethodInfo newer = Require(type, "IsNewerVersion", 2);
        bool decisions =
            (bool)newer.Invoke(null, new object[] { "v0.0.61", "0.0.60" }) &&
            !(bool)newer.Invoke(null, new object[] { "0.0.60", "0.0.60" }) &&
            !(bool)newer.Invoke(null, new object[] { "0.0.59", "0.0.60" });
        report.Add("patch_update.version_compare", new ScenarioResult(
            decisions, decisions ? "newer:true,equal:false,older:false" :
                "version_decision:mismatch",
            "newer:true,equal:false,older:false"));
    }

    private static MethodInfo Require(Type type, string name, int parameterCount)
    {
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int index = 0; index < methods.Length; index++)
            if (methods[index].Name == name &&
                methods[index].GetParameters().Length == parameterCount)
                return methods[index];
        throw new MissingMethodException(type.FullName, name);
    }

    private static ScenarioResult Missing()
    {
        return new ScenarioResult(false, "update_manager:missing",
            "update_manager:available");
    }
}
