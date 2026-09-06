using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class DynamicLightCacheScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        DynamicLightCacheHarness harness =
            new DynamicLightCacheHarness(magicka);
        try
        {
            report.Add(
                "dynamic_light_cache.level_dispose",
                harness.DisposeCache());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class DynamicLightCacheHarness
{
    private readonly Type dynamicLightType;
    private readonly FieldInfo cacheField;
    private readonly MethodInfo disposeCache;
    private readonly MethodInfo enqueue;
    private readonly HarmonyInstance harmony;

    internal DynamicLightCacheHarness(Assembly magicka)
    {
        dynamicLightType = magicka.GetType(
            "Magicka.Graphics.Lights.DynamicLight",
            true);
        cacheField = dynamicLightType.GetField(
            "sLightCache",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (cacheField == null || !cacheField.FieldType.IsGenericType ||
            cacheField.FieldType.GetGenericTypeDefinition().FullName !=
                "System.Collections.Generic.Queue`1" ||
            cacheField.FieldType.GetGenericArguments()[0] != dynamicLightType)
            throw new MissingFieldException(
                dynamicLightType.FullName,
                "sLightCache");

        disposeCache = dynamicLightType.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (disposeCache == null || disposeCache.ReturnType != typeof(void))
            throw new MissingMethodException(
                dynamicLightType.FullName,
                "DisposeCache");

        enqueue = cacheField.FieldType.GetMethod(
            "Enqueue",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { dynamicLightType },
            null);
        if (enqueue == null || enqueue.ReturnType != typeof(void))
            throw new MissingMethodException(
                cacheField.FieldType.FullName,
                "Enqueue");

        Type pointLight = dynamicLightType.BaseType;
        MethodInfo disposeShadowMap = pointLight == null
            ? null
            : pointLight.GetMethod(
                "DisposeShadowMap",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
        if (disposeShadowMap == null ||
            disposeShadowMap.ReturnType != typeof(void))
            throw new MissingMethodException(
                pointLight == null ? dynamicLightType.FullName : pointLight.FullName,
                "DisposeShadowMap");

        harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-dynamic-light-cache");
        harmony.Patch(
            disposeShadowMap,
            null,
            new HarmonyMethod(
                typeof(DynamicLightCacheProbe).GetMethod(
                    "DisposeShadowMapPostfix")),
            null);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(
            "org.magickacommunitypatch.behavior-probe-dynamic-light-cache");
    }

    internal ScenarioResult DisposeCache()
    {
        object cache = Activator.CreateInstance(cacheField.FieldType);
        object light = FormatterServices.GetUninitializedObject(
            dynamicLightType);
        GC.SuppressFinalize(light);
        enqueue.Invoke(cache, new object[] { light });
        cacheField.SetValue(null, cache);
        DynamicLightCacheProbe.ShadowMapDisposals = 0;

        bool completed = false;
        string exceptionType = "none";
        try
        {
            disposeCache.Invoke(null, new object[0]);
            completed = true;
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            exceptionType = inner.GetType().FullName;
        }

        int count = ((ICollection)cache).Count;
        int shadowMapDisposals =
            DynamicLightCacheProbe.ShadowMapDisposals;
        return new ScenarioResult(
            completed && count == 0 && shadowMapDisposals == 1,
            "completed:" + completed + ",count:" + count +
                ",shadow_disposals:" + shadowMapDisposals +
                ",exception:" + exceptionType,
            "completed:True,count:0,shadow_disposals:1,exception:none");
    }
}

public static class DynamicLightCacheProbe
{
    public static int ShadowMapDisposals;

    public static void DisposeShadowMapPostfix()
    {
        ShadowMapDisposals++;
    }
}
