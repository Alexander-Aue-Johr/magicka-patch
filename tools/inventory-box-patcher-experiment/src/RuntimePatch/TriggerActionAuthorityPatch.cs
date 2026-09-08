using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class TriggerActionAuthorityPatch
    {
        private static FieldInfo actionTypeField;
        private static FieldInfo serverIdField;
        private static MethodInfo networkAction;
        private static HashSet<int> serverOnlyActions;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "NetworkClient TriggerAction server authority",
                "org.magickacommunitypatch.network-client-trigger-authority",
                FindReadMessage,
                typeof(TriggerActionAuthorityPatch).GetMethod("Transpiler"));

        private static MethodInfo FindReadMessage(Assembly targetAssembly)
        {
            Type clientType = targetAssembly.GetType(
                "Magicka.Network.NetworkClient",
                true);
            Type messageType = targetAssembly.GetType(
                "Magicka.Network.TriggerActionMessage",
                true);
            Type triggerType = targetAssembly.GetType(
                "Magicka.Levels.Triggers.Trigger",
                true);
            actionTypeField = RequireField(messageType, "ActionType");
            serverIdField = RequireField(clientType, "mServerID");
            networkAction = triggerType.GetMethod(
                "NetworkAction",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { messageType.MakeByRefType() },
                null);
            if (networkAction == null)
                throw new MissingMethodException(
                    triggerType.FullName,
                    "NetworkAction(TriggerActionMessage&)");

            serverOnlyActions = new HashSet<int>();
            AddServerOnlyAction("SpawnNPC");
            AddServerOnlyAction("SpawnLuggage");
            AddServerOnlyAction("SpawnElemental");
            AddServerOnlyAction("SpawnItem");
            AddServerOnlyAction("SpawnMagick");
            AddServerOnlyAction("SpawnDamageablePhysicsEntity");
            AddServerOnlyAction("SpawnGrease");
            AddServerOnlyAction("SpawnTornado");

            MethodInfo match = null;
            MethodInfo[] methods = clientType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "ReadMessage" ||
                    method.ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(System.IO.BinaryReader) ||
                    parameters[1].ParameterType != serverIdField.FieldType)
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple NetworkClient.ReadMessage methods matched.");
                match = method;
            }
            if (match == null)
                throw new MissingMethodException(clientType.FullName, "ReadMessage");
            return match;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static void AddServerOnlyAction(string name)
        {
            if (!Enum.IsDefined(actionTypeField.FieldType, name))
                return;
            object value = Enum.Parse(actionTypeField.FieldType, name);
            serverOnlyActions.Add(Convert.ToInt32(value));
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int call = -1;
            for (int index = 1; index < result.Count; index++)
            {
                if ((result[index].opcode != OpCodes.Call &&
                    result[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(result[index].operand, networkAction) ||
                    !IsLocalAddressLoad(result[index - 1]))
                    continue;
                if (call >= 0)
                    throw new InvalidOperationException(
                        "NetworkClient.ReadMessage contains multiple TriggerAction calls.");
                call = index;
            }
            if (call < 0 || call + 1 >= result.Count)
                throw new InvalidOperationException(
                    "NetworkClient TriggerAction call shape changed.");

            int start = call - 1;
            Label skip = generator.DefineLabel();
            result[call + 1].labels.Add(skip);

            CodeInstruction loadMessage = Clone(result[start]);
            loadMessage.labels.AddRange(result[start].labels);
            loadMessage.blocks.AddRange(result[start].blocks);
            result[start].labels.Clear();
            result[start].blocks.Clear();

            List<CodeInstruction> guard = new List<CodeInstruction>();
            guard.Add(loadMessage);
            guard.Add(new CodeInstruction(OpCodes.Ldfld, actionTypeField));
            guard.Add(new CodeInstruction(OpCodes.Conv_I4));
            guard.Add(new CodeInstruction(OpCodes.Ldarg_2));
            if (serverIdField.FieldType.IsValueType)
                guard.Add(new CodeInstruction(OpCodes.Box, serverIdField.FieldType));
            guard.Add(new CodeInstruction(OpCodes.Ldarg_0));
            guard.Add(new CodeInstruction(OpCodes.Ldfld, serverIdField));
            if (serverIdField.FieldType.IsValueType)
                guard.Add(new CodeInstruction(OpCodes.Box, serverIdField.FieldType));
            guard.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(TriggerActionAuthorityPatch).GetMethod("ShouldProcess")));
            guard.Add(new CodeInstruction(OpCodes.Brfalse, skip));
            result.InsertRange(start, guard);
            return result;
        }

        private static bool IsLocalAddressLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloca ||
                instruction.opcode == OpCodes.Ldloca_S;
        }

        private static CodeInstruction Clone(CodeInstruction instruction)
        {
            return new CodeInstruction(instruction.opcode, instruction.operand);
        }

        public static bool ShouldProcess(
            int actionType,
            object sender,
            object serverId)
        {
            if (Object.Equals(sender, serverId))
                return true;
            return !serverOnlyActions.Contains(actionType);
        }
    }
}
