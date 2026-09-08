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
        report.Add(
            "missile_event.missing_collision_target_cleanup",
            harness.MissingCollisionTargetCleanup());
    }
}

internal sealed class MissileEntityNetworkEventHarness
{
    private readonly Type entityType;
    private readonly Type missileType;
    private readonly Type messageType;
    private readonly Type playStateType;
    private readonly Type entityManagerType;
    private readonly Type entityListType;
    private readonly FieldInfo instancesField;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo playStateEntityManagerField;
    private readonly FieldInfo managerEntitiesField;
    private readonly FieldInfo collisionField;
    private readonly FieldInfo handleField;
    private readonly FieldInfo targetField;
    private readonly ConstructorInfo constructor;
    private readonly MethodInfo networkEvent;
    private readonly MethodInfo messageWrite;
    private readonly MethodInfo serverReadMessage;
    private readonly PropertyInfo deadProperty;
    private readonly object server;
    private readonly Type steamIdType;
    private readonly byte missilePacket;

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
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        entityListType = magicka.GetType("Magicka.StaticObjectList`1", true)
            .MakeGenericType(entityType);
        RuntimeHelpers.RunClassConstructor(entityType.TypeHandle);
        instancesField = RuntimeReflection.RequireField(entityType, "mInstances");
        playStateField = RuntimeReflection.RequireField(entityType, "mPlayState");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        playStateEntityManagerField = RuntimeReflection.RequireField(
            playStateType,
            "mEntityManager");
        managerEntitiesField = RuntimeReflection.RequireField(
            entityManagerType,
            "mEntities");
        collisionField = RuntimeReflection.RequireField(messageType, "OnCollision");
        handleField = RuntimeReflection.RequireField(messageType, "Handle");
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

        Type serverType = magicka.GetType("Magicka.Network.NetworkServer", true);
        Type packetType = magicka.GetType("Magicka.Network.PacketType", true);
        server = FormatterServices.GetUninitializedObject(serverType);
        steamIdType = FindSteamIdType(serverType);
        serverReadMessage = serverType.GetMethod(
            "ReadMessage",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(System.IO.BinaryReader), steamIdType },
            null);
        messageWrite = messageType.GetMethod(
            "Write",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(System.IO.BinaryWriter) },
            null);
        deadProperty = missileType.GetProperty(
            "Dead",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (serverReadMessage == null)
            throw new MissingMethodException(serverType.FullName, "ReadMessage");
        if (messageWrite == null)
            throw new MissingMethodException(messageType.FullName, "Write");
        if (deadProperty == null || deadProperty.GetGetMethod(true) == null)
            throw new MissingMemberException(missileType.FullName, "Dead");
        missilePacket = Convert.ToByte(Enum.Parse(packetType, "MissileEntity"));
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

    internal ScenarioResult MissingCollisionTargetCleanup()
    {
        ResetInstances();
        object missile = CreateInitializedMissile();
        object playState = playStateField.GetValue(missile);
        AttachToActiveState(playState, missile);
        object previousPlayState = recentPlayStateField.GetValue(null);
        object message = CreateMessage(true, ushort.MaxValue);
        handleField.SetValue(message, (ushort)0);

        System.IO.MemoryStream stream = new System.IO.MemoryStream();
        System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream);
        writer.Write(missilePacket);
        messageWrite.Invoke(message, new object[] { writer });
        writer.Flush();
        stream.Position = 0;

        string actual;
        try
        {
            recentPlayStateField.SetValue(null, playState);
            serverReadMessage.Invoke(
                server,
                new object[]
                {
                    new System.IO.BinaryReader(stream),
                    Activator.CreateInstance(steamIdType)
                });
            actual = "returned";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            actual = "exception:" + inner.GetType().Name;
        }
        finally
        {
            recentPlayStateField.SetValue(null, previousPlayState);
        }
        bool dead = (bool)deadProperty.GetValue(missile, null);
        return new ScenarioResult(
            actual == "returned" && dead,
            actual + ",dead:" + dead,
            "returned,dead:True");
    }

    private object CreateInitializedMissile()
    {
        object playState = FormatterServices.GetUninitializedObject(playStateType);
        return constructor.Invoke(new object[] { playState });
    }

    private void AttachToActiveState(object playState, object missile)
    {
        object manager = FormatterServices.GetUninitializedObject(
            entityManagerType);
        object entities = Activator.CreateInstance(
            entityListType,
            new object[] { 8 });
        MethodInfo add = entityListType.GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { entityType },
            null);
        if (add == null)
            throw new MissingMethodException(entityListType.FullName, "Add");
        add.Invoke(entities, new object[] { missile });
        managerEntitiesField.SetValue(manager, entities);
        playStateEntityManagerField.SetValue(playState, manager);
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
