using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkServerHotjoinPatch
    {
        private static MethodInfo addSyncMessage;

        internal static void ApplyTo(Assembly targetAssembly)
        {
            Type player = targetAssembly.GetType(
                "Magicka.GameLogic.Player",
                true);
            addSyncMessage = player.GetMethod(
                "AddSyncMessage",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (addSyncMessage == null)
                return;

            MethodInfo sendMessage = FindBroadcastSendMessage(targetAssembly);
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
                MethodInfo closedMethod = sendMessage.MakeGenericMethod(messageType);
                RuntimePatchSession.Apply(
                    targetAssembly,
                    CreateDefinition(closedMethod, messageType));
            }
        }

        private static RuntimePatchDefinition CreateDefinition(
            MethodInfo closedMethod,
            Type messageType)
        {
            string suffix = messageType.FullName.Replace('+', '.');
            return RuntimePatchDefinition.Transpile(
                "NetworkServer hotjoin broadcast continuation: " + suffix,
                "org.magickacommunitypatch.network-server-hotjoin-broadcast." +
                    suffix.ToLowerInvariant(),
                assembly => closedMethod,
                typeof(NetworkServerHotjoinPatch).GetMethod("Transpiler"));
        }

        private static MethodInfo FindBroadcastSendMessage(
            Assembly targetAssembly)
        {
            Type server = targetAssembly.GetType(
                "Magicka.Network.NetworkServer",
                true);
            MethodInfo match = null;
            MethodInfo[] methods = server.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "SendMessage" ||
                    !method.IsGenericMethodDefinition ||
                    method.ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    !parameters[0].ParameterType.IsByRef ||
                    !parameters[1].ParameterType.IsEnum)
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple NetworkServer broadcast SendMessage methods matched.");
                match = method;
            }
            if (match == null)
                throw new MissingMethodException(server.FullName, "SendMessage");
            return match;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int addMessage = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if ((result[index].opcode != OpCodes.Call &&
                    result[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(result[index].operand, addSyncMessage))
                    continue;
                if (addMessage >= 0)
                    throw new InvalidOperationException(
                        "NetworkServer.SendMessage contains multiple sync queue writes.");
                addMessage = index;
            }
            if (addMessage < 0 || addMessage + 1 >= result.Count ||
                result[addMessage + 1].opcode != OpCodes.Leave &&
                result[addMessage + 1].opcode != OpCodes.Leave_S)
                throw new InvalidOperationException(
                    "NetworkServer.SendMessage sync queue exit changed.");

            int increment = FindLoopIncrement(result, addMessage + 2);
            if (increment < 0)
                throw new InvalidOperationException(
                    "NetworkServer.SendMessage client loop increment changed.");

            Label continueLabel = generator.DefineLabel();
            result[increment].labels.Add(continueLabel);
            result[addMessage + 1].opcode = OpCodes.Br;
            result[addMessage + 1].operand = continueLabel;
            return result;
        }

        private static int FindLoopIncrement(
            IList<CodeInstruction> instructions,
            int start)
        {
            for (int index = start; index + 3 < instructions.Count; index++)
            {
                int loadedLocal;
                int storedLocal;
                if (!TryGetLocalIndex(instructions[index], true, out loadedLocal) ||
                    instructions[index + 1].opcode != OpCodes.Ldc_I4_1 ||
                    instructions[index + 2].opcode != OpCodes.Add ||
                    !TryGetLocalIndex(
                        instructions[index + 3],
                        false,
                        out storedLocal) ||
                    loadedLocal != storedLocal)
                    continue;
                return index;
            }
            return -1;
        }

        private static bool TryGetLocalIndex(
            CodeInstruction instruction,
            bool load,
            out int index)
        {
            index = -1;
            OpCode opcode = instruction.opcode;
            if (load && opcode == OpCodes.Ldloc_0 ||
                !load && opcode == OpCodes.Stloc_0)
                index = 0;
            else if (load && opcode == OpCodes.Ldloc_1 ||
                !load && opcode == OpCodes.Stloc_1)
                index = 1;
            else if (load && opcode == OpCodes.Ldloc_2 ||
                !load && opcode == OpCodes.Stloc_2)
                index = 2;
            else if (load && opcode == OpCodes.Ldloc_3 ||
                !load && opcode == OpCodes.Stloc_3)
                index = 3;
            else if (load && (opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S) ||
                !load && (opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S))
            {
                LocalBuilder local = instruction.operand as LocalBuilder;
                if (local != null)
                    index = local.LocalIndex;
                else if (instruction.operand is byte)
                    index = (byte)instruction.operand;
                else if (instruction.operand is int)
                    index = (int)instruction.operand;
            }
            return index >= 0;
        }
    }
}
