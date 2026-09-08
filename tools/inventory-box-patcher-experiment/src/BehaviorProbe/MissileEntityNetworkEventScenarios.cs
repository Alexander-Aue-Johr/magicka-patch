using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class MissileEntityNetworkEventScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        MissileEntityNetworkEventHarness harness =
            new MissileEntityNetworkEventHarness(magicka);
        report.Add(
            "missile_event.uninitialized_state",
            harness.UninitializedState());
        report.Add(
            "missile_event.missing_collision_target",
            harness.MissingCollisionTarget());
        report.Add(
            "missile_event.valid_targetless_event",
            harness.ValidTargetlessEvent());
    }
}

internal sealed class MissileEntityNetworkEventHarness
{
    private readonly Type entityType;
    private readonly Type missileType;
    private readonly Type messageType;
    private readonly Type playStateType;
    private readonly FieldInfo instancesField;
    private readonly FieldInfo collisionField;
    private readonly FieldInfo targetField;
    private readonly ConstructorInfo constructor;
    private readonly MethodInfo networkEvent;

    internal MissileEntityNetworkEventHarness(Assembly magicka)
    {
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        missileType = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity",
            true);
        messageType = magicka.GetType(
            "Magicka.Network.MissileEntityEventMessage",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        RuntimeHelpers.RunClassConstructor(entityType.TypeHandle);
        instancesField = RuntimeReflection.RequireField(entityType, "mInstances");
        collisionField = RuntimeReflection.RequireField(messageType, "OnCollision");
        targetField = RuntimeReflection.RequireField(messageType, "TargetHandle");
        constructor = missileType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            new Type[] { playStateType },
            null);
        networkEvent = missileType.GetMethod(
            "NetworkEventMessage",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { messageType.MakeByRefType() },
            null);
        if (constructor == null)
            throw new MissingMethodException(missileType.FullName, ".ctor");
        if (networkEvent == null)
            throw new MissingMethodException(
                missileType.FullName,
                "NetworkEventMessage");
    }

    internal ScenarioResult UninitializedState()
    {
        ResetInstances();
        object missile = FormatterServices.GetUninitializedObject(missileType);
        return Invoke(missile, CreateMessage(false, ushort.MaxValue));
    }

    internal ScenarioResult MissingCollisionTarget()
    {
        ResetInstances();
        object missile = CreateInitializedMissile();
        return Invoke(missile, CreateMessage(true, ushort.MaxValue));
    }

    internal ScenarioResult ValidTargetlessEvent()
    {
        ResetInstances();
        object missile = CreateInitializedMissile();
        return Invoke(missile, CreateMessage(false, ushort.MaxValue));
    }

    private object CreateInitializedMissile()
    {
        object playState = FormatterServices.GetUninitializedObject(playStateType);
        return constructor.Invoke(new object[] { playState });
    }

    private object CreateMessage(bool collision, ushort target)
    {
        object message = Activator.CreateInstance(messageType);
        collisionField.SetValue(message, collision);
        targetField.SetValue(message, target);
        return message;
    }

    private void ResetInstances()
    {
        IList instances = (IList)instancesField.GetValue(null);
        instances.Clear();
    }

    private ScenarioResult Invoke(object missile, object message)
    {
        try
        {
            networkEvent.Invoke(missile, new object[] { message });
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
}
