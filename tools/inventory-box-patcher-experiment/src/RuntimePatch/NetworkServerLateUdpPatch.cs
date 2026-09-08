using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkServerLateUdpPatch
    {
        private static FieldInfo clientsField;
        private static MethodInfo clientIndexer;

        internal static void ApplyTo(Assembly targetAssembly)
        {
            MethodInfo queue = FindQueueUdpMessage(targetAssembly);
            Type sendable = targetAssembly.GetType(
                "Magicka.Network.ISendable",
                true);
            List<Type> messageTypes = new List<Type>();
            Type[] types = targetAssembly.GetTypes();
            for (int index = 0; index < types.Length; index++)
            {
                Type type = types[index];
                if (type == sendable || type.IsAbstract ||
                    type.ContainsGenericParameters ||
                    !sendable.IsAssignableFrom(type))
                    continue;
                messageTypes.Add(type);
            }
            messageTypes.Sort(delegate(Type left, Type right)
            {
                return String.CompareOrdinal(left.FullName, right.FullName);
            });
            if (messageTypes.Count == 0)
                throw new InvalidOperationException(
                    "No concrete ISendable message types were found.");

            for (int index = 0; index < messageTypes.Count; index++)
            {
                Type messageType = messageTypes[index];
                MethodInfo closedQueue = queue.MakeGenericMethod(messageType);
                RuntimePatchSession.Apply(
                    targetAssembly,
                    CreateDefinition(closedQueue, messageType));
            }
        }

        private static RuntimePatchDefinition CreateDefinition(
            MethodInfo closedQueue,
            Type messageType)
        {
            string suffix = messageType.FullName.Replace('+', '.');
            return RuntimePatchDefinition.Transpile(
                "NetworkServer late UDP client guard: " + suffix,
                "org.magickacommunitypatch.network-server-late-udp-client." +
                    suffix.ToLowerInvariant(),
                assembly => closedQueue,
                typeof(NetworkServerLateUdpPatch).GetMethod("Transpiler"));
        }

        private static MethodInfo FindQueueUdpMessage(Assembly targetAssembly)
        {
            Type server = targetAssembly.GetType(
                "Magicka.Network.NetworkServer",
                true);
            clientsField = server.GetField(
                "mClients",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (clientsField == null || !clientsField.FieldType.IsGenericType ||
                clientsField.FieldType.GetGenericTypeDefinition() !=
                    typeof(System.Collections.Generic.List<>))
                throw new MissingFieldException(server.FullName, "mClients");
            clientIndexer = clientsField.FieldType.GetProperty("Item").GetGetMethod();
            if (clientIndexer == null)
                throw new MissingMethodException(
                    clientsField.FieldType.FullName,
                    "get_Item");

            MethodInfo match = null;
            MethodInfo[] methods = server.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "QueueUDPMessage" ||
                    !method.IsGenericMethodDefinition ||
                    method.ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(int) ||
                    !parameters[1].ParameterType.IsByRef)
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple NetworkServer.QueueUDPMessage methods matched.");
                match = method;
            }
            if (match == null)
                throw new MissingMethodException(server.FullName, "QueueUDPMessage");
            return match;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int lookup = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if ((result[index].opcode != OpCodes.Call &&
                    result[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(result[index].operand, clientIndexer))
                    continue;
                if (lookup >= 0)
                    throw new InvalidOperationException(
                        "NetworkServer.QueueUDPMessage contains multiple client lookups.");
                lookup = index;
            }
            if (lookup < 3 || result[lookup - 3].opcode != OpCodes.Ldarg_0 ||
                result[lookup - 2].opcode != OpCodes.Ldfld ||
                !Object.Equals(result[lookup - 2].operand, clientsField) ||
                result[lookup - 1].opcode != OpCodes.Ldarg_1)
                throw new InvalidOperationException(
                    "NetworkServer.QueueUDPMessage client lookup changed.");

            int methodReturn = -1;
            for (int index = result.Count - 1; index >= 0; index--)
            {
                if (result[index].opcode == OpCodes.Ret)
                {
                    methodReturn = index;
                    break;
                }
            }
            if (methodReturn < 0)
                throw new InvalidOperationException(
                    "NetworkServer.QueueUDPMessage return changed.");

            int loadStart = lookup - 3;
            Label validClient = generator.DefineLabel();
            Label methodEnd = generator.DefineLabel();
            result[methodReturn].labels.Add(methodEnd);
            List<CodeInstruction> guard = new List<CodeInstruction>();
            guard.Add(new CodeInstruction(OpCodes.Ldarg_0));
            guard.Add(new CodeInstruction(OpCodes.Ldfld, clientsField));
            guard.Add(new CodeInstruction(OpCodes.Ldarg_1));
            guard.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(NetworkServerLateUdpPatch).GetMethod("IsValidClient")));
            guard.Add(new CodeInstruction(OpCodes.Brtrue, validClient));
            guard.Add(new CodeInstruction(OpCodes.Leave, methodEnd));
            guard[0].labels.AddRange(result[loadStart].labels);
            guard[0].blocks.AddRange(result[loadStart].blocks);
            result[loadStart].labels.Clear();
            result[loadStart].blocks.Clear();
            result[loadStart].labels.Add(validClient);
            result.InsertRange(loadStart, guard);
            return result;
        }

        public static bool IsValidClient(ICollection clients, int index)
        {
            return index >= 0 && index < clients.Count;
        }
    }
}
