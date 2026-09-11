using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

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
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> body = new List<CodeInstruction>(instructions);
            int replacements = 0;
            int dependencyReplacements = 0;
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
                    if (candidate != null &&
                        (candidate.Name == "GetByHandle" ||
                         candidate.Name == "GetFromCache") &&
                        candidate.ReturnType != typeof(void) &&
                        candidate.GetParameters().Length == 1)
                    {
                        body[call].opcode = OpCodes.Call;
                        body[call].operand = CreateRequiredResultAdapter(
                            candidate,
                            message.Replace("Message", String.Empty)
                                .ToLowerInvariant() +
                                "_missing_owner_cache_or_hitlist");
                        dependencyReplacements++;
                        continue;
                    }
                    if (message == "SpawnPlayerMessage" && candidate != null &&
                        ((candidate.Name == "get_Gamer" &&
                          candidate.GetParameters().Length == 0) ||
                         (candidate.Name == "GetCachedTemplate" &&
                          candidate.GetParameters().Length == 1)))
                    {
                        body[call].opcode = OpCodes.Call;
                        body[call].operand = CreateRequiredResultAdapter(
                            candidate, candidate.Name == "get_Gamer"
                                ? "spawn_player_missing_gamer_for_template"
                                : "spawn_player_template_not_cached");
                        dependencyReplacements++;
                        continue;
                    }
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
            if (replacements == 0 || dependencyReplacements == 0)
                throw new InvalidOperationException(
                    "Network spawn handle/dependency sites were not found.");
            WrapRejectedPacket(body, generator);
            return body;
        }

        public static object ResolveHandle(int handle, string side,
            string reason, bool active)
        {
            object result = active
                ? Magicka.CommunityPatch.NetworkEntityHandleGuard.ResolveActive(
                    handle, side, reason, true)
                : Magicka.CommunityPatch.NetworkEntityHandleGuard.Resolve(
                    handle, side, reason, true);
            if (active && result == null)
                throw new NetworkPacketRejectedException();
            return result;
        }

        private static void WrapRejectedPacket(List<CodeInstruction> body,
            ILGenerator generator)
        {
            if (body.Count == 0) return;
            body[0].blocks.Insert(0, new ExceptionBlock(
                ExceptionBlockType.BeginExceptionBlock, null));
            Label exit = generator.DefineLabel();
            for (int index = 0; index < body.Count; index++)
            {
                if (body[index].opcode != OpCodes.Ret) continue;
                body[index].opcode = OpCodes.Leave;
                body[index].operand = exit;
            }
            CodeInstruction handler = new CodeInstruction(OpCodes.Pop);
            handler.blocks.Add(new ExceptionBlock(
                ExceptionBlockType.BeginCatchBlock,
                typeof(NetworkPacketRejectedException)));
            body.Add(handler);
            body.Add(new CodeInstruction(OpCodes.Leave, exit));
            CodeInstruction end = new CodeInstruction(OpCodes.Nop);
            end.blocks.Add(new ExceptionBlock(
                ExceptionBlockType.EndExceptionBlock, null));
            body.Add(end);
            CodeInstruction ret = new CodeInstruction(OpCodes.Ret);
            ret.labels.Add(exit);
            body.Add(ret);
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

        private static MethodInfo CreateRequiredResultAdapter(
            MethodInfo original, string reason)
        {
            ParameterInfo[] parameters = original.GetParameters();
            Type[] args = new Type[parameters.Length + (original.IsStatic ? 0 : 1)];
            int offset = 0;
            if (!original.IsStatic)
            {
                args[0] = original.DeclaringType;
                offset = 1;
            }
            for (int index = 0; index < parameters.Length; index++)
                args[index + offset] = parameters[index].ParameterType;
            DynamicMethod method = new DynamicMethod("RequireNetworkDependency",
                original.ReturnType, args, typeof(NetworkSpawnHandlePatch), true);
            ILGenerator il = method.GetILGenerator();
            for (int index = 0; index < args.Length; index++)
                il.Emit(OpCodes.Ldarg, index);
            il.EmitCall(original.IsStatic ? OpCodes.Call : OpCodes.Callvirt,
                original, null);
            Label valid = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, valid);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, currentSide);
            il.Emit(OpCodes.Ldstr, reason);
            il.EmitCall(OpCodes.Call,
                typeof(NetworkSpawnHandlePatch).GetMethod("RejectDependency"),
                null);
            il.MarkLabel(valid);
            il.Emit(OpCodes.Ret);
            return method;
        }

        public static void RejectDependency(string side, string reason)
        {
            RuntimePatchTelemetry.SendNetworkGuardDrop(side, "spawn",
                String.Empty, String.Empty, reason, String.Empty);
            throw new NetworkPacketRejectedException();
        }

        private static bool IsHandledMessage(string name)
        {
            return name == "SpawnShieldMessage" ||
                name == "SpawnBarrierMessage" || name == "SpawnWaveMessage" ||
                name == "SpawnVortexMessage" || name == "SpawnMineMessage" ||
                name == "SpawnMissileMessage" || name == "SpawnPlayerMessage" ||
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

    internal sealed class NetworkPacketRejectedException : Exception
    {
    }
}
