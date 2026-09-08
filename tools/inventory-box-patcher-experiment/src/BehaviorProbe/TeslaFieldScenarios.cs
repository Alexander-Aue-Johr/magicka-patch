using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class TeslaFieldScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        TeslaFieldHarness harness = new TeslaFieldHarness(magicka);
        report.Add(
            "tesla_field.initialized_pool_release",
            harness.InitializedPoolRelease());
        report.Add(
            "tesla_field.empty_pool_allocation_release",
            harness.EmptyPoolAllocationRelease());
    }
}

internal sealed class TeslaFieldHarness
{
    private readonly Type playStateType;
    private readonly MethodInfo getFromCache;
    private readonly MethodInfo initializeCache;
    private readonly FieldInfo cacheField;
    private readonly FieldInfo legacyPlayStateField;

    internal TeslaFieldHarness(Assembly magicka)
    {
        Type teslaFieldType = magicka.GetType(
            "Magicka.GameLogic.Entities.TeslaField",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        initializeCache = teslaFieldType.GetMethod(
            "InitializeCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(int), playStateType },
            null);
        getFromCache = teslaFieldType.GetMethod(
            "GetFromCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playStateType },
            null);
        cacheField = teslaFieldType.GetField(
            "sCache",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        legacyPlayStateField = teslaFieldType.GetField(
            "mPlaystate",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (initializeCache == null || getFromCache == null ||
            cacheField == null)
            throw new MissingMemberException(
                "TeslaField cache behavior contract is incomplete.");
    }

    internal ScenarioResult InitializedPoolRelease()
    {
        object supplied = NewUninitialized(playStateType);
        Invoke(initializeCache, null, new object[] { 3, supplied });
        IList cache = (IList)cacheField.GetValue(null);
        int retained = CountRetained(cache, supplied);
        bool passed = cache.Count == 3 && retained == 0;
        string actual = "count:" + cache.Count + ",retained:" + retained;
        return new ScenarioResult(
            passed,
            actual,
            "count:3,retained:0");
    }

    internal ScenarioResult EmptyPoolAllocationRelease()
    {
        IList cache = (IList)Activator.CreateInstance(cacheField.FieldType);
        cacheField.SetValue(null, cache);
        object supplied = NewUninitialized(playStateType);
        object field = Invoke(
            getFromCache,
            null,
            new object[] { supplied });
        bool retained = legacyPlayStateField != null &&
            ReferenceEquals(legacyPlayStateField.GetValue(field), supplied);
        bool passed = field != null && cache.Count == 0 && !retained;
        string actual = "created:" + (field != null) + ",count:" +
            cache.Count + ",play_state:" +
            (retained ? "retained" : "released");
        return new ScenarioResult(
            passed,
            actual,
            "created:True,count:0,play_state:released");
    }

    private int CountRetained(IList cache, object playState)
    {
        if (legacyPlayStateField == null)
            return 0;
        int retained = 0;
        for (int index = 0; index < cache.Count; index++)
        {
            if (ReferenceEquals(
                legacyPlayStateField.GetValue(cache[index]),
                playState))
                retained++;
        }
        return retained;
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
