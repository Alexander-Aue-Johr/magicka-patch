using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MissingLevelFilePatch
    {
        public const int ExitCode = 1;
        private const string Title = "Missing level file";

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Missing level hash file handling",
                "org.magickacommunitypatch.missing-level-file",
                FindComputeHashes,
                typeof(MissingLevelFilePatch).GetMethod("Transpiler"));

        private static MethodInfo FindComputeHashes(Assembly targetAssembly)
        {
            Type managerType = targetAssembly.GetType(
                "Magicka.Levels.Campaign.LevelManager",
                true);
            MethodInfo method = managerType.GetMethod(
                "ComputeHashes",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(managerType.FullName, "ComputeHashes");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            DynamicMethod wrapper = null;
            MethodInfo wrappedMethod = null;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode != OpCodes.Call &&
                        result[index].opcode != OpCodes.Callvirt) ||
                    called == null || called.DeclaringType == null ||
                    called.DeclaringType.FullName !=
                        "Magicka.Levels.Campaign.LevelNode" ||
                    called.Name != "ComputeHashSums" ||
                    called.ReturnType != typeof(void))
                    continue;
                ParameterInfo[] parameters = called.GetParameters();
                if (parameters.Length != 1 ||
                    parameters[0].ParameterType.FullName !=
                        "System.Security.Cryptography.SHA256")
                    continue;
                if (wrapper == null)
                {
                    wrappedMethod = called;
                    wrapper = BuildWrapper(called);
                }
                else if (!Object.Equals(called, wrappedMethod))
                {
                    throw new InvalidOperationException(
                        "Multiple LevelNode.ComputeHashSums methods matched.");
                }
                result[index].opcode = OpCodes.Call;
                result[index].operand = wrapper;
                replacements++;
            }
            if (replacements == 0)
                throw new InvalidOperationException(
                    "No LevelNode.ComputeHashSums call was found.");
            return result;
        }

        public static string ClassifyException(Exception exception)
        {
            return exception is FileNotFoundException
                ? "missing_file"
                : "other";
        }

        public static string BuildMessage(Exception exception)
        {
            FileNotFoundException missing = exception as FileNotFoundException;
            string fileName = missing == null ? String.Empty : missing.FileName;
            return "Magicka could not load this required level file:\n\n" +
                fileName +
                "\n\nRestore the file or verify the game files. " +
                "Modded installations must provide every referenced level file.";
        }

        public static void HandleMissingLevelFile(FileNotFoundException exception)
        {
            ShowMessage(BuildMessage(exception));
            Environment.Exit(ExitCode);
        }

        private static DynamicMethod BuildWrapper(MethodInfo method)
        {
            DynamicMethod wrapper = new DynamicMethod(
                "LevelManager_ComputeHashSumsWithMissingFileHandling",
                typeof(void),
                new Type[]
                {
                    method.DeclaringType,
                    method.GetParameters()[0].ParameterType
                },
                typeof(MissingLevelFilePatch).Module,
                true);
            ILGenerator il = wrapper.GetILGenerator();
            Label done = il.DefineLabel();
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, method);
            il.Emit(OpCodes.Leave_S, done);
            il.BeginCatchBlock(typeof(FileNotFoundException));
            il.Emit(
                OpCodes.Call,
                typeof(MissingLevelFilePatch).GetMethod(
                    "HandleMissingLevelFile"));
            il.Emit(OpCodes.Leave_S, done);
            il.EndExceptionBlock();
            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }

        private static void ShowMessage(string message)
        {
            try
            {
                Type messageBoxType = Type.GetType(
                    "System.Windows.Forms.MessageBox, System.Windows.Forms",
                    false);
                Type buttonsType = Type.GetType(
                    "System.Windows.Forms.MessageBoxButtons, System.Windows.Forms",
                    false);
                Type iconType = Type.GetType(
                    "System.Windows.Forms.MessageBoxIcon, System.Windows.Forms",
                    false);
                if (messageBoxType == null || buttonsType == null ||
                    iconType == null)
                    return;
                MethodInfo show = messageBoxType.GetMethod(
                    "Show",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new Type[]
                    {
                        typeof(string),
                        typeof(string),
                        buttonsType,
                        iconType
                    },
                    null);
                if (show == null)
                    return;
                show.Invoke(
                    null,
                    new object[]
                    {
                        message,
                        Title,
                        Enum.Parse(buttonsType, "OK"),
                        Enum.Parse(iconType, "Hand")
                    });
            }
            catch
            {
            }
        }
    }
}
