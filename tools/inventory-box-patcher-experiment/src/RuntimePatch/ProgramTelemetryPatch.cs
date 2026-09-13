using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ProgramTelemetryPatch
    {
        internal static readonly RuntimePatchDefinition StartupDefinition =
            RuntimePatchDefinition.Prefix(
                "Startup telemetry after Steam initialization",
                "org.magickacommunitypatch.telemetry-startup",
                FindGameInitialize,
                target => typeof(ProgramTelemetryPatch).GetMethod(
                    "StartupPrefix"));

        internal static readonly RuntimePatchDefinition NormalCloseDefinition =
            RuntimePatchDefinition.Transpile(
                "Normal-close telemetry context",
                "org.magickacommunitypatch.telemetry-normal-close",
                FindMain,
                typeof(ProgramTelemetryPatch).GetMethod(
                    "NormalCloseTranspiler"));

        internal static readonly RuntimePatchDefinition CrashDefinition =
            RuntimePatchDefinition.Transpile(
                "Crash telemetry context",
                "org.magickacommunitypatch.telemetry-crash",
                FindWriteReport,
                typeof(ProgramTelemetryPatch).GetMethod(
                    "CrashTranspiler"));

        private static MethodInfo FindMain(Assembly assembly)
        {
            Type program = assembly.GetType("Magicka.Program", true);
            MethodInfo method = program.GetMethod(
                "Main",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(string[]) },
                null);
            if (method == null || method.ReturnType != typeof(int))
                throw new MissingMethodException(program.FullName, "Main");
            return method;
        }

        private static MethodInfo FindGameInitialize(Assembly assembly)
        {
            Type game = assembly.GetType("Magicka.Game", true);
            MethodInfo method = game.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(game.FullName, "Initialize");
            return method;
        }

        public static void StartupPrefix()
        {
            RuntimePatchTelemetry.SendStartup();
        }

        private static MethodInfo FindWriteReport(Assembly assembly)
        {
            Type program = assembly.GetType("Magicka.Program", true);
            MethodInfo method = program.GetMethod(
                "WriteReport",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[]
                {
                    typeof(object),
                    typeof(UnhandledExceptionEventArgs)
                },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    program.FullName,
                    "WriteReport");
            return method;
        }

        public static IEnumerable<CodeInstruction> NormalCloseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int shutdown = -1;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo method = result[index].operand as MethodInfo;
                if (result[index].opcode != OpCodes.Call || method == null ||
                    method.Name != "Shutdown" ||
                    method.DeclaringType == null ||
                    method.DeclaringType.FullName != "SteamWrapper.SteamAPI" ||
                    method.GetParameters().Length != 0)
                    continue;
                if (shutdown >= 0)
                    throw new InvalidOperationException(
                        "Multiple Steam shutdown calls matched.");
                shutdown = index;
            }
            if (shutdown < 0)
                throw new InvalidOperationException(
                    "Steam shutdown call was not found.");
            result.InsertRange(
                shutdown + 1,
                new CodeInstruction[]
                {
                    new CodeInstruction(
                        OpCodes.Call,
                        typeof(RuntimePatchTelemetry).GetMethod(
                            "SendGameClosedNormally")),
                    new CodeInstruction(
                        OpCodes.Call,
                        typeof(RuntimePatchUpdateManager).GetMethod(
                            "OfferPendingUpdateAfterGameExit"))
                });
            return result;
        }

        public static IEnumerable<CodeInstruction> CrashTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            CodeInstruction reportPathLoad = null;
            int dispose = -1;
            int debuggerCheck = -1;

            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo method = result[index].operand as MethodInfo;
                if (method == null)
                    continue;
                if (method.DeclaringType == typeof(File) &&
                    method.Name == "CreateText" && index > 0)
                    reportPathLoad = CloneLocalLoad(result[index - 1]);
                else if (method.Name == "Dispose" &&
                    method.GetParameters().Length == 0)
                    dispose = index;
                else if (method.DeclaringType == typeof(Debugger) &&
                    method.Name == "get_IsAttached")
                    debuggerCheck = index;
            }

            if (reportPathLoad == null || dispose < 0 ||
                debuggerCheck < 0 || dispose >= debuggerCheck)
                throw new InvalidOperationException(
                    "Crash-report telemetry insertion contract did not match.");

            List<CodeInstruction> addition = new List<CodeInstruction>();
            addition.Add(new CodeInstruction(OpCodes.Ldarg_1));
            addition.Add(new CodeInstruction(
                OpCodes.Callvirt,
                typeof(UnhandledExceptionEventArgs).GetProperty(
                    "ExceptionObject").GetGetMethod()));
            addition.Add(new CodeInstruction(OpCodes.Isinst, typeof(Exception)));
            addition.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(Thread).GetProperty("CurrentThread").GetGetMethod()));
            addition.Add(new CodeInstruction(
                OpCodes.Callvirt,
                typeof(Thread).GetProperty("Name").GetGetMethod()));
            addition.Add(reportPathLoad);
            addition.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(RuntimePatchTelemetry).GetMethod(
                    "SendCrashFromReportPath")));
            addition.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(RuntimePatchUpdateManager).GetMethod(
                    "OfferPendingUpdateAfterCrash")));
            result.InsertRange(dispose + 1, addition);
            return result;
        }

        private static CodeInstruction CloneLocalLoad(
            CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldloc_0 ||
                instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Ldloc_2 ||
                instruction.opcode == OpCodes.Ldloc_3 ||
                instruction.opcode == OpCodes.Ldloc ||
                instruction.opcode == OpCodes.Ldloc_S)
                return new CodeInstruction(
                    instruction.opcode,
                    instruction.operand);
            throw new InvalidOperationException(
                "Expected a local load before File.CreateText.");
        }

    }
}
