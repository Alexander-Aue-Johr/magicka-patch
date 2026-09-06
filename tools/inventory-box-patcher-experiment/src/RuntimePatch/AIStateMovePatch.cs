using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AIStateMovePatch
    {
        internal static readonly RuntimePatchDefinition OnEnterDefinition =
            RuntimePatchDefinition.Transpile(
                "AI move detached target entry guard",
                "org.magickacommunitypatch.ai-move-enter-target",
                AIStateMoveTargetMethods.FindOnEnterIn,
                typeof(AIStateMovePatch).GetMethod("OnEnterTranspiler"));

        internal static readonly RuntimePatchDefinition OnExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "AI move detached target execution guard",
                "org.magickacommunitypatch.ai-move-execute-target",
                AIStateMoveTargetMethods.FindOnExecuteIn,
                typeof(AIStateMovePatch).GetMethod("OnExecuteTranspiler"));

        public static IEnumerable<CodeInstruction> OnEnterTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int positionCall = FindCall(result, "get_Position", "Magicka.GameLogic.Entities.IDamageable");
            int positionTargetCall = FindPreviousCall(
                result,
                positionCall,
                "get_CurrentTarget",
                "Magicka.AI.Agent");
            int nullCheckCall = FindPreviousCall(
                result,
                positionTargetCall,
                "get_CurrentTarget",
                "Magicka.AI.Agent");
            int nullBranch = nullCheckCall + 1;
            if (result[nullBranch].opcode != OpCodes.Brfalse &&
                result[nullBranch].opcode != OpCodes.Brfalse_S)
                throw new InvalidOperationException(
                    "AIStateMove.OnEnter target null branch was not found.");

            CodeInstruction agentLoad = CloneLocalLoad(result[nullCheckCall - 1]);
            MoveEntryMetadata(result[nullBranch + 1], agentLoad);
            MethodInfo currentTargetGetter = (MethodInfo)result[nullCheckCall].operand;
            MethodInfo bodyGetter = currentTargetGetter.ReturnType
                .GetProperty("Body").GetGetMethod();
            object skipTargetOffset = result[nullBranch].operand;

            result.Insert(nullBranch + 1, agentLoad);
            result.Insert(nullBranch + 2, new CodeInstruction(
                result[nullCheckCall].opcode,
                currentTargetGetter));
            result.Insert(nullBranch + 3, new CodeInstruction(OpCodes.Callvirt, bodyGetter));
            result.Insert(nullBranch + 4, new CodeInstruction(OpCodes.Brfalse, skipTargetOffset));
            return result;
        }

        public static IEnumerable<CodeInstruction> OnExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int positionCall = FindCall(result, "get_Position", "Magicka.GameLogic.Entities.IDamageable");
            int deadCall = FindPreviousCall(
                result,
                positionCall,
                "get_Dead",
                "Magicka.GameLogic.Entities.IDamageable");
            int currentTargetCall = deadCall - 1;
            MethodInfo currentTargetGetter = result[currentTargetCall].operand as MethodInfo;
            if (currentTargetGetter == null || currentTargetGetter.Name != "get_CurrentTarget")
                throw new InvalidOperationException(
                    "AIStateMove.OnExecute target Dead read has an unexpected source.");
            if (result[deadCall + 1].opcode != OpCodes.Brtrue &&
                result[deadCall + 1].opcode != OpCodes.Brtrue_S)
                throw new InvalidOperationException(
                    "AIStateMove.OnExecute target rejection branch was not found.");

            CodeInstruction agentLoad = CloneLocalLoad(result[currentTargetCall - 1]);
            MoveEntryMetadata(result[deadCall + 2], agentLoad);
            OpCode currentTargetOpcode = result[currentTargetCall].opcode;
            MethodInfo bodyGetter = currentTargetGetter.ReturnType
                .GetProperty("Body").GetGetMethod();
            object rejectTarget = result[deadCall + 1].operand;

            result.Insert(deadCall + 2, agentLoad);
            result.Insert(deadCall + 3, new CodeInstruction(
                currentTargetOpcode,
                currentTargetGetter));
            result.Insert(deadCall + 4, new CodeInstruction(OpCodes.Callvirt, bodyGetter));
            result.Insert(deadCall + 5, new CodeInstruction(OpCodes.Brfalse, rejectTarget));
            return result;
        }

        private static int FindCall(
            IList<CodeInstruction> instructions,
            string methodName,
            string declaringType)
        {
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    method != null &&
                    method.Name == methodName &&
                    method.DeclaringType.FullName == declaringType)
                    return index;
            }
            throw new InvalidOperationException(
                "AIStateMove " + declaringType + "." + methodName + " call was not found.");
        }

        private static int FindPreviousCall(
            IList<CodeInstruction> instructions,
            int beforeIndex,
            string methodName,
            string declaringType)
        {
            for (int index = beforeIndex - 1; index >= 0; index--)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    method != null &&
                    method.Name == methodName &&
                    method.DeclaringType.FullName == declaringType)
                    return index;
            }
            throw new InvalidOperationException(
                "AIStateMove preceding " + declaringType + "." + methodName +
                " call was not found.");
        }

        private static CodeInstruction CloneLocalLoad(CodeInstruction source)
        {
            if (source.opcode != OpCodes.Ldloc && source.opcode != OpCodes.Ldloc_S &&
                source.opcode != OpCodes.Ldloc_0 && source.opcode != OpCodes.Ldloc_1 &&
                source.opcode != OpCodes.Ldloc_2 && source.opcode != OpCodes.Ldloc_3)
                throw new InvalidOperationException(
                    "AIStateMove target owner is no longer loaded from a local.");
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
