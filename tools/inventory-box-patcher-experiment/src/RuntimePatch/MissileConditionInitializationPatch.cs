using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MissileConditionInitializationPatch
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static FieldInfo conditionCollectionField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Missile initialized condition collection",
                "org.magickacommunitypatch.missile-initial-condition-collection",
                FindTarget,
                typeof(MissileConditionInitializationPatch).GetMethod(
                    "Transpiler"));

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type missile = assembly.GetType(
                "Magicka.GameLogic.Entities.MissileEntity", true);
            conditionCollectionField = missile.GetField(
                "mConditionCollection", Instance);
            if (conditionCollectionField == null)
                throw new MissingFieldException(
                    missile.FullName, "mConditionCollection");
            Type conditions = conditionCollectionField.FieldType;
            MethodInfo result = null;
            MethodInfo[] methods = missile.GetMethods(Instance);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "Initialize" && parameters.Length == 7 &&
                    parameters[5].ParameterType == conditions)
                {
                    if (result != null)
                        throw new AmbiguousMatchException(
                            missile.FullName + ".Initialize");
                    result = method;
                }
            }
            if (result == null)
                throw new MissingMemberException(
                    missile.FullName, "condition initialization contract");
            return result;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacement = -1;
            for (int index = 4; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if (called == null || called.Name != "ExecuteAll" ||
                    called.DeclaringType != conditionCollectionField.FieldType ||
                    called.GetParameters().Length != 3)
                    continue;
                int receiver = index - 4;
                if (!LoadsArgument(result[receiver], 6))
                    continue;
                if (replacement >= 0)
                    throw new InvalidOperationException(
                        "Multiple missile default-condition calls matched.");
                replacement = receiver;
            }
            if (replacement < 0)
                throw new InvalidOperationException(
                    "Missile default-condition call was not found.");
            CodeInstruction original = result[replacement];
            CodeInstruction loadThis = new CodeInstruction(OpCodes.Ldarg_0);
            loadThis.labels.AddRange(original.labels);
            original.labels.Clear();
            result[replacement] = loadThis;
            result.Insert(
                replacement + 1,
                new CodeInstruction(OpCodes.Ldfld, conditionCollectionField));
            return result;
        }

        private static bool LoadsArgument(CodeInstruction instruction, int argument)
        {
            if (argument == 0 && instruction.opcode == OpCodes.Ldarg_0)
                return true;
            if (argument == 1 && instruction.opcode == OpCodes.Ldarg_1)
                return true;
            if (argument == 2 && instruction.opcode == OpCodes.Ldarg_2)
                return true;
            if (argument == 3 && instruction.opcode == OpCodes.Ldarg_3)
                return true;
            if (instruction.opcode != OpCodes.Ldarg &&
                instruction.opcode != OpCodes.Ldarg_S)
                return false;
            ParameterInfo parameter = instruction.operand as ParameterInfo;
            if (parameter != null)
                return parameter.Position + 1 == argument;
            return Convert.ToInt32(instruction.operand) == argument;
        }
    }
}
