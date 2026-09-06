using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class EntityManagerClosestDamageablePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "EntityManager detached damageable guard",
                "org.magickacommunitypatch.entity-manager-closest-damageable",
                EntityManagerTargetMethods.FindGetClosestDamageableIn,
                typeof(EntityManagerClosestDamageablePatch).GetMethod("Transpiler"));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int positionCall = FindFirstDamageablePositionCall(result);
            int skipBranch = FindPreviousConditionalBranch(result, positionCall);
            MethodInfo positionGetter = (MethodInfo)result[positionCall].operand;
            MethodInfo bodyGetter = positionGetter.DeclaringType.GetProperty("Body").GetGetMethod();
            CodeInstruction originalLoad = result[positionCall - 1];

            if (!LoadsLocal(originalLoad.opcode))
                throw new InvalidOperationException(
                    "GetClosestIDamageable no longer loads its candidate from a local.");

            CodeInstruction bodyLoad = new CodeInstruction(originalLoad.opcode, originalLoad.operand);
            bodyLoad.labels.AddRange(originalLoad.labels);
            bodyLoad.blocks.AddRange(originalLoad.blocks);
            originalLoad.labels.Clear();
            originalLoad.blocks.Clear();

            result.Insert(positionCall - 1, bodyLoad);
            result.Insert(positionCall, new CodeInstruction(OpCodes.Callvirt, bodyGetter));
            result.Insert(positionCall + 1, new CodeInstruction(
                OpCodes.Brfalse,
                result[skipBranch].operand));
            return result;
        }

        private static int FindFirstDamageablePositionCall(
            IList<CodeInstruction> instructions)
        {
            int match = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if (instructions[index].opcode == OpCodes.Callvirt &&
                    method != null &&
                    method.Name == "get_Position" &&
                    method.DeclaringType.FullName == "Magicka.GameLogic.Entities.IDamageable")
                {
                    if (match >= 0)
                        break;
                    match = index;
                }
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "GetClosestIDamageable candidate Position read was not found.");
            return match;
        }

        private static int FindPreviousConditionalBranch(
            IList<CodeInstruction> instructions,
            int beforeIndex)
        {
            for (int index = beforeIndex - 1; index >= Math.Max(0, beforeIndex - 10); index--)
            {
                if (instructions[index].opcode == OpCodes.Brtrue ||
                    instructions[index].opcode == OpCodes.Brtrue_S)
                    return index;
            }
            throw new InvalidOperationException(
                "GetClosestIDamageable candidate skip branch was not found.");
        }

        private static bool LoadsLocal(OpCode opcode)
        {
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }
    }
}
