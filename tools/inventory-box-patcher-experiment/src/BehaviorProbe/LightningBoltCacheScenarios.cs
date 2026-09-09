using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class LightningBoltCacheScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        LightningBoltCacheHarness harness =
            new LightningBoltCacheHarness(magicka, runtimePatchEnabled);
        report.Add(
            "lightning_bolt_cache.level_dispose",
            harness.InitializedDispose());
        report.Add(
            "lightning_bolt_cache.uninitialized_dispose",
            harness.UninitializedDispose());
    }
}

internal sealed class LightningBoltCacheHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly Type playStateType;
    private readonly Type lightningBoltType;
    private readonly MethodInfo playStateDispose;
    private readonly MethodInfo manualDisposeCache;
    private readonly FieldInfo contentField;
    private readonly FieldInfo cacheField;

    internal LightningBoltCacheHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        lightningBoltType = magicka.GetType(
            "Magicka.GameLogic.Spells.LightningBolt",
            true);
        playStateDispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (playStateDispose == null || playStateDispose.ReturnType != typeof(void))
            throw new MissingMethodException(playStateType.FullName, "Dispose");

        manualDisposeCache = lightningBoltType.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        contentField = RequireStaticField(lightningBoltType, "sContent");
        cacheField = RequireStaticField(lightningBoltType, "sCache");
        if (!typeof(IList).IsAssignableFrom(cacheField.FieldType))
            throw new MissingFieldException(lightningBoltType.FullName, "sCache");
    }

    internal ScenarioResult InitializedDispose()
    {
        IList cache = Populate();
        if (manualDisposeCache != null)
            Invoke(manualDisposeCache, null);
        else if (runtimePatchEnabled)
            InvokeRuntimeCleanup();
        return Result(cache, true);
    }

    internal ScenarioResult UninitializedDispose()
    {
        IList cache = Populate();
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mInitialized", false);
        Invoke(playStateDispose, playState);
        return Result(cache, false);
    }

    private IList Populate()
    {
        object content = NewUninitialized(contentField.FieldType);
        IList cache = (IList)Activator.CreateInstance(cacheField.FieldType);
        cache.Add(NewUninitialized(lightningBoltType));
        contentField.SetValue(null, content);
        cacheField.SetValue(null, cache);
        return cache;
    }

    private ScenarioResult Result(IList cache, bool expectedReleased)
    {
        bool contentReleased = contentField.GetValue(null) == null;
        bool cacheReleased = cache.Count == 0;
        string actual = "content:" + (contentReleased ? "null" : "set") +
            ",cache:" + cache.Count;
        string expected = expectedReleased
            ? "content:null,cache:0"
            : "content:set,cache:1";
        return new ScenarioResult(
            contentReleased == expectedReleased &&
                cacheReleased == expectedReleased,
            actual,
            expected);
    }

    private void InvokeRuntimeCleanup()
    {
        Type cleanupType =
            typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.LightningBoltCachePatch",
                false);
        MethodInfo cleanup = cleanupType == null
            ? null
            : cleanupType.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public);
        if (cleanup != null)
            Invoke(cleanup, null);
    }

    private static FieldInfo RequireStaticField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static object Invoke(MethodInfo method, object target)
    {
        try
        {
            return method.Invoke(target, new object[0]);
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
