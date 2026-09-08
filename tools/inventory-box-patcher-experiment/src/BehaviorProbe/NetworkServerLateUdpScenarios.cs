using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class NetworkServerLateUdpScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        NetworkServerLateUdpHarness harness =
            new NetworkServerLateUdpHarness(magicka, runtimePatchEnabled);
        report.Add("late_udp.empty_client_list", harness.EmptyClientList());
        report.Add("late_udp.negative_client_index", harness.NegativeClientIndex());
        report.Add("late_udp.valid_client_identity", harness.ValidClientIdentity());
    }
}

internal sealed class NetworkServerLateUdpHarness
{
    private readonly object server;
    private readonly Type connectionType;
    private readonly object clients;
    private readonly MethodInfo queue;
    private readonly MethodInfo runtimeGet;

    internal NetworkServerLateUdpHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        Type serverType = magicka.GetType("Magicka.Network.NetworkServer", true);
        FieldInfo clientsField = serverType.GetField(
            "mClients",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (clientsField == null)
            throw new MissingFieldException(serverType.FullName, "mClients");
        connectionType = clientsField.FieldType.GetGenericArguments()[0];
        clients = Activator.CreateInstance(clientsField.FieldType);
        server = FormatterServices.GetUninitializedObject(serverType);
        clientsField.SetValue(server, clients);
        queue = FindQueue(serverType, magicka);

        runtimeGet = runtimePatchEnabled
            ? typeof(Magicka.CommunityPatch.Runtime.NetworkServerLateUdpPatch)
                .GetMethod("IsValidClient")
            : null;
    }

    internal ScenarioResult EmptyClientList()
    {
        return InvokeInvalid(0);
    }

    internal ScenarioResult NegativeClientIndex()
    {
        return InvokeInvalid(-1);
    }

    internal ScenarioResult ValidClientIdentity()
    {
        object connection = FormatterServices.GetUninitializedObject(connectionType);
        clients.GetType().GetMethod("Add").Invoke(clients, new object[] { connection });
        bool valid = true;
        if (runtimeGet != null)
        {
            valid = (bool)runtimeGet.Invoke(null, new object[] { clients, 0 });
        }
        object actual = clients.GetType().GetProperty("Item").GetValue(
            clients,
            new object[] { 0 });
        bool same = connectionType.IsValueType
            ? Object.Equals(actual, connection)
            : Object.ReferenceEquals(actual, connection);
        return new ScenarioResult(
            valid && same,
            (valid && same).ToString(),
            Boolean.TrueString);
    }

    private ScenarioResult InvokeInvalid(int index)
    {
        object message = Activator.CreateInstance(
            queue.GetGenericArguments()[0]);
        try
        {
            queue.Invoke(server, new object[] { index, message });
            return new ScenarioResult(true, "returned", "returned");
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

    private static MethodInfo FindQueue(Type serverType, Assembly magicka)
    {
        MethodInfo definition = null;
        MethodInfo[] methods = serverType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "QueueUDPMessage" &&
                methods[index].IsGenericMethodDefinition)
            {
                definition = methods[index];
                break;
            }
        }
        if (definition == null)
            throw new MissingMethodException(serverType.FullName, "QueueUDPMessage");
        Type messageType = magicka.GetType(
            "Magicka.Network.EntityRemoveMessage",
            true);
        return definition.MakeGenericMethod(messageType);
    }
}
