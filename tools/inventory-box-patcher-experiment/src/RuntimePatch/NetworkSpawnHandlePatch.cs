using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkSpawnHandlePatch
    {
        private static MethodInfo getFromHandle;
        private static Type entityType;
        private static string currentSide;

        internal static readonly RuntimePatchDefinition ClientDefinition =
            Definition("NetworkClient", "client");
        internal static readonly RuntimePatchDefinition ServerDefinition =
            Definition("NetworkServer", "server");

        private static RuntimePatchDefinition Definition(string typeName,
            string side)
        {
            return RuntimePatchDefinition.Transpile(
                typeName + " spawn handle lifecycle",
                "org.magickacommunitypatch." + typeName.ToLowerInvariant() +
                    "-spawn-handle-lifecycle",
                assembly => FindReadMessage(assembly, typeName, side),
                typeof(NetworkSpawnHandlePatch).GetMethod("Transpiler"));
        }

        private static MethodInfo FindReadMessage(Assembly assembly,
            string typeName, string side)
        {
            Magicka.CommunityPatch.NetworkEntityHandleGuard.Initialize(assembly);
            entityType = assembly.GetType("Magicka.GameLogic.Entities.Entity", true);
            getFromHandle = entityType.GetMethod("GetFromHandle",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { typeof(int) }, null);
            currentSide = side;
            Type network = assembly.GetType("Magicka.Network." + typeName, true);
            MethodInfo[] methods = network.GetMethods(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "ReadMessage" &&
                    methods[index].GetParameters().Length == 2)
                    return methods[index];
            throw new MissingMethodException(network.FullName, "ReadMessage");
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> body = new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 0; index < body.Count; index++)
            {
                MethodInfo read = body[index].operand as MethodInfo;
                if (read == null || read.Name != "Read" ||
                    read.DeclaringType == null)
                    continue;
                string message = read.DeclaringType.Name;
                if (!IsHandledMessage(message))
                    continue;
                int end = FindNextMessageRead(body, index + 1);
                for (int call = index + 1; call < end; call++)
                {
                    MethodInfo candidate = body[call].operand as MethodInfo;
                    if (!CallsGetFromHandle(candidate))
                        continue;
                    bool active = RequiresActiveHandle(message, body, call);
                    string reason = BuildReason(message, active, body, call);
                    body[call].opcode = OpCodes.Call;
                    body[call].operand = CreateAdapter(active, reason);
                    replacements++;
                }
                index = end - 1;
            }
            if (replacements == 0)
                throw new InvalidOperationException(
                    "No network spawn handle sites were found.");
            return body;
        }

        public static object ResolveHandle(int handle, string side,
            string reason, bool active)
        {
            return active
                ? Magicka.CommunityPatch.NetworkEntityHandleGuard.ResolveActive(
                    handle, side, reason, true)
                : Magicka.CommunityPatch.NetworkEntityHandleGuard.Resolve(
                    handle, side, reason, true);
        }

        private static MethodInfo CreateAdapter(bool active, string reason)
        {
            DynamicMethod method = new DynamicMethod("ResolveSpawnHandle",
                entityType, new Type[] { typeof(int) },
                typeof(NetworkSpawnHandlePatch), true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, currentSide);
            il.Emit(OpCodes.Ldstr, reason);
            il.Emit(active ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.EmitCall(OpCodes.Call,
                typeof(NetworkSpawnHandlePatch).GetMethod("ResolveHandle"), null);
            il.Emit(OpCodes.Castclass, entityType);
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static bool IsHandledMessage(string name)
        {
            return name == "SpawnShieldMessage" ||
                name == "SpawnBarrierMessage" || name == "SpawnWaveMessage" ||
                name == "SpawnVortexMessage" || name == "SpawnMineMessage" ||
                name == "SpawnMissileMessage" ||
                name == "SpawnShieldRequestMessage" ||
                name == "SpawnBarrierRequestMessage" ||
                name == "SpawnWaveRequestMessage" ||
                name == "SpawnMineRequestMessage" ||
                name == "EntityRemoveMessage" || name == "CharacterDieMessage";
        }

        private static bool RequiresActiveHandle(string message,
            IList<CodeInstruction> body, int call)
        {
            if (message.EndsWith("RequestMessage", StringComparison.Ordinal) ||
                message == "SpawnVortexMessage" ||
                message == "EntityRemoveMessage" ||
                message == "CharacterDieMessage")
                return true;
            string field = FindLoadedField(body, call);
            return field != "Handle" && field != "Item";
        }

        private static string BuildReason(string message, bool active,
            IList<CodeInstruction> body, int call)
        {
            string packet = message.Replace("RequestMessage", String.Empty)
                .Replace("Message", String.Empty).ToLowerInvariant();
            string field = FindLoadedField(body, call).ToLowerInvariant();
            if (field.Length == 0)
                field = active ? "owner" : "entity";
            return packet + "_missing_or_unusable_" + field;
        }

        private static string FindLoadedField(IList<CodeInstruction> body,
            int call)
        {
            for (int index = call - 1; index >= Math.Max(0, call - 6); index--)
            {
                FieldInfo field = body[index].operand as FieldInfo;
                if (field != null)
                    return field.Name;
            }
            return String.Empty;
        }

        private static int FindNextMessageRead(IList<CodeInstruction> body,
            int start)
        {
            for (int index = start; index < body.Count; index++)
            {
                MethodInfo method = body[index].operand as MethodInfo;
                if (method != null && method.Name == "Read" &&
                    method.DeclaringType != null &&
                    method.DeclaringType.Namespace == "Magicka.Network")
                    return index;
            }
            return body.Count;
        }

        private static bool CallsGetFromHandle(MethodInfo candidate)
        {
            return candidate != null && getFromHandle != null &&
                candidate.Name == getFromHandle.Name &&
                candidate.GetParameters().Length == 1;
        }
    }
}
