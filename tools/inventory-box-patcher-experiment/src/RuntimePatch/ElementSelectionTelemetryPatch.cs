using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ElementSelectionTelemetryPatch
    {
        internal static readonly RuntimePatchDefinition KeyboardDefinition =
            RuntimePatchDefinition.Transpile(
                "Keyboard element selection telemetry",
                "org.magickacommunitypatch.keyboard-element-selection-telemetry",
                assembly => FindMethod(assembly, "KeyboardMouseController", "Update"),
                typeof(ElementSelectionTelemetryPatch).GetMethod(
                    "KeyboardTranspiler"));

        internal static readonly RuntimePatchDefinition DirectInputDefinition =
            RuntimePatchDefinition.Transpile(
                "DirectInput element selection telemetry",
                "org.magickacommunitypatch.directinput-element-selection-telemetry",
                assembly => FindMethod(assembly, "DirectInputController", "RightStickUpdate"),
                typeof(ElementSelectionTelemetryPatch).GetMethod(
                    "ControllerTranspiler"));

        internal static readonly RuntimePatchDefinition XInputDefinition =
            RuntimePatchDefinition.Transpile(
                "XInput element selection telemetry",
                "org.magickacommunitypatch.xinput-element-selection-telemetry",
                assembly => FindMethod(assembly, "XInputController", "RightStickUpdate"),
                typeof(ElementSelectionTelemetryPatch).GetMethod(
                    "ControllerTranspiler"));

        private static MethodInfo FindMethod(
            Assembly assembly,
            string typeName,
            string methodName)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.Controls." + typeName,
                true);
            MethodInfo match = null;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name != methodName)
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple " + typeName + "." + methodName +
                        " methods matched.");
                match = methods[index];
            }
            if (match == null)
                throw new MissingMethodException(type.FullName, methodName);
            return match;
        }

        public static IEnumerable<CodeInstruction> KeyboardTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int matches = 0;
            for (int index = result.Count - 1; index >= 0; index--)
            {
                MethodInfo call = result[index].operand as MethodInfo;
                if (!IsCall(result[index]) || call == null ||
                    call.Name != "Cooldown")
                    continue;
                result.Insert(
                    index + 1,
                    new CodeInstruction(
                        OpCodes.Call,
                        typeof(RuntimePatchTelemetry).GetMethod(
                            "RecordKeyboardElementSelection")));
                matches++;
            }
            if (matches != 8)
                throw new InvalidOperationException(
                    "Expected eight keyboard element cooldown paths, found " +
                    matches + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> ControllerTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int second = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo call = result[index].operand as MethodInfo;
                if (!IsCall(result[index]) || call == null ||
                    call.Name != "HandleCombo")
                    continue;
                matches++;
                if (matches == 2)
                    second = index;
            }
            if (matches != 2 || second < 0)
                throw new InvalidOperationException(
                    "Expected exactly two controller HandleCombo calls, found " +
                    matches + ".");
            result.Insert(
                second + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(RuntimePatchTelemetry).GetMethod(
                        "RecordControllerElementSelection")));
            return result;
        }

        private static bool IsCall(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt;
        }
    }
}
