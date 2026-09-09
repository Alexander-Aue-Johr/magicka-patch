using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ItemPickableCacheScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ItemPickableCacheHarness harness =
            new ItemPickableCacheHarness(magicka, runtimePatchEnabled);
        report.Add(
            "item_pickable_cache.level_dispose",
            harness.InitializedDispose());
        report.Add(
            "item_pickable_cache.uninitialized_dispose",
            harness.UninitializedDispose());
    }
}

internal sealed class ItemPickableCacheHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly Type playStateType;
    private readonly Type itemType;
    private readonly MethodInfo playStateDispose;
    private readonly MethodInfo manualDisposeCache;
    private readonly MethodInfo enqueue;
    private readonly FieldInfo cacheField;

    internal ItemPickableCacheHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        itemType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.Item",
            true);
        playStateDispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (playStateDispose == null ||
            playStateDispose.ReturnType != typeof(void))
            throw new MissingMethodException(playStateType.FullName, "Dispose");

        manualDisposeCache = itemType.GetMethod(
            "DisposePickableCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        cacheField = itemType.GetField(
            "sPickableCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (cacheField == null ||
            !cacheField.FieldType.IsGenericType ||
            cacheField.FieldType.GetGenericTypeDefinition() != typeof(System.Collections.Generic.Queue<>))
            throw new MissingFieldException(itemType.FullName, "sPickableCache");
        enqueue = cacheField.FieldType.GetMethod(
            "Enqueue",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { itemType },
            null);
        if (enqueue == null)
            throw new MissingMethodException(cacheField.FieldType.FullName, "Enqueue");
    }

    internal ScenarioResult InitializedDispose()
    {
        object queue = Populate();
        if (manualDisposeCache != null)
            Invoke(manualDisposeCache, null, new object[0]);
        else if (runtimePatchEnabled)
            InvokeRuntimeCleanup();
        return Result(queue, true);
    }

    internal ScenarioResult UninitializedDispose()
    {
        object queue = Populate();
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mInitialized", false);
        Invoke(playStateDispose, playState, new object[0]);
        return Result(queue, false);
    }

    private object Populate()
    {
        object queue = Activator.CreateInstance(cacheField.FieldType);
        object item = NewUninitialized(itemType);
        Invoke(enqueue, queue, new object[] { item });
        cacheField.SetValue(null, queue);
        return queue;
    }

    private ScenarioResult Result(object queue, bool expectedReleased)
    {
        object current = cacheField.GetValue(null);
        bool released = current == null;
        bool sameQueue = Object.ReferenceEquals(current, queue);
        int count = ((ICollection)queue).Count;
        string actual = released
            ? "released,seed_count:" + count
            : "retained:" + (sameQueue ? "same" : "different") +
                ",seed_count:" + count;
        string expected = expectedReleased
            ? "released,seed_count:1"
            : "retained:same,seed_count:1";
        return new ScenarioResult(
            released == expectedReleased &&
                (expectedReleased || sameQueue) && count == 1,
            actual,
            expected);
    }

    private void InvokeRuntimeCleanup()
    {
        Type cleanupType =
            typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.ItemPickableCachePatch",
                false);
        MethodInfo cleanup = cleanupType == null
            ? null
            : cleanupType.GetMethod(
                "Release",
                BindingFlags.Static | BindingFlags.Public);
        if (cleanup != null)
            Invoke(cleanup, null, new object[0]);
    }

    private static object Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
