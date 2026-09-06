using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class EntityStateStorageScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        EntityStateStorageHarness harness = new EntityStateStorageHarness(magicka);
        report.Add("entity_state_storage.constructor_release", harness.ConstructorReleasesPlayState());
        report.Add("entity_state_storage.current_restore", harness.RestoresWithCurrentPlayState());
        report.Add("entity_state_storage.empty_restore", harness.EmptyRestoreIsNoOp());
    }
}

internal sealed class EntityStateStorageHarness
{
    private readonly Type entityType;
    private readonly Type pickableStateType;
    private readonly Type pickableType;
    private readonly Type playStateType;
    private readonly Type storageType;
    private readonly ConstructorInfo constructor;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly MethodInfo restore;

    internal EntityStateStorageHarness(Assembly magicka)
    {
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        pickableType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.Pickable",
            true);
        pickableStateType = pickableType.GetNestedType(
            "State",
            BindingFlags.Public | BindingFlags.NonPublic);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        storageType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityStateStorage",
            true);
        constructor = FindConstructor();
        playStateField = storageType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        restore = storageType.GetMethod(
            "Restore",
            BindingFlags.Instance | BindingFlags.Public);
        InstallRestoreProbe();
    }

    internal ScenarioResult ConstructorReleasesPlayState()
    {
        object stalePlayState = NewUninitialized(playStateType);
        object storage = constructor.Invoke(new object[] { stalePlayState });
        bool retained = playStateField != null &&
            ReferenceEquals(playStateField.GetValue(storage), stalePlayState);
        return new ScenarioResult(
            !retained,
            retained ? "retained" : "released",
            "released");
    }

    internal ScenarioResult RestoresWithCurrentPlayState()
    {
        object previous = recentPlayStateField.GetValue(null);
        object stalePlayState = NewUninitialized(playStateType);
        object currentPlayState = NewUninitialized(playStateType);
        try
        {
            recentPlayStateField.SetValue(null, currentPlayState);
            object storage = constructor.Invoke(new object[] { stalePlayState });
            IList states = (IList)RuntimeReflection.ReadField(storage, "mPickableStates");
            states.Add(NewState());
            EntityStateStorageRestoreProbe.CapturedPlayState = null;
            try
            {
                restore.Invoke(storage, new object[] { NewEntityList() });
                return new ScenarioResult(false, "restore_not_reached", "current");
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException;
                while (inner != null && inner.InnerException != null)
                    inner = inner.InnerException;
                if (!(inner is EntityStateStorageRestoreProbeReachedException))
                    return new ScenarioResult(false, inner.GetType().FullName, "current");
            }

            bool current = ReferenceEquals(
                EntityStateStorageRestoreProbe.CapturedPlayState,
                currentPlayState);
            return new ScenarioResult(
                current,
                current ? "current" : "stale",
                "current");
        }
        finally
        {
            recentPlayStateField.SetValue(null, previous);
            EntityStateStorageRestoreProbe.CapturedPlayState = null;
        }
    }

    internal ScenarioResult EmptyRestoreIsNoOp()
    {
        object storage = constructor.Invoke(
            new object[] { NewUninitialized(playStateType) });
        IList target = NewEntityList();
        restore.Invoke(storage, new object[] { target });
        return new ScenarioResult(
            target.Count == 0,
            "count:" + target.Count,
            "count:0");
    }

    private void InstallRestoreProbe()
    {
        MethodInfo stateRestore = pickableStateType.GetMethod(
            "Restore",
            BindingFlags.Instance | BindingFlags.Public);
        ParameterInfo[] parameters = stateRestore.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != playStateType)
            throw new MissingMethodException(pickableStateType.FullName, "Restore");
        MethodInfo prefix = typeof(EntityStateStorageRestoreProbe)
            .GetMethod("Prefix")
            .MakeGenericMethod(new Type[] { playStateType });
        HarmonyInstance.Create("org.magickacommunitypatch.behavior-probe-state-storage")
            .Patch(stateRestore, new HarmonyMethod(prefix), null, null);
    }

    private ConstructorInfo FindConstructor()
    {
        ConstructorInfo[] constructors = storageType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int index = 0; index < constructors.Length; index++)
        {
            ParameterInfo[] parameters = constructors[index].GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == playStateType)
                return constructors[index];
        }
        throw new MissingMethodException(storageType.FullName, ".ctor");
    }

    private IList NewEntityList()
    {
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(entityType);
        return (IList)Activator.CreateInstance(listType);
    }

    private object NewState()
    {
        return pickableStateType.IsValueType
            ? Activator.CreateInstance(pickableStateType)
            : NewUninitialized(pickableStateType);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class EntityStateStorageRestoreProbe
{
    public static object CapturedPlayState;

    public static bool Prefix<TPlayState>(TPlayState iPlayState)
    {
        CapturedPlayState = iPlayState;
        throw new EntityStateStorageRestoreProbeReachedException();
    }
}

internal sealed class EntityStateStorageRestoreProbeReachedException : Exception
{
}
