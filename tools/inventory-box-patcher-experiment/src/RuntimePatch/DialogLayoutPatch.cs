using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class DialogLayoutPatch
    {
        private static FieldInfo lineLengthField;
        private static FieldInfo hintHashField;

        internal static readonly RuntimePatchDefinition MessageDefinition =
            RuntimePatchDefinition.Transpile(
                "Dialog list line breaks",
                "org.magickacommunitypatch.dialog-list-line-breaks",
                FindMessageInitialize,
                typeof(DialogLayoutPatch).GetMethod("MessageTranspiler"));

        internal static readonly RuntimePatchDefinition HintDefinition =
            RuntimePatchDefinition.Transpile(
                "Element hint line breaks",
                "org.magickacommunitypatch.element-hint-line-breaks",
                FindHintInitialize,
                typeof(DialogLayoutPatch).GetMethod("HintTranspiler"));

        private static MethodInfo FindMessageInitialize(Assembly targetAssembly)
        {
            Type messageType = targetAssembly.GetType(
                "Magicka.GameLogic.UI.Message",
                true);
            lineLengthField = RequireField(messageType, "mLineLength", typeof(int));
            return RequireInitialize(messageType);
        }

        private static MethodInfo FindHintInitialize(Assembly targetAssembly)
        {
            Type hintType = targetAssembly.GetType(
                "Magicka.Levels.Triggers.Actions.SetDialogHint",
                true);
            hintHashField = RequireField(hintType, "mHintHash", typeof(int));
            return RequireInitialize(hintType);
        }

        private static FieldInfo RequireField(Type type, string name, Type fieldType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireInitialize(Type type)
        {
            MethodInfo method = type.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, "Initialize");
            return method;
        }

        public static IEnumerable<CodeInstruction> MessageTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            MethodInfo helper = typeof(DialogLayoutPatch).GetMethod(
                "RestoreDialogListBreaks");
            int wrapCall = -1;
            int helperCall = -1;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if (Calls(result[index], helper))
                    helperCall = SingleMatch(helperCall, index, "dialog helper");
                if (!IsWrap(called) || index < 5 ||
                    !LoadsOne(result[index - 1]) ||
                    result[index - 2].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index - 2].operand, lineLengthField) ||
                    result[index - 3].opcode != OpCodes.Ldarg_0 ||
                    !LoadsLocal(result[index - 4].opcode) ||
                    !LoadsLocal(result[index - 5].opcode))
                    continue;
                wrapCall = SingleMatch(wrapCall, index, "message Wrap call");
            }
            if (wrapCall < 0)
                throw new InvalidOperationException("Message Wrap call was not found.");
            if (helperCall >= 0)
            {
                if (helperCall != wrapCall - 4)
                    throw new InvalidOperationException(
                        "Dialog line-break helper has an unexpected position.");
                return result;
            }

            result.Insert(
                wrapCall - 3,
                new CodeInstruction(OpCodes.Call, helper));
            return result;
        }

        public static IEnumerable<CodeInstruction> HintTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            MethodInfo helper = typeof(DialogLayoutPatch).GetMethod(
                "RestoreElementHintBreaks");
            int getStringCall = -1;
            int helperCall = -1;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if (Calls(result[index], helper))
                    helperCall = SingleMatch(helperCall, index, "element-hint helper");
                if (!IsLanguageGetString(called) || index < 3 ||
                    !LoadsLocal(result[index - 3].opcode) ||
                    result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index - 1].operand, hintHashField) ||
                    index + 1 >= result.Count ||
                    !StoresLocal(result[index + 1].opcode))
                    continue;
                getStringCall = SingleMatch(
                    getStringCall,
                    index,
                    "hint localization lookup");
            }
            if (getStringCall < 0)
                throw new InvalidOperationException(
                    "SetDialogHint localization lookup was not found.");
            if (helperCall >= 0)
            {
                if (helperCall != getStringCall + 1)
                    throw new InvalidOperationException(
                        "Element-hint helper has an unexpected position.");
                return result;
            }

            result.Insert(
                getStringCall + 1,
                new CodeInstruction(OpCodes.Call, helper));
            return result;
        }

        private static int SingleMatch(int previous, int current, string description)
        {
            if (previous >= 0)
                throw new InvalidOperationException(
                    "Multiple " + description + " instructions matched.");
            return current;
        }

        private static bool Calls(CodeInstruction instruction, MethodInfo method)
        {
            return method != null &&
                (instruction.opcode == OpCodes.Call ||
                    instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, method);
        }

        private static bool IsWrap(MethodInfo method)
        {
            if (method == null || method.Name != "Wrap" ||
                method.DeclaringType == null ||
                method.DeclaringType.FullName != "PolygonHead.BitmapFont" ||
                method.ReturnType != typeof(string))
                return false;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 3 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(int) &&
                parameters[2].ParameterType == typeof(bool);
        }

        private static bool IsLanguageGetString(MethodInfo method)
        {
            if (method == null || method.Name != "GetString" ||
                method.DeclaringType == null ||
                method.DeclaringType.FullName !=
                    "Magicka.Localization.LanguageManager" ||
                method.ReturnType != typeof(string))
                return false;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(int);
        }

        private static bool LoadsOne(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldc_I4_1;
        }

        private static bool LoadsLocal(OpCode opcode)
        {
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }

        private static bool StoresLocal(OpCode opcode)
        {
            return opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S ||
                opcode == OpCodes.Stloc_0 || opcode == OpCodes.Stloc_1 ||
                opcode == OpCodes.Stloc_2 || opcode == OpCodes.Stloc_3;
        }

        public static string RestoreDialogListBreaks(string value)
        {
            if (String.IsNullOrEmpty(value))
                return value;

            StringBuilder builder = null;
            int copyStart = 0;
            for (int index = 0; index + 3 < value.Length; index++)
            {
                if (value[index] != '[' ||
                    (value[index + 1] != 'P' && value[index + 1] != 'p') ||
                    value[index + 2] != '=')
                    continue;

                int close = value.IndexOf(']', index + 3);
                if (close < 0)
                    break;

                int next = close + 1;
                while (next < value.Length &&
                    (value[next] == ' ' || value[next] == '\t'))
                    next++;

                if (next >= value.Length || value[next] != '-' ||
                    (next + 1 < value.Length && value[next + 1] == '-'))
                {
                    index = close;
                    continue;
                }

                if (builder == null)
                    builder = new StringBuilder(value.Length + 8);
                builder.Append(value, copyStart, close - copyStart + 1);
                builder.Append('\n');
                copyStart = next;
                index = next - 1;
            }

            if (builder == null)
                return value;
            builder.Append(value, copyStart, value.Length - copyStart);
            return builder.ToString();
        }

        public static string RestoreElementHintBreaks(string value)
        {
            if (String.IsNullOrEmpty(value) ||
                value.IndexOf("#TYPE;") < 0 ||
                value.IndexOf("#PROP;") < 0 ||
                value.IndexOf("#OPP;") < 0)
                return value;

            value = value.Replace("  #TYPE;", "\n\n#TYPE;");
            value = value.Replace("  #PROP;", "\n\n#PROP;");
            value = value.Replace("  #OPP;", "\n\n#OPP;");
            return value.Replace("  ", "\n");
        }
    }
}
