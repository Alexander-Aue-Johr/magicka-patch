using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

internal static class NetworkServerEnterSyncScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        NetworkServerEnterSyncHarness harness =
            new NetworkServerEnterSyncHarness(magicka);
        report.Add("enter_sync.unknown_sender", harness.UnknownSender());
        report.Add("enter_sync.connected_sender", harness.ConnectedSender());
    }
}

internal sealed class NetworkServerEnterSyncHarness
{
    private const uint SyncPoint = 0x12345678;

    private readonly object server;
    private readonly IList clients;
    private readonly Type connectionType;
    private readonly Type steamIdType;
    private readonly FieldInfo syncPointsField;
    private readonly MethodInfo readMessage;
    private readonly byte enterSyncPacket;

    internal NetworkServerEnterSyncHarness(Assembly magicka)
    {
        Type serverType = magicka.GetType("Magicka.Network.NetworkServer", true);
        Type packetType = magicka.GetType("Magicka.Network.PacketType", true);
        steamIdType = FindSteamIdType(serverType);
        FieldInfo clientsField = RuntimeReflection.RequireField(
            serverType,
            "mClients");
        connectionType = clientsField.FieldType.GetGenericArguments()[0];
        syncPointsField = RuntimeReflection.RequireField(
            connectionType,
            "SyncPoints");
        clients = (IList)Activator.CreateInstance(clientsField.FieldType);
        server = FormatterServices.GetUninitializedObject(serverType);
        clientsField.SetValue(server, clients);
        readMessage = serverType.GetMethod(
            "ReadMessage",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(BinaryReader), steamIdType },
            null);
        if (readMessage == null)
            throw new MissingMethodException(serverType.FullName, "ReadMessage");
        enterSyncPacket = Convert.ToByte(Enum.Parse(packetType, "EnterSync"));
    }

    internal ScenarioResult UnknownSender()
    {
        clients.Clear();
        return Invoke(Activator.CreateInstance(steamIdType), false);
    }

    internal ScenarioResult ConnectedSender()
    {
        clients.Clear();
        object connection = Activator.CreateInstance(connectionType);
        IList syncPoints = (IList)Activator.CreateInstance(syncPointsField.FieldType);
        syncPointsField.SetValue(connection, syncPoints);
        clients.Add(connection);
        ScenarioResult invocation = Invoke(
            Activator.CreateInstance(steamIdType),
            true);
        bool added = syncPoints.Count == 1 &&
            Convert.ToUInt32(syncPoints[0]) == SyncPoint;
        return new ScenarioResult(
            invocation.Status == "PASS" && added,
            invocation.Actual + ",sync_point:" + added,
            "returned,sync_point:True");
    }

    private ScenarioResult Invoke(object sender, bool connected)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(enterSyncPacket);
        writer.Write(SyncPoint);
        writer.Flush();
        stream.Position = 0;
        try
        {
            readMessage.Invoke(
                server,
                new object[] { new BinaryReader(stream), sender });
            return new ScenarioResult(
                true,
                "returned",
                connected ? "returned" : "returned");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(
                false,
                "exception:" + inner.GetType().Name,
                "returned");
        }
    }

    private static Type FindSteamIdType(Type serverType)
    {
        MethodInfo[] methods = serverType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "GetClient")
                continue;
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (parameters.Length == 1)
                return parameters[0].ParameterType;
        }
        throw new MissingMethodException(serverType.FullName, "GetClient");
    }
}
