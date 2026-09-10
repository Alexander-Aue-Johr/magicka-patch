using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkEntityUpdateLifecyclePatch
    {
        private static MethodInfo entityNetworkUpdate;
        private static MethodInfo getFromHandle;
        private static MethodInfo resolveActiveAdapter;

        internal static readonly RuntimePatchDefinition ClientDefinition =
            Definition("NetworkClient", "client");
        internal static readonly RuntimePatchDefinition ServerDefinition =
            Definition("NetworkServer", "server");

        private static RuntimePatchDefinition Definition(string type, string side)
        {
            return RuntimePatchDefinition.Transpile(
                type + " active EntityUpdate guard",
                "org.magickacommunitypatch." + type.ToLowerInvariant() +
                    "-entity-update-lifecycle",
                assembly => FindReadMessage(assembly, type),
                typeof(NetworkEntityUpdateLifecyclePatch).GetMethod("Apply",
                    BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static MethodInfo FindReadMessage(Assembly assembly, string typeName)
        {
            Magicka.CommunityPatch.NetworkEntityHandleGuard.Initialize(assembly);
            Type networkType = assembly.GetType("Magicka.Network." + typeName, true);
            Type entity = assembly.GetType("Magicka.GameLogic.Entities.Entity", true);
            Type steamId = RuntimeMember.FindLoadedType("SteamWrapper.SteamID");
            Type message = assembly.GetType("Magicka.Network.EntityUpdateMessage", true);
            entityNetworkUpdate = entity.GetMethod("NetworkUpdate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { steamId, message.MakeByRefType() }, null);
            if (entityNetworkUpdate == null)
                throw new MissingMethodException(entity.FullName, "NetworkUpdate");
            getFromHandle = entity.GetMethod("GetFromHandle",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new Type[] { typeof(int) }, null);
            if (getFromHandle == null)
                throw new MissingMethodException(entity.FullName, "GetFromHandle");
            resolveActiveAdapter = CreateResolveAdapter(entity);
            MethodInfo[] methods = networkType.GetMethods(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "ReadMessage" &&
                    methods[index].GetParameters().Length == 2)
                    return methods[index];
            throw new MissingMethodException(networkType.FullName, "ReadMessage");
        }

        internal static IEnumerable<CodeInstruction> Apply(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int call = -1;
            for (int index = 0; index < result.Count; index++)
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(result[index].operand, entityNetworkUpdate))
                {
                    if (call >= 0)
                        throw new InvalidOperationException(
                            "Multiple Entity.NetworkUpdate calls found.");
                    call = index;
                }
            int resolve = -1;
            for (int index = call - 1; index >= Math.Max(0, call - 16); index--)
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(result[index].operand, getFromHandle))
                {
                    resolve = index;
                    break;
                }
            if (call < 0 || resolve < 0)
                throw new InvalidOperationException(
                    "Entity.NetworkUpdate call shape changed.");
            result[resolve].opcode = OpCodes.Call;
            result[resolve].operand = resolveActiveAdapter;
            return result;
        }

        public static object ResolveActiveHandle(int handle)
        {
            return Magicka.CommunityPatch.NetworkEntityHandleGuard.ResolveActive(
                handle, "network", "entity_update_unknown_handle", true);
        }

        private static MethodInfo CreateResolveAdapter(Type entity)
        {
            DynamicMethod method = new DynamicMethod(
                "ResolveActiveEntityUpdateHandle",
                entity,
                new Type[] { typeof(int) },
                typeof(NetworkEntityUpdateLifecyclePatch),
                true);
            ILGenerator generator = method.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.EmitCall(OpCodes.Call,
                typeof(NetworkEntityUpdateLifecyclePatch).GetMethod(
                    "ResolveActiveHandle"), null);
            generator.Emit(OpCodes.Castclass, entity);
            generator.Emit(OpCodes.Ret);
            return method;
        }
    }

}
