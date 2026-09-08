using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

internal static class NetworkClientRulesetScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        NetworkClientRulesetHarness harness =
            new NetworkClientRulesetHarness(magicka);
        report.Add(
            "ruleset_update.detached_play_state",
            harness.DetachedPlayState());
    }
}

internal sealed class NetworkClientRulesetHarness
{
    private readonly object client;
    private readonly object sender;
    private readonly object message;
    private readonly byte packetType;
    private readonly FieldInfo recentPlayState;
    private readonly MethodInfo readMessage;
    private readonly MethodInfo writeMessage;

    internal NetworkClientRulesetHarness(Assembly magicka)
    {
        Type clientType = magicka.GetType("Magicka.Network.NetworkClient", true);
        Type rulesetMessageType = magicka.GetType(
            "Magicka.Network.RulesetMessage",
            true);
        Type packetTypeType = magicka.GetType("Magicka.Network.PacketType", true);
        Type playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        recentPlayState = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        readMessage = FindReadMessage(clientType);
        sender = Activator.CreateInstance(
            readMessage.GetParameters()[1].ParameterType);
        client = FormatterServices.GetUninitializedObject(clientType);
        message = Activator.CreateInstance(rulesetMessageType);
        writeMessage = rulesetMessageType.GetMethod(
            "Write",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(BinaryWriter) },
            null);
        if (writeMessage == null)
            throw new MissingMethodException(rulesetMessageType.FullName, "Write");
        packetType = Convert.ToByte(Enum.Parse(packetTypeType, "RulesetUpdate"));
    }

    internal ScenarioResult DetachedPlayState()
    {
        recentPlayState.SetValue(null, null);
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(packetType);
        writeMessage.Invoke(message, new object[] { writer });
        writer.Flush();
        stream.Position = 0;
        try
        {
            readMessage.Invoke(
                client,
                new object[] { new BinaryReader(stream), sender });
            return new ScenarioResult(true, "returned", "returned");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            string target = inner.TargetSite == null
                ? "unknown"
                : inner.TargetSite.DeclaringType.FullName + "." +
                    inner.TargetSite.Name;
            return new ScenarioResult(
                false,
                "exception:" + inner.GetType().Name + "@" + target,
                "returned");
        }
    }

    private static MethodInfo FindReadMessage(Type clientType)
    {
        MethodInfo[] methods = clientType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            MethodInfo method = methods[index];
            ParameterInfo[] parameters = method.GetParameters();
            if (method.Name == "ReadMessage" &&
                method.ReturnType == typeof(void) &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(BinaryReader))
                return method;
        }
        throw new MissingMethodException(clientType.FullName, "ReadMessage");
    }
}
