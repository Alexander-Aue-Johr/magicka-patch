using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkServerEnterSyncPatch
    {
        private static FieldInfo clientsField;
        private static FieldInfo syncPointsField;
        private static FieldInfo syncPointIdField;
        private static MethodInfo clientIndexer;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "NetworkServer EnterSync client guard",
                "org.magickacommunitypatch.network-server-enter-sync-client",
                FindReadMessage,
                typeof(NetworkServerEnterSyncPatch).GetMethod("Transpiler"));

        private static MethodInfo FindReadMessage(Assembly targetAssembly)
        {
            Type serverType = targetAssembly.GetType(
                "Magicka.Network.NetworkServer",
                true);
            Type messageType = targetAssembly.GetType(
                "Magicka.Network.EnterSyncMessage",
                true);
            clientsField = serverType.GetField(
                "mClients",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (clientsField == null || !clientsField.FieldType.IsGenericType ||
                clientsField.FieldType.GetGenericTypeDefinition() !=
                    typeof(List<>))
                throw new MissingFieldException(serverType.FullName, "mClients");
            Type connectionType = clientsField.FieldType.GetGenericArguments()[0];
            syncPointsField = connectionType.GetField(
                "SyncPoints",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            syncPointIdField = messageType.GetField(
                "ID",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            clientIndexer = clientsField.FieldType.GetProperty("Item").GetGetMethod();
            if (syncPointsField == null)
                throw new MissingFieldException(connectionType.FullName, "SyncPoints");
            if (syncPointIdField == null ||
                syncPointIdField.FieldType != typeof(uint))
                throw new MissingFieldException(messageType.FullName, "ID");
            if (clientIndexer == null)
                throw new MissingMethodException(
                    clientsField.FieldType.FullName,
                    "get_Item");

            MethodInfo[] methods = serverType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            MethodInfo match = null;
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "ReadMessage" ||
                    method.ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(System.IO.BinaryReader))
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple NetworkServer.ReadMessage methods matched.");
                match = method;
            }
            if (match == null)
                throw new MissingMethodException(serverType.FullName, "ReadMessage");
            return match;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int syncPoints = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index].operand, syncPointsField))
                    continue;
                if (syncPoints >= 0)
                    throw new InvalidOperationException(
                        "NetworkServer.ReadMessage contains multiple EnterSync stores.");
                syncPoints = index;
            }
            int start = syncPoints - 4;
            int add = syncPoints + 3;
            if (start < 0 || add >= result.Count ||
                result[start].opcode != OpCodes.Ldarg_0 ||
                result[start + 1].opcode != OpCodes.Ldfld ||
                !Object.Equals(result[start + 1].operand, clientsField) ||
                !IsLocalLoad(result[start + 2]) ||
                (result[start + 3].opcode != OpCodes.Call &&
                    result[start + 3].opcode != OpCodes.Callvirt) ||
                !Object.Equals(result[start + 3].operand, clientIndexer) ||
                !IsLocalAddressLoad(result[syncPoints + 1]) ||
                result[syncPoints + 2].opcode != OpCodes.Ldfld ||
                !Object.Equals(result[syncPoints + 2].operand, syncPointIdField) ||
                !IsListAdd(result[add]))
                throw new InvalidOperationException(
                    "NetworkServer EnterSync store shape changed.");

            List<CodeInstruction> replacement = new List<CodeInstruction>();
            replacement.Add(Clone(result[start]));
            replacement.Add(Clone(result[start + 1]));
            replacement.Add(Clone(result[start + 2]));
            replacement.Add(Clone(result[syncPoints + 1]));
            replacement.Add(Clone(result[syncPoints + 2]));
            replacement.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(NetworkServerEnterSyncPatch).GetMethod("TryAddSyncPoint")));
            for (int index = start; index <= add; index++)
            {
                replacement[0].labels.AddRange(result[index].labels);
                replacement[0].blocks.AddRange(result[index].blocks);
            }
            result.RemoveRange(start, add - start + 1);
            result.InsertRange(start, replacement);
            return result;
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

        private static bool IsLocalAddressLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloca ||
                instruction.opcode == OpCodes.Ldloca_S;
        }

        private static bool IsListAdd(CodeInstruction instruction)
        {
            MethodInfo method = instruction.operand as MethodInfo;
            return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
                method != null && method.Name == "Add" &&
                method.DeclaringType == syncPointsField.FieldType;
        }

        private static CodeInstruction Clone(CodeInstruction instruction)
        {
            return new CodeInstruction(instruction.opcode, instruction.operand);
        }

        public static void TryAddSyncPoint(IList clients, int index, uint syncPoint)
        {
            if (clients == null || index < 0 || index >= clients.Count)
                return;
            object connection = clients[index];
            IList syncPoints = syncPointsField.GetValue(connection) as IList;
            if (syncPoints != null)
                syncPoints.Add(syncPoint);
        }
    }
}
