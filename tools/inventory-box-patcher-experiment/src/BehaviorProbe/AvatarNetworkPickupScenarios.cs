using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class AvatarNetworkPickupScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        AvatarNetworkPickupHarness harness =
            new AvatarNetworkPickupHarness(magicka);
        report.Add("network_pickup.missing_target", harness.MissingTarget());
        report.Add("network_pickup.bodyless_pickup", harness.Bodyless("PickUp"));
        report.Add(
            "network_pickup.bodyless_pickup_request",
            harness.Bodyless("PickUpRequest"));
    }
}

internal sealed class AvatarNetworkPickupHarness
{
    private readonly Type entityType;
    private readonly Type itemType;
    private readonly Type messageType;
    private readonly FieldInfo instancesField;
    private readonly FieldInfo actionField;
    private readonly FieldInfo targetField;
    private readonly FieldInfo playerField;
    private readonly FieldInfo queuedActionsField;
    private readonly MethodInfo networkAction;
    private readonly object avatar;

    internal AvatarNetworkPickupHarness(Assembly magicka)
    {
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        itemType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.Item",
            true);
        messageType = magicka.GetType(
            "Magicka.Network.CharacterActionMessage",
            true);
        Type avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        RuntimeHelpers.RunClassConstructor(entityType.TypeHandle);
        instancesField = RuntimeReflection.RequireField(entityType, "mInstances");
        actionField = RuntimeReflection.RequireField(messageType, "Action");
        targetField = RuntimeReflection.RequireField(messageType, "TargetHandle");
        playerField = RuntimeReflection.RequireField(avatarType, "mPlayer");
        queuedActionsField = RuntimeReflection.RequireField(
            avatarType,
            "mQueuedNetActions");
        networkAction = avatarType.GetMethod(
            "NetworkAction",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { messageType.MakeByRefType() },
            null);
        if (networkAction == null)
            throw new MissingMethodException(avatarType.FullName, "NetworkAction");
        avatar = FormatterServices.GetUninitializedObject(avatarType);
        playerField.SetValue(
            avatar,
            FormatterServices.GetUninitializedObject(playerField.FieldType));
        queuedActionsField.SetValue(
            avatar,
            Activator.CreateInstance(queuedActionsField.FieldType));
    }

    internal ScenarioResult MissingTarget()
    {
        ResetInstances();
        return Invoke("PickUp", 0, true);
    }

    internal ScenarioResult Bodyless(string action)
    {
        IList instances = ResetInstances();
        instances.Add(FormatterServices.GetUninitializedObject(itemType));
        return Invoke(action, 0, true);
    }

    private IList ResetInstances()
    {
        IList instances = (IList)instancesField.GetValue(null);
        instances.Clear();
        return instances;
    }

    private ScenarioResult Invoke(string action, ushort target, bool expected)
    {
        return Invoke(
            Convert.ToByte(Enum.Parse(actionField.FieldType, action)),
            target,
            expected);
    }

    private ScenarioResult Invoke(byte action, ushort target, bool expected)
    {
        object message = Activator.CreateInstance(messageType);
        actionField.SetValue(message, Enum.ToObject(actionField.FieldType, action));
        targetField.SetValue(message, target);
        try
        {
            networkAction.Invoke(avatar, new object[] { message });
            return new ScenarioResult(true, "returned", "returned");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(
                false,
                "exception:" + inner.GetType().Name,
                expected ? "returned" : "exception");
        }
    }
}
