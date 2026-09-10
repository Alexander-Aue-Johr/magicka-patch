using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Magicka.CommunityPatch.Runtime;

internal static class OriginalBackupAuditScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type audit = runtimePatchEnabled
            ? typeof(RuntimeOriginalBackupAudit)
            : magicka.GetType(
                "Magicka.CommunityPatch.OriginalBackupAudit",
                false);
        report.Add(
            "original_backup_audit.available",
            new ScenarioResult(
                audit != null,
                audit == null ? "audit:missing" : "audit:available",
                "audit:available"));

        if (!runtimePatchEnabled)
        {
            report.Add(
                "original_backup_audit.missing",
                new ScenarioResult(
                    audit != null,
                    audit == null ? "audit:missing" : "audit:available",
                    "audit:available"));
            report.Add(
                "original_backup_audit.unverified",
                new ScenarioResult(
                    audit != null,
                    audit == null ? "audit:missing" : "audit:available",
                    "audit:available"));
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            "magicka-backup-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Dictionary<string, string> values = Audit(directory);
            bool missing = values["original_magicka_backup_status"] == "missing" &&
                values["original_polygonhead_backup_status"] == "missing" &&
                values["original_backup_status"] == "none_verified" &&
                values["original_backup_audit_schema"] == "1" &&
                values["original_backup_catalog"] == "steam_build_4143032";
            report.Add(
                "original_backup_audit.missing",
                new ScenarioResult(
                    missing,
                    Describe(values),
                    "magicka:missing,polygonhead:missing,combined:none_verified"));

            File.WriteAllText(Path.Combine(directory, "Magicka.original.bak"), "x");
            File.WriteAllText(Path.Combine(directory, "PolygonHead.backup.dll"), "x");
            values = Audit(directory);
            bool unverified =
                values["original_magicka_backup_status"] == "unverified" &&
                values["original_polygonhead_backup_status"] == "unverified" &&
                values["original_backup_status"] == "none_verified";
            report.Add(
                "original_backup_audit.unverified",
                new ScenarioResult(
                    unverified,
                    Describe(values),
                    "magicka:unverified,polygonhead:unverified,combined:none_verified"));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
            }
        }
    }

    private static Dictionary<string, string> Audit(string directory)
    {
        Dictionary<string, string> values = new Dictionary<string, string>();
        RuntimeOriginalBackupAudit.AddTelemetryProperties(values, directory);
        return values;
    }

    private static string Describe(Dictionary<string, string> values)
    {
        return "magicka:" + values["original_magicka_backup_status"] +
            ",polygonhead:" + values["original_polygonhead_backup_status"] +
            ",combined:" + values["original_backup_status"];
    }
}
