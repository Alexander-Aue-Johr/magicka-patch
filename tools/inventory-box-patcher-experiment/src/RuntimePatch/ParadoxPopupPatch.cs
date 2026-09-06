using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ParadoxPopupPatch
    {
        private static FieldInfo popupField;
        private static MethodInfo extraMessageGetter;
        private static MethodInfo textSetter;
        private static MethodInfo showMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Paradox popup stale extra-message cleanup",
                "org.magickacommunitypatch.paradox-popup-extra-message",
                FindTarget,
                typeof(ParadoxPopupPatch).GetMethod("Transpiler"));

        internal static bool HasSupportedPopup(Assembly targetAssembly)
        {
            Type popupUtilsType = targetAssembly.GetType(
                "Magicka.WebTools.Paradox.ParadoxPopupUtils",
                false);
            if (popupUtilsType == null)
                return false;
            FieldInfo popup = popupUtilsType.GetField(
                "sPopup",
                BindingFlags.Static | BindingFlags.NonPublic);
            return popup != null && popup.FieldType.GetProperty(
                "ExtraMessage",
                BindingFlags.Instance | BindingFlags.Public) != null;
        }

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type popupUtilsType = targetAssembly.GetType(
                "Magicka.WebTools.Paradox.ParadoxPopupUtils",
                true);
            popupField = popupUtilsType.GetField(
                "sPopup",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (popupField == null)
                throw new MissingFieldException(popupUtilsType.FullName, "sPopup");

            PropertyInfo extraMessage = popupField.FieldType.GetProperty(
                "ExtraMessage",
                BindingFlags.Instance | BindingFlags.Public);
            extraMessageGetter = extraMessage == null
                ? null
                : extraMessage.GetGetMethod();
            PropertyInfo text = extraMessageGetter == null
                ? null
                : extraMessageGetter.ReturnType.GetProperty(
                    "Text",
                    BindingFlags.Instance | BindingFlags.Public);
            textSetter = text == null ? null : text.GetSetMethod();
            showMethod = popupField.FieldType.GetMethod(
                "Show",
                BindingFlags.Instance | BindingFlags.Public);
            if (extraMessageGetter == null || textSetter == null ||
                textSetter.GetParameters().Length != 1 ||
                textSetter.GetParameters()[0].ParameterType != typeof(string) ||
                showMethod == null || showMethod.ReturnType != typeof(void))
                throw new MissingMethodException(
                    "Paradox popup display members have an unexpected shape.");

            MethodInfo method = popupUtilsType.GetMethod(
                "ShowErrorPopup",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(string), typeof(string) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    popupUtilsType.FullName,
                    "ShowErrorPopup(string, string)");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            if (CountCalls(result, extraMessageGetter) != 0)
                throw new InvalidOperationException(
                    "Paradox plain error popup already accesses ExtraMessage.");
            int showCall = FindSingleCall(result, showMethod, "Show");
            if (showCall < 1 || result[showCall - 1].opcode != OpCodes.Ldsfld ||
                !Object.Equals(result[showCall - 1].operand, popupField))
                throw new InvalidOperationException(
                    "Paradox plain error popup Show call has an unexpected shape.");

            int insertion = showCall - 1;
            CodeInstruction originalPopupLoad = result[insertion];
            CodeInstruction clearStart = new CodeInstruction(
                OpCodes.Ldsfld,
                popupField);
            clearStart.labels.AddRange(originalPopupLoad.labels);
            clearStart.blocks.AddRange(originalPopupLoad.blocks);
            originalPopupLoad.labels.Clear();
            originalPopupLoad.blocks.Clear();
            result.InsertRange(
                insertion,
                new CodeInstruction[]
                {
                    clearStart,
                    new CodeInstruction(OpCodes.Callvirt, extraMessageGetter),
                    new CodeInstruction(OpCodes.Ldstr, ""),
                    new CodeInstruction(OpCodes.Callvirt, textSetter)
                });
            return result;
        }

        private static int FindSingleCall(
            List<CodeInstruction> instructions,
            MethodInfo method,
            string name)
        {
            int match = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                if (!IsCall(instructions[index], method))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple Paradox popup " + name + " calls matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "Paradox popup " + name + " call was not found.");
            return match;
        }

        private static int CountCalls(
            List<CodeInstruction> instructions,
            MethodInfo method)
        {
            int count = 0;
            for (int index = 0; index < instructions.Count; index++)
            {
                if (IsCall(instructions[index], method))
                    count++;
            }
            return count;
        }

        private static bool IsCall(CodeInstruction instruction, MethodInfo method)
        {
            MethodInfo called = instruction.operand as MethodInfo;
            return (instruction.opcode == OpCodes.Call ||
                    instruction.opcode == OpCodes.Callvirt) &&
                (Object.Equals(called, method) ||
                    (called != null &&
                        called.Name == method.Name &&
                        called.DeclaringType.FullName ==
                            method.DeclaringType.FullName));
        }
    }
}
