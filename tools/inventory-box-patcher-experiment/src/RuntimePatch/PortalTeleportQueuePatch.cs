using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class PortalTeleportQueuePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Portal detached teleport entry guard",
                "org.magickacommunitypatch.portal-teleport-queue",
                PortalTargetMethods.FindPortalEntityUpdateIn,
                typeof(PortalTeleportQueuePatch).GetMethod("Transpiler"));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int dequeueCall = FindMethodCall(result, "Dequeue", "System.Collections.Generic.Queue`1");
            int bodyCall = FindMethodCallAfter(
                result,
                dequeueCall,
                "get_Body",
                "Magicka.GameLogic.Entities.Entity");
            object loopCondition = FindLoopConditionTarget(result, dequeueCall);
            MethodInfo bodyGetter = (MethodInfo)result[bodyCall].operand;
            CodeInstruction originalLoad = result[bodyCall - 1];

            if (!LoadsLocal(originalLoad.opcode))
                throw new InvalidOperationException(
                    "PortalEntity.Update no longer loads its dequeued entity from a local.");

            CodeInstruction nullLoad = CloneLocalLoad(originalLoad);
            nullLoad.labels.AddRange(originalLoad.labels);
            nullLoad.blocks.AddRange(originalLoad.blocks);
            originalLoad.labels.Clear();
            originalLoad.blocks.Clear();

            result.Insert(bodyCall - 1, nullLoad);
            result.Insert(bodyCall, new CodeInstruction(OpCodes.Brfalse, loopCondition));
            result.Insert(bodyCall + 1, CloneLocalLoad(originalLoad));
            result.Insert(bodyCall + 2, new CodeInstruction(OpCodes.Callvirt, bodyGetter));
            result.Insert(bodyCall + 3, new CodeInstruction(OpCodes.Brfalse, loopCondition));
            return result;
        }

        private static CodeInstruction CloneLocalLoad(CodeInstruction source)
        {
            return new CodeInstruction(source.opcode, source.operand);
        }

        private static int FindMethodCall(
            IList<CodeInstruction> instructions,
            string methodName,
            string declaringTypePrefix)
        {
            return FindMethodCallAfter(instructions, -1, methodName, declaringTypePrefix);
        }

        private static int FindMethodCallAfter(
            IList<CodeInstruction> instructions,
            int afterIndex,
            string methodName,
            string declaringTypePrefix)
        {
            for (int index = afterIndex + 1; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    method != null &&
                    method.Name == methodName &&
                    method.DeclaringType.FullName.StartsWith(
                        declaringTypePrefix,
                        StringComparison.Ordinal))
                    return index;
            }
            throw new InvalidOperationException(
                "PortalEntity.Update " + methodName + " call was not found.");
        }

        private static object FindLoopConditionTarget(
            IList<CodeInstruction> instructions,
            int dequeueCall)
        {
            for (int index = dequeueCall - 1; index >= 0; index--)
            {
                if ((instructions[index].opcode != OpCodes.Br &&
                    instructions[index].opcode != OpCodes.Br_S) ||
                    instructions[index].operand == null)
                    continue;

                object target = instructions[index].operand;
                for (int candidate = dequeueCall + 1; candidate < instructions.Count; candidate++)
                {
                    if (instructions[candidate].labels.Contains((Label)target))
                        return target;
                }
            }
            throw new InvalidOperationException(
                "PortalEntity.Update queue condition branch was not found.");
        }

        private static bool LoadsLocal(OpCode opcode)
        {
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }
    }
}
