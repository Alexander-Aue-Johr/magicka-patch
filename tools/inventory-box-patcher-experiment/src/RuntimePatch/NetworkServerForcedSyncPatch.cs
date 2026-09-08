using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkServerForcedSyncPatch
    {
        private static FieldInfo requestHandleField;
        private static FieldInfo gameSingletonField;
        private static FieldInfo gamePlayersField;
        private static FieldInfo responseCountField;
        private static FieldInfo responseUpdatesField;
        private static FieldInfo updateHandleField;
        private static PropertyInfo playerIdProperty;
        private static PropertyInfo playerAvatarProperty;
        private static PropertyInfo playerGamerProperty;
        private static PropertyInfo networkGamerClientProperty;
        private static MethodInfo getFromHandle;
        private static MethodInfo resolverAdapter;
        private static MethodInfo getNetworkUpdate;
        private static MethodInfo sendMessage;
        private static MethodInfo sync;
        private static Type networkGamerType;
        private static Type responseType;
        private static Type updateType;
        private static object serverState;
        private static object reliableSend;

        internal static readonly RuntimePatchDefinition RequestDefinition =
            RuntimePatchDefinition.Transpile(
                "NetworkServer forced sync player and sender resolution",
                "org.magickacommunitypatch.network-server-forced-sync-request",
                FindReadMessage,
                typeof(NetworkServerForcedSyncPatch).GetMethod(
                    "RequestTranspiler"));

        internal static readonly RuntimePatchDefinition BuilderDefinition =
            RuntimePatchDefinition.Prefix(
                "NetworkServer forced sync response builder",
                "org.magickacommunitypatch.network-server-forced-sync-response",
                FindBuilder,
                CreateBuilderPrefix);

        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (targetAssembly.GetType(
                "Magicka.Network.RequestForcedPlayerStatusSync",
                false) == null)
                return;
            RuntimePatchSession.Apply(targetAssembly, RequestDefinition);
            RuntimePatchSession.Apply(targetAssembly, BuilderDefinition);
        }

        private static MethodInfo FindReadMessage(Assembly targetAssembly)
        {
            Type serverType;
            Type playerType;
            ConfigureCommon(targetAssembly, out serverType, out playerType);
            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type requestType = targetAssembly.GetType(
                "Magicka.Network.RequestForcedPlayerStatusSync",
                true);
            requestHandleField = RequireField(requestType, "Handle");
            getFromHandle = entityType.GetMethod(
                "GetFromHandle",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { typeof(int) },
                null);
            if (getFromHandle == null)
                throw new MissingMethodException(entityType.FullName, "GetFromHandle");

            MethodInfo readMessage = FindMethod(
                serverType,
                "ReadMessage",
                typeof(System.IO.BinaryReader),
                null);
            Type senderType = readMessage.GetParameters()[1].ParameterType;
            resolverAdapter = BuildResolverAdapter(entityType, senderType);
            return readMessage;
        }

        private static MethodInfo FindBuilder(Assembly targetAssembly)
        {
            Type serverType;
            Type playerType;
            ConfigureCommon(targetAssembly, out serverType, out playerType);
            responseType = targetAssembly.GetType(
                "Magicka.Network.ForceSyncPlayerStatusesMessage",
                true);
            updateType = targetAssembly.GetType(
                "Magicka.Network.EntityUpdateMessage",
                true);
            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            responseCountField = RequireField(responseType, "numPlayers");
            responseUpdatesField = RequireField(
                responseType,
                "playerUpdateMessages");
            updateHandleField = RequireField(updateType, "Handle");
            getNetworkUpdate = entityType.GetMethod(
                "GetNetworkUpdate",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[]
                {
                    updateType.MakeByRefType(),
                    targetAssembly.GetType("Magicka.Network.NetworkState", true),
                    typeof(float)
                },
                null);
            if (getNetworkUpdate == null)
                throw new MissingMethodException(
                    entityType.FullName,
                    "GetNetworkUpdate");
            Type networkState = getNetworkUpdate.GetParameters()[1].ParameterType;
            serverState = Enum.Parse(networkState, "Server");

            MethodInfo builder = FindMethod(
                serverType,
                "SendForcedSyncMessageToClient",
                typeof(int),
                typeof(bool));
            sendMessage = FindIndexedSendMessage(serverType)
                .MakeGenericMethod(responseType);
            Type sendType = sendMessage.GetParameters()[2].ParameterType;
            reliableSend = Enum.Parse(sendType, "Reliable");
            sync = serverType.GetMethod(
                "Sync",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (sync == null)
                throw new MissingMethodException(serverType.FullName, "Sync");
            return builder;
        }

        private static void ConfigureCommon(
            Assembly targetAssembly,
            out Type serverType,
            out Type playerType)
        {
            serverType = targetAssembly.GetType(
                "Magicka.Network.NetworkServer",
                true);
            Type gameType = targetAssembly.GetType("Magicka.Game", true);
            playerType = targetAssembly.GetType("Magicka.GameLogic.Player", true);
            networkGamerType = targetAssembly.GetType(
                "Magicka.Gamers.NetworkGamer",
                true);
            gameSingletonField = RequireField(gameType, "mSingelton");
            gamePlayersField = RequireField(gameType, "mPlayers");
            playerIdProperty = RequireProperty(playerType, "ID");
            playerAvatarProperty = RequireProperty(playerType, "Avatar");
            playerGamerProperty = RequireProperty(playerType, "Gamer");
            networkGamerClientProperty = RequireProperty(
                networkGamerType,
                "ClientID");
        }

        private static MethodInfo CreateBuilderPrefix(MethodInfo target)
        {
            return typeof(NetworkServerForcedSyncPatch).GetMethod(
                "BuilderPrefix");
        }

        public static IEnumerable<CodeInstruction> RequestTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int lookup = -1;
            for (int index = 1; index < result.Count; index++)
            {
                if ((result[index].opcode != OpCodes.Call &&
                    result[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(result[index].operand, getFromHandle) ||
                    result[index - 1].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index - 1].operand, requestHandleField))
                    continue;
                if (lookup >= 0)
                    throw new InvalidOperationException(
                        "NetworkServer.ReadMessage contains multiple forced-sync entity lookups.");
                lookup = index;
            }
            if (lookup < 0)
                throw new InvalidOperationException(
                    "NetworkServer forced-sync entity lookup changed.");

            result.Insert(lookup, new CodeInstruction(OpCodes.Ldarg_2));
            result[lookup + 1].opcode = OpCodes.Call;
            result[lookup + 1].operand = resolverAdapter;
            return result;
        }

        public static bool BuilderPrefix(
            object __instance,
            int clientIndex,
            bool syncAfterwards)
        {
            object message = BuildForcedSyncPlayerStatuses();
            int count = Convert.ToInt32(responseCountField.GetValue(message));
            if (count != 0)
            {
                sendMessage.Invoke(
                    __instance,
                    new object[] { message, clientIndex, reliableSend });
                if (syncAfterwards)
                    sync.Invoke(__instance, null);
            }
            return false;
        }

        public static object BuildForcedSyncPlayerStatuses()
        {
            object game = gameSingletonField.GetValue(null);
            Array players = game == null
                ? null
                : gamePlayersField.GetValue(game) as Array;
            List<object> eligible = new List<object>();
            if (players != null)
            {
                for (int index = 0; index < players.Length; index++)
                {
                    object player = players.GetValue(index);
                    if (player == null)
                        continue;
                    object avatar = playerAvatarProperty.GetValue(player, null);
                    object gamer = playerGamerProperty.GetValue(player, null);
                    if (avatar != null && gamer != null &&
                        networkGamerType.IsInstanceOfType(gamer))
                        eligible.Add(player);
                }
            }

            object response = Activator.CreateInstance(responseType);
            Array updates = Array.CreateInstance(updateType, eligible.Count);
            responseCountField.SetValue(response, (short)eligible.Count);
            responseUpdatesField.SetValue(response, updates);
            for (int index = 0; index < eligible.Count; index++)
            {
                object player = eligible[index];
                object avatar = playerAvatarProperty.GetValue(player, null);
                object[] arguments = new object[] { null, serverState, 1f };
                getNetworkUpdate.Invoke(avatar, arguments);
                object update = arguments[0];
                int playerId = Convert.ToInt32(
                    playerIdProperty.GetValue(player, null));
                updateHandleField.SetValue(update, (ushort)playerId);
                updates.SetValue(update, index);
            }
            return response;
        }

        public static object ResolvePlayerAvatar(int playerId, object sender)
        {
            object game = gameSingletonField.GetValue(null);
            Array players = game == null
                ? null
                : gamePlayersField.GetValue(game) as Array;
            if (players == null)
                return null;
            for (int index = 0; index < players.Length; index++)
            {
                object player = players.GetValue(index);
                if (player == null || Convert.ToInt32(
                    playerIdProperty.GetValue(player, null)) != playerId)
                    continue;
                object avatar = playerAvatarProperty.GetValue(player, null);
                object gamer = playerGamerProperty.GetValue(player, null);
                if (avatar == null || gamer == null ||
                    !networkGamerType.IsInstanceOfType(gamer))
                    return null;
                object clientId = networkGamerClientProperty.GetValue(gamer, null);
                return Object.Equals(clientId, sender) ? avatar : null;
            }
            return null;
        }

        private static DynamicMethod BuildResolverAdapter(
            Type entityType,
            Type senderType)
        {
            DynamicMethod adapter = new DynamicMethod(
                "ForcedSyncPlayerResolverAdapter",
                entityType,
                new Type[] { typeof(int), senderType },
                typeof(NetworkServerForcedSyncPatch),
                true);
            ILGenerator il = adapter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            if (senderType.IsValueType)
                il.Emit(OpCodes.Box, senderType);
            il.Emit(
                OpCodes.Call,
                typeof(NetworkServerForcedSyncPatch).GetMethod(
                    "ResolvePlayerAvatar"));
            il.Emit(OpCodes.Castclass, entityType);
            il.Emit(OpCodes.Ret);
            return adapter;
        }

        private static MethodInfo FindIndexedSendMessage(Type serverType)
        {
            MethodInfo match = null;
            MethodInfo[] methods = serverType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "SendMessage" ||
                    !method.IsGenericMethodDefinition ||
                    method.ReturnType != typeof(void) ||
                    parameters.Length != 3 ||
                    !parameters[0].ParameterType.IsByRef ||
                    parameters[1].ParameterType != typeof(int) ||
                    !parameters[2].ParameterType.IsEnum)
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple indexed NetworkServer.SendMessage methods matched.");
                match = method;
            }
            if (match == null)
                throw new MissingMethodException(serverType.FullName, "SendMessage");
            return match;
        }

        private static MethodInfo FindMethod(
            Type type,
            string name,
            Type firstParameter,
            Type secondParameter)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == name && parameters.Length == 2 &&
                    parameters[0].ParameterType == firstParameter &&
                    (secondParameter == null ||
                        parameters[1].ParameterType == secondParameter))
                    return method;
            }
            throw new MissingMethodException(type.FullName, name);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (property == null || property.GetGetMethod(true) == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }
}
