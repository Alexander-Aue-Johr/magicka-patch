using System;
using System.Reflection;

internal static class TriggerActionAuthorityScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        TriggerActionAuthorityHarness harness =
            new TriggerActionAuthorityHarness(magicka, runtimePatchEnabled);
        report.Add(
            "trigger_authority.peer_spawn",
            harness.PeerSpawn());
        report.Add(
            "trigger_authority.server_spawn",
            harness.ServerSpawn());
        report.Add(
            "trigger_authority.peer_nonspawn",
            harness.PeerNonSpawn());
    }
}

internal sealed class TriggerActionAuthorityHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly Type messageType;
    private readonly Type steamIdType;
    private readonly FieldInfo actionTypeField;
    private readonly MethodInfo manualGuard;

    internal TriggerActionAuthorityHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        messageType = magicka.GetType(
            "Magicka.Network.TriggerActionMessage",
            true);
        Type clientType = magicka.GetType(
            "Magicka.Network.NetworkClient",
            true);
        actionTypeField = RuntimeReflection.RequireField(
            messageType,
            "ActionType");
        steamIdType = RuntimeReflection.RequireField(
            clientType,
            "mServerID").FieldType;
        Type helper = magicka.GetType(
            "Magicka.CommunityPatch.NetworkLifecycleCompatibility",
            false);
        manualGuard = helper == null
            ? null
            : helper.GetMethod(
                "CanProcessTriggerActionFrom",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
    }

    internal ScenarioResult PeerSpawn()
    {
        bool actual = CanProcess(
            "SpawnItem",
            CreateSteamId(1UL),
            CreateSteamId(2UL));
        return new ScenarioResult(
            !actual,
            actual.ToString(),
            Boolean.FalseString);
    }

    internal ScenarioResult ServerSpawn()
    {
        object server = CreateSteamId(2UL);
        bool actual = CanProcess("SpawnItem", server, server);
        return new ScenarioResult(
            actual,
            actual.ToString(),
            Boolean.TrueString);
    }

    internal ScenarioResult PeerNonSpawn()
    {
        bool actual = CanProcess(
            "Nullify",
            CreateSteamId(1UL),
            CreateSteamId(2UL));
        return new ScenarioResult(
            actual,
            actual.ToString(),
            Boolean.TrueString);
    }

    private bool CanProcess(
        string actionName,
        object sender,
        object server)
    {
        object action = Enum.Parse(actionTypeField.FieldType, actionName);
        if (runtimePatchEnabled)
        {
            Type helper = FindLoadedType(
                "Magicka.CommunityPatch.Runtime.TriggerActionAuthorityPatch");
            if (helper != null)
            {
                MethodInfo method = helper.GetMethod(
                    "ShouldProcess",
                    BindingFlags.Static | BindingFlags.Public);
                return (bool)method.Invoke(
                    null,
                    new object[] { Convert.ToInt32(action), sender, server });
            }
        }
        if (manualGuard == null)
            return true;
        object message = Activator.CreateInstance(messageType);
        actionTypeField.SetValue(message, action);
        object[] arguments = new object[] { message, sender, server };
        return (bool)manualGuard.Invoke(null, arguments);
    }

    private object CreateSteamId(ulong value)
    {
        ConstructorInfo constructor = steamIdType.GetConstructor(
            new Type[] { typeof(ulong) });
        if (constructor == null)
            throw new MissingMethodException(steamIdType.FullName, ".ctor(UInt64)");
        return constructor.Invoke(new object[] { value });
    }

    private static Type FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < assemblies.Length; index++)
        {
            Type type = assemblies[index].GetType(fullName, false);
            if (type != null)
                return type;
        }
        return null;
    }
}
