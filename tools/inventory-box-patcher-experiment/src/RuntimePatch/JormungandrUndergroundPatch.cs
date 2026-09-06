using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class JormungandrUndergroundPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Jormungandr missing underground target guard",
                "org.magickacommunitypatch.jormungandr-underground-target",
                JormungandrTargetMethods.FindUndergroundUpdateIn,
                typeof(JormungandrUndergroundPatch).GetMethod("Transpiler"));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int selectTargetCall = FindSelectTargetCall(result);
            FieldInfo targetField = FindTargetField(result, selectTargetCall);
            CodeInstruction continuation = result[selectTargetCall + 1];
            Label continueLabel = generator.DefineLabel();
            continuation.labels.Add(continueLabel);

            result.Insert(selectTargetCall + 1, new CodeInstruction(OpCodes.Ldarg_2));
            result.Insert(selectTargetCall + 2, new CodeInstruction(OpCodes.Ldfld, targetField));
            result.Insert(selectTargetCall + 3, new CodeInstruction(OpCodes.Brtrue, continueLabel));
            result.Insert(selectTargetCall + 4, new CodeInstruction(OpCodes.Ret));
            return result;
        }

        private static int FindSelectTargetCall(IList<CodeInstruction> instructions)
        {
            int match = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    method != null &&
                    method.Name == "SelectTarget" &&
                    method.DeclaringType.FullName ==
                        "Magicka.GameLogic.Entities.Bosses.Jormungandr")
                {
                    if (match >= 0)
                        throw new InvalidOperationException(
                            "Jormungandr UndergroundState.OnUpdate contains multiple SelectTarget calls.");
                    match = index;
                }
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "Jormungandr UndergroundState.OnUpdate SelectTarget call was not found.");
            return match;
        }

        private static FieldInfo FindTargetField(
            IList<CodeInstruction> instructions,
            int afterIndex)
        {
            for (int index = afterIndex + 1; index < instructions.Count; index++)
            {
                FieldInfo field = instructions[index].operand as FieldInfo;
                if (instructions[index].opcode == OpCodes.Ldfld &&
                    field != null &&
                    field.Name == "mTarget" &&
                    field.DeclaringType.FullName ==
                        "Magicka.GameLogic.Entities.Bosses.Jormungandr")
                    return field;
            }
            throw new InvalidOperationException(
                "Jormungandr UndergroundState.OnUpdate target field read was not found.");
        }
    }
}
