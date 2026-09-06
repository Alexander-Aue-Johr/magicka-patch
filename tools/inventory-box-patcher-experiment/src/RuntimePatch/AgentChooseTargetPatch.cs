using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AgentChooseTargetPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Agent detached target candidate guard",
                "org.magickacommunitypatch.agent-choose-target",
                AgentTargetMethods.FindChooseTargetIn,
                typeof(AgentChooseTargetPatch).GetMethod("Transpiler"));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int deadCall = FindDeadCall(result);
            int rejectionBranch = FindNextTrueBranch(result, deadCall);
            CodeInstruction candidateLoad = CloneLocalLoad(result[deadCall - 1]);
            MoveEntryMetadata(result[rejectionBranch + 1], candidateLoad);
            MethodInfo damageableGetter = (MethodInfo)result[deadCall].operand;
            MethodInfo bodyGetter = damageableGetter.DeclaringType
                .GetProperty("Body").GetGetMethod();
            object nextCandidate = result[rejectionBranch].operand;

            result.Insert(rejectionBranch + 1, candidateLoad);
            result.Insert(rejectionBranch + 2, new CodeInstruction(OpCodes.Callvirt, bodyGetter));
            result.Insert(rejectionBranch + 3, new CodeInstruction(OpCodes.Brfalse, nextCandidate));
            return result;
        }

        private static int FindDeadCall(IList<CodeInstruction> instructions)
        {
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    method != null &&
                    method.Name == "get_Dead" &&
                    method.DeclaringType.FullName ==
                        "Magicka.GameLogic.Entities.IDamageable")
                    return index;
            }
            throw new InvalidOperationException(
                "Agent.ChooseTarget candidate Dead read was not found.");
        }

        private static int FindNextTrueBranch(
            IList<CodeInstruction> instructions,
            int afterIndex)
        {
            for (int index = afterIndex + 1;
                index < Math.Min(instructions.Count, afterIndex + 10);
                index++)
            {
                if (instructions[index].opcode == OpCodes.Brtrue ||
                    instructions[index].opcode == OpCodes.Brtrue_S)
                    return index;
            }
            throw new InvalidOperationException(
                "Agent.ChooseTarget candidate rejection branch was not found.");
        }

        private static CodeInstruction CloneLocalLoad(CodeInstruction source)
        {
            if (source.opcode != OpCodes.Ldloc && source.opcode != OpCodes.Ldloc_S &&
                source.opcode != OpCodes.Ldloc_0 && source.opcode != OpCodes.Ldloc_1 &&
                source.opcode != OpCodes.Ldloc_2 && source.opcode != OpCodes.Ldloc_3)
                throw new InvalidOperationException(
                    "Agent.ChooseTarget candidate is no longer loaded from a local.");
            return new CodeInstruction(source.opcode, source.operand);
        }

        private static void MoveEntryMetadata(
            CodeInstruction source,
            CodeInstruction destination)
        {
            destination.labels.AddRange(source.labels);
            destination.blocks.AddRange(source.blocks);
            source.labels.Clear();
            source.blocks.Clear();
        }
    }
}
