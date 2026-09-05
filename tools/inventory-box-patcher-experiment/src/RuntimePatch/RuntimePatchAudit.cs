using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class RuntimePatchAudit
    {
        internal const string FileName = "magicka-runtime-patch-audit.txt";
        private static List<string> run;

        internal static void BeginRun(Assembly targetAssembly)
        {
            run = Header("PASS", targetAssembly);
            WriteRun();
        }

        internal static void WriteSuccess(
            Assembly targetAssembly,
            MethodInfo targetMethod,
            RuntimePatchDefinition definition)
        {
            if (run == null)
                BeginRun(targetAssembly);
            run.Add("patch_begin=" + definition.Name);
            run.Add("target=" + targetMethod.DeclaringType.FullName + "." + targetMethod.Name);
            run.Add("harmony_owner=" + definition.HarmonyOwner);
            run.Add("patch_kind=" + definition.Kind);
            run.Add("registered_patches=1");
            run.Add("patch_end=" + definition.Name);
            WriteRun();
        }

        internal static void WriteFailure(Assembly targetAssembly, Exception exception)
        {
            try
            {
                List<string> lines = Header("FAIL", targetAssembly);
                lines.Add("exception=" + exception.GetType().FullName);
                lines.Add("message=" + exception.Message);
                File.WriteAllLines(AuditPath(), lines.ToArray());
            }
            catch
            {
            }
        }

        internal static void WriteNotApplicable(
            RuntimePatchDefinition definition,
            string reason)
        {
            run.Add("patch_begin=" + definition.Name);
            run.Add("status=NOT_APPLICABLE");
            run.Add("reason=" + reason);
            run.Add("patch_end=" + definition.Name);
            WriteRun();
        }

        private static List<string> Header(string result, Assembly targetAssembly)
        {
            return new List<string>
            {
                "result=" + result,
                "target_assembly=" + (targetAssembly == null ? "<null>" : targetAssembly.FullName),
                "runtime=" + (targetAssembly == null ? "<null>" : targetAssembly.ImageRuntimeVersion)
            };
        }

        private static string AuditPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        }

        private static void WriteRun()
        {
            File.WriteAllLines(AuditPath(), run.ToArray());
        }
    }
}
