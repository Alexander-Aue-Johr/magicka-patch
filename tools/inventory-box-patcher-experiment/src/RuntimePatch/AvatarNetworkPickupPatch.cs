using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AvatarNetworkPickupPatch
    {
        private static Type pickableType;
        private static PropertyInfo bodyProperty;
        private static PropertyInfo disposedProperty;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Avatar late network pickup guard",
                "org.magickacommunitypatch.avatar-late-network-pickup",
                FindNetworkAction,
                typeof(AvatarNetworkPickupPatch).GetMethod("Transpiler"));

        private static MethodInfo FindNetworkAction(Assembly targetAssembly)
        {
            Type avatarType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Avatar",
                true);
            Type messageType = targetAssembly.GetType(
                "Magicka.Network.CharacterActionMessage",
                true);
            pickableType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Items.Pickable",
                true);
            bodyProperty = pickableType.GetProperty(
                "Body",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            disposedProperty = pickableType.GetProperty(
                "IsDisposed",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (bodyProperty == null || bodyProperty.GetGetMethod(true) == null)
                throw new MissingMemberException(pickableType.FullName, "Body");
            if (disposedProperty != null &&
                (disposedProperty.PropertyType != typeof(bool) ||
                disposedProperty.GetGetMethod(true) == null))
                throw new MissingMemberException(
                    pickableType.FullName,
                    "IsDisposed");

            MethodInfo method = avatarType.GetMethod(
                "NetworkAction",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { messageType.MakeByRefType() },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(avatarType.FullName, "NetworkAction");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            List<int> calls = new List<int>();
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo method = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    method != null && method.DeclaringType != null &&
                    method.DeclaringType.FullName ==
                        "Magicka.GameLogic.Entities.Avatar" &&
                    (method.Name == "InternalPickUp" || method.Name == "PickUp") &&
                    HasPickableParameter(method))
                    calls.Add(index);
            }
            if (calls.Count != 2)
                throw new InvalidOperationException(
                    "Expected two Avatar.NetworkAction pickup calls, found " +
                    calls.Count + ".");

            for (int callIndex = calls.Count - 1; callIndex >= 0; callIndex--)
                InsertGuard(result, calls[callIndex], generator);
            return result;
        }

        private static bool HasPickableParameter(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == pickableType;
        }

        private static void InsertGuard(
            List<CodeInstruction> instructions,
            int call,
            ILGenerator generator)
        {
            int start = call - 2;
            if (start < 0 || instructions[start].opcode != OpCodes.Ldarg_0 ||
                !IsLocalLoad(instructions[call - 1]) || call + 1 >= instructions.Count)
                throw new InvalidOperationException(
                    "Avatar.NetworkAction pickup call shape changed.");

            Label skip = generator.DefineLabel();
            instructions[call + 1].labels.Add(skip);
            CodeInstruction load = Clone(instructions[call - 1]);
            CodeInstruction test = new CodeInstruction(
                OpCodes.Call,
                typeof(AvatarNetworkPickupPatch).GetMethod("IsUsable"));
            CodeInstruction branch = new CodeInstruction(OpCodes.Brfalse, skip);
            load.labels.AddRange(instructions[start].labels);
            load.blocks.AddRange(instructions[start].blocks);
            instructions[start].labels.Clear();
            instructions[start].blocks.Clear();
            instructions.InsertRange(
                start,
                new CodeInstruction[] { load, test, branch });
        }

        private static bool IsLocalLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloc ||
                instruction.opcode == OpCodes.Ldloc_S ||
                instruction.opcode == OpCodes.Ldloc_0 ||
                instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Ldloc_2 ||
                instruction.opcode == OpCodes.Ldloc_3;
        }

        private static CodeInstruction Clone(CodeInstruction instruction)
        {
            return new CodeInstruction(instruction.opcode, instruction.operand);
        }

        public static bool IsUsable(object pickable)
        {
            if (pickable == null)
                return false;
            if (disposedProperty != null &&
                (bool)disposedProperty.GetValue(pickable, null))
                return false;
            return bodyProperty.GetValue(pickable, null) != null;
        }
    }
}
