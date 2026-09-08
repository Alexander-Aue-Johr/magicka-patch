using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

internal static class NetworkServerHotjoinScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        if (magicka.GetName().Version < new Version(1, 10))
        {
            report.AddNotApplicable(
                "hotjoin_broadcast.two_syncing_players",
                "Legacy NetworkServer does not queue messages during hotjoin sync");
            return;
        }
        NetworkServerHotjoinHarness harness =
            new NetworkServerHotjoinHarness(magicka);
        report.Add(
            "hotjoin_broadcast.two_syncing_players",
            harness.TwoSyncingPlayers());
    }
}

internal sealed class NetworkServerHotjoinHarness
{
    private readonly Type gameType;
    private readonly Type playerType;
    private readonly Type messageType;
    private readonly Type steamIdType;
    private readonly FieldInfo gameSingletonField;
    private readonly FieldInfo gamePlayersField;
    private readonly FieldInfo playerGamerField;
    private readonly FieldInfo playerStateField;
    private readonly FieldInfo playerQueueField;
    private readonly FieldInfo networkGamerClientField;
    private readonly FieldInfo clientsField;
    private readonly FieldInfo txBufferField;
    private readonly FieldInfo writerField;
    private readonly FieldInfo cacheablePacketsField;
    private readonly FieldInfo connectionIdField;
    private readonly MethodInfo sendMessage;
    private readonly PropertyInfo packetTypeProperty;
    private readonly object reliableSend;

    internal NetworkServerHotjoinHarness(Assembly magicka)
    {
        gameType = magicka.GetType("Magicka.Game", true);
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        messageType = magicka.GetType(
            "Magicka.Network.EntityRemoveMessage",
            true);
        Type serverType = magicka.GetType(
            "Magicka.Network.NetworkServer",
            true);
        Type networkGamerType = magicka.GetType(
            "Magicka.Gamers.NetworkGamer",
            true);
        gameSingletonField = RuntimeReflection.RequireField(
            gameType,
            "mSingelton");
        gamePlayersField = RuntimeReflection.RequireField(gameType, "mPlayers");
        playerGamerField = RuntimeReflection.RequireField(playerType, "mGamer");
        playerStateField = RuntimeReflection.RequireField(
            playerType,
            "mGamePlayState");
        playerQueueField = RuntimeReflection.RequireField(
            playerType,
            "mSyncMessageQueue");
        networkGamerClientField = RuntimeReflection.RequireField(
            networkGamerType,
            "mClientID");
        clientsField = RuntimeReflection.RequireField(serverType, "mClients");
        txBufferField = RuntimeReflection.RequireField(serverType, "mTxBuffer");
        writerField = RuntimeReflection.RequireField(serverType, "mWriter");
        cacheablePacketsField = RuntimeReflection.RequireField(
            serverType,
            "mCacheablePackets");
        Type connectionType = clientsField.FieldType.GetGenericArguments()[0];
        connectionIdField = RuntimeReflection.RequireField(connectionType, "ID");
        steamIdType = connectionIdField.FieldType;
        packetTypeProperty = messageType.GetProperty(
            "PacketType",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (packetTypeProperty == null)
            throw new MissingMemberException(messageType.FullName, "PacketType");
        sendMessage = FindSendMessage(serverType).MakeGenericMethod(messageType);
        Type sendType = sendMessage.GetParameters()[1].ParameterType;
        reliableSend = Enum.Parse(sendType, "Reliable");
    }

    internal ScenarioResult TwoSyncingPlayers()
    {
        object previousGame = gameSingletonField.GetValue(null);
        try
        {
            object firstId = CreateSteamId(101UL);
            object secondId = CreateSteamId(202UL);
            object first = CreateSyncingPlayer(firstId);
            object second = CreateSyncingPlayer(secondId);
            object game = FormatterServices.GetUninitializedObject(gameType);
            Array players = Array.CreateInstance(playerType, 2);
            players.SetValue(first, 0);
            players.SetValue(second, 1);
            gamePlayersField.SetValue(game, players);
            gameSingletonField.SetValue(null, game);

            object server = FormatterServices.GetUninitializedObject(
                clientsField.DeclaringType);
            IList clients = (IList)Activator.CreateInstance(clientsField.FieldType);
            clients.Add(CreateConnection(firstId));
            clients.Add(CreateConnection(secondId));
            clientsField.SetValue(server, clients);
            byte[] buffer = new byte[1024];
            txBufferField.SetValue(server, buffer);
            writerField.SetValue(
                server,
                new BinaryWriter(new MemoryStream(buffer)));
            object message = Activator.CreateInstance(messageType);
            object packetType = packetTypeProperty.GetValue(message, null);
            IList cacheablePackets = (IList)Activator.CreateInstance(
                cacheablePacketsField.FieldType);
            cacheablePackets.Add(packetType);
            cacheablePacketsField.SetValue(server, cacheablePackets);

            string actual = "returned";
            try
            {
                sendMessage.Invoke(
                    server,
                    new object[] { message, reliableSend });
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                actual = "exception:" + inner.GetType().Name;
            }
            int firstCount = ((ICollection)playerQueueField.GetValue(first)).Count;
            int secondCount = ((ICollection)playerQueueField.GetValue(second)).Count;
            string detail = actual + ",queues:" + firstCount + "," + secondCount;
            return new ScenarioResult(
                actual == "returned" && firstCount == 1 && secondCount == 1,
                detail,
                "returned,queues:1,1");
        }
        finally
        {
            gameSingletonField.SetValue(null, previousGame);
        }
    }

    private object CreateSyncingPlayer(object clientId)
    {
        object player = FormatterServices.GetUninitializedObject(playerType);
        object gamer = FormatterServices.GetUninitializedObject(
            networkGamerClientField.DeclaringType);
        networkGamerClientField.SetValue(gamer, clientId);
        playerGamerField.SetValue(player, gamer);
        playerStateField.SetValue(
            player,
            Enum.Parse(playerStateField.FieldType, "Sync"));
        playerQueueField.SetValue(
            player,
            Activator.CreateInstance(playerQueueField.FieldType));
        return player;
    }

    private object CreateConnection(object clientId)
    {
        object connection = Activator.CreateInstance(connectionIdField.DeclaringType);
        connectionIdField.SetValue(connection, clientId);
        return connection;
    }

    private object CreateSteamId(ulong value)
    {
        ConstructorInfo constructor = steamIdType.GetConstructor(
            new Type[] { typeof(ulong) });
        if (constructor != null)
            return constructor.Invoke(new object[] { value });
        object steamId = Activator.CreateInstance(steamIdType);
        FieldInfo valueField = steamIdType.GetField(
            "m_SteamID",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (valueField == null)
            throw new MissingMethodException(steamIdType.FullName, ".ctor(UInt64)");
        valueField.SetValue(steamId, value);
        return steamId;
    }

    private static MethodInfo FindSendMessage(Type serverType)
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
            throw new MissingMethodException(serverType.FullName, "SendMessage");
        return match;
    }
}
