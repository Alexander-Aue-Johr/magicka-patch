using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MachineNetworkInitializePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Machine network initialization",
                "org.magickacommunitypatch.machine-network-initialize",
                MachineTargetMethods.FindNetworkInitializeIn,
                typeof(MachineNetworkInitializePatch).GetMethod("Transpiler"));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int assignmentIndex = FindNetworkInitializedAssignment(result);
            CodeInstruction valueInstruction = result[assignmentIndex - 1];
            FieldInfo networkInitializedField = (FieldInfo)result[assignmentIndex].operand;
            FieldInfo warlockField = networkInitializedField.DeclaringType.GetField(
                "mWarlock",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (warlockField == null)
            {
                throw new MissingFieldException(
                    networkInitializedField.DeclaringType.FullName,
                    "mWarlock");
            }
            if (valueInstruction.opcode != OpCodes.Ldc_I4_1)
            {
                throw new InvalidOperationException(
                    "Machine.NetworkInitialize no longer assigns a literal true.");
            }

            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            loadThis.labels.AddRange(valueInstruction.labels);
            loadThis.blocks.AddRange(valueInstruction.blocks);
            result[assignmentIndex - 1] = loadThis;
            result.Insert(assignmentIndex, new CodeInstruction(OpCodes.Ldfld, warlockField));
            result.Insert(assignmentIndex + 1, new CodeInstruction(OpCodes.Ldnull));
            result.Insert(assignmentIndex + 2, new CodeInstruction(OpCodes.Cgt_Un));
            return result;
        }

        private static int FindNetworkInitializedAssignment(
            IList<CodeInstruction> instructions)
        {
            int match = -1;
            for (int index = 1; index < instructions.Count; index++)
            {
                FieldInfo field = instructions[index].operand as FieldInfo;
                if (instructions[index].opcode == OpCodes.Stfld &&
                    field != null &&
                    field.Name == "mNetworkInitialized")
                {
                    if (match >= 0)
                    {
                        throw new InvalidOperationException(
                            "Machine.NetworkInitialize has more than one status assignment.");
                    }
                    match = index;
                }
            }

            if (match < 0)
            {
                throw new InvalidOperationException(
                    "Machine.NetworkInitialize status assignment was not found.");
            }
            return match;
        }
    }
}
