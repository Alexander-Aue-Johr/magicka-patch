using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Magicka.InventoryBoxRuntimePatch
{
    internal static class RuntimePatchAudit
    {
        internal const string FileName = "inventory-box-runtime-audit.txt";
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
            run.Add("registered_transpilers=1");
            run.Add("transpiler_calls=" + PatchObservation.TranspilerCalls);
            run.Add("instruction_count_before=" + PatchObservation.InstructionCountBefore);
            run.Add("instruction_count_after=" + PatchObservation.InstructionCountAfter);
            run.Add("inserted_opcodes=" + PatchObservation.InsertedOpcodes);
            run.Add("csharp_context_diff_begin");
            run.AddRange(PatchObservation.CSharpDiff.Split('\n'));
            run.Add("csharp_context_diff_end");
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
