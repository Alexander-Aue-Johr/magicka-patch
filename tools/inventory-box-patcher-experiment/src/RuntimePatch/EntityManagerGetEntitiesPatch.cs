using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class EntityManagerGetEntitiesPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "EntityManager detached spatial entry guard",
                "org.magickacommunitypatch.entity-manager-get-entities",
                EntityManagerTargetMethods.FindGetEntitiesIn,
                typeof(EntityManagerGetEntitiesPatch).GetMethod("Transpiler"));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int deadCall = FindEntityGetter(result, "get_Dead", 0);
            int deadBranch = FindNextTrueBranch(result, deadCall);
            object skipTarget = result[deadBranch].operand;

            InsertNullCheck(result, deadCall - 1, skipTarget);

            int positionCall = FindEntityGetter(result, "get_Position", deadCall + 2);
            MethodInfo positionGetter = (MethodInfo)result[positionCall].operand;
            MethodInfo bodyGetter = positionGetter.DeclaringType.GetProperty("Body").GetGetMethod();
            InsertBodyCheck(result, positionCall - 1, bodyGetter, skipTarget);
            return result;
        }

        private static int FindEntityGetter(
            IList<CodeInstruction> instructions,
            string methodName,
            int startIndex)
        {
            for (int index = startIndex; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if (instructions[index].opcode == OpCodes.Callvirt &&
                    method != null &&
                    method.Name == methodName &&
                    method.DeclaringType.FullName == "Magicka.GameLogic.Entities.Entity")
                    return index;
            }
            throw new InvalidOperationException(
                "EntityManager.GetEntities " + methodName + " call was not found.");
        }

        private static int FindNextTrueBranch(
            IList<CodeInstruction> instructions,
            int afterIndex)
        {
            for (int index = afterIndex + 1; index <= Math.Min(instructions.Count - 1, afterIndex + 3); index++)
            {
                if (instructions[index].opcode == OpCodes.Brtrue ||
                    instructions[index].opcode == OpCodes.Brtrue_S)
                    return index;
            }
            throw new InvalidOperationException(
                "EntityManager.GetEntities dead-entry branch was not found.");
        }

        private static void InsertNullCheck(
            IList<CodeInstruction> instructions,
            int loadIndex,
            object skipTarget)
        {
            CodeInstruction originalLoad = instructions[loadIndex];
            RequireLocalLoad(originalLoad);
            CodeInstruction nullLoad = CopyAndMoveLabels(originalLoad);
            instructions.Insert(loadIndex, nullLoad);
            instructions.Insert(loadIndex + 1, new CodeInstruction(OpCodes.Brfalse, skipTarget));
        }

        private static void InsertBodyCheck(
            IList<CodeInstruction> instructions,
            int loadIndex,
            MethodInfo bodyGetter,
            object skipTarget)
        {
            CodeInstruction originalLoad = instructions[loadIndex];
            RequireLocalLoad(originalLoad);
            CodeInstruction bodyLoad = CopyAndMoveLabels(originalLoad);
            instructions.Insert(loadIndex, bodyLoad);
            instructions.Insert(loadIndex + 1, new CodeInstruction(OpCodes.Callvirt, bodyGetter));
            instructions.Insert(loadIndex + 2, new CodeInstruction(OpCodes.Brfalse, skipTarget));
        }

        private static CodeInstruction CopyAndMoveLabels(CodeInstruction original)
        {
            CodeInstruction copy = new CodeInstruction(original.opcode, original.operand);
            copy.labels.AddRange(original.labels);
            copy.blocks.AddRange(original.blocks);
            original.labels.Clear();
            original.blocks.Clear();
            return copy;
        }

        private static void RequireLocalLoad(CodeInstruction instruction)
        {
            OpCode opcode = instruction.opcode;
            if (opcode != OpCodes.Ldloc && opcode != OpCodes.Ldloc_S &&
                opcode != OpCodes.Ldloc_0 && opcode != OpCodes.Ldloc_1 &&
                opcode != OpCodes.Ldloc_2 && opcode != OpCodes.Ldloc_3)
                throw new InvalidOperationException(
                    "EntityManager.GetEntities no longer loads its entry from a local.");
        }
    }
}
