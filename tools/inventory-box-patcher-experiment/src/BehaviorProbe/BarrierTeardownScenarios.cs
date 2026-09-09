using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class BarrierTeardownScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type barrier = magicka.GetType(
            "Magicka.GameLogic.Entities.Barrier",
            false);
        Type entity = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            false);
        Type hitList = magicka.GetType(
            "Magicka.GameLogic.Entities.Barrier+HitListWithBarriers",
            false);
        if (barrier == null || entity == null || hitList == null)
        {
            report.AddNotApplicable(
                "barrier_teardown.active_instance",
                "Barrier teardown types are not present in this version");
            report.AddNotApplicable(
                "barrier_teardown.cached_instance",
                "Barrier teardown types are not present in this version");
            report.AddNotApplicable(
                "barrier_teardown.hit_list_cache",
                "Barrier teardown types are not present in this version");
            return;
        }

        report.Add(
            "barrier_teardown.active_instance",
            RunBarrierCase(magicka, runtimePatchEnabled, barrier, entity, false));
        report.Add(
            "barrier_teardown.cached_instance",
            RunBarrierCase(magicka, runtimePatchEnabled, barrier, entity, true));
        report.Add(
            "barrier_teardown.hit_list_cache",
            RunHitListCase(magicka, runtimePatchEnabled, hitList));
    }

    private static ScenarioResult RunBarrierCase(
        Assembly magicka,
        bool runtimePatchEnabled,
        Type barrierType,
        Type entityType,
        bool cached)
    {
        FieldInfo ownerField = RequireField(barrierType, "mOwner");
        FieldInfo resistancesField = RequireField(barrierType, "mResistances");
        FieldInfo statusEffectsField = RequireField(barrierType, "mStatusEffects");
        FieldInfo iceRenderField = RequireField(barrierType, "mIceRenderData");
        FieldInfo earthRenderField = RequireField(barrierType, "mEarthRenderData");
        FieldInfo runeRenderField = RequireField(barrierType, "mRuneRenderData");
        FieldInfo cacheField = RequireStaticField(barrierType, "mCache");
        FieldInfo instancesField = RequireStaticField(entityType, "mInstances");

        object barrier = FormatterServices.GetUninitializedObject(barrierType);
        Type avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        object owner = FormatterServices.GetUninitializedObject(avatarType);
        ownerField.SetValue(barrier, owner);
        resistancesField.SetValue(
            barrier,
            Array.CreateInstance(resistancesField.FieldType.GetElementType(), 1));
        statusEffectsField.SetValue(
            barrier,
            Array.CreateInstance(statusEffectsField.FieldType.GetElementType(), 1));
        iceRenderField.SetValue(
            barrier,
            Array.CreateInstance(iceRenderField.FieldType.GetElementType(), 1));
        earthRenderField.SetValue(
            barrier,
            Array.CreateInstance(earthRenderField.FieldType.GetElementType(), 1));
        runeRenderField.SetValue(
            barrier,
            Array.CreateInstance(runeRenderField.FieldType.GetElementType(), 1));

        IList cache = CreateList(cacheField.FieldType);
        IList instances = instancesField.GetValue(null) as IList;
        if (instances == null)
            throw new InvalidOperationException("Entity handle list is unavailable.");
        cacheField.SetValue(null, cache);
        instances.Clear();
        if (cached)
            cache.Add(barrier);
        else
            instances.Add(barrier);

        Exception failure = null;
        if (runtimePatchEnabled)
        {
            failure = InvokeRuntimeCleanup();
        }
        else
        {
            MethodInfo dispose = barrierType.GetMethod(
                cached ? "DisposeCache" : "Dispose",
                (cached ? BindingFlags.Static : BindingFlags.Instance) |
                    BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose != null)
                failure = Invoke(dispose, cached ? null : barrier, new object[0]);
        }

        bool fieldsReleased =
            ownerField.GetValue(barrier) == null &&
            resistancesField.GetValue(barrier) == null &&
            statusEffectsField.GetValue(barrier) == null &&
            iceRenderField.GetValue(barrier) == null &&
            earthRenderField.GetValue(barrier) == null &&
            runeRenderField.GetValue(barrier) == null;
        bool cacheCleared = cache.Count == 0;
        instances.Clear();
        cache.Clear();
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        return new ScenarioResult(
            failure == null && fieldsReleased && cacheCleared,
            "exception:" + exception + ",fields_released:" + fieldsReleased +
                ",cache_cleared:" + cacheCleared,
            "exception:none,fields_released:True,cache_cleared:True");
    }

    private static ScenarioResult RunHitListCase(
        Assembly magicka,
        bool runtimePatchEnabled,
        Type hitListType)
    {
        FieldInfo instancesField = RequireStaticField(hitListType, "sInstances");
        FieldInfo cacheField = RequireStaticField(hitListType, "sHitListCache");
        IList instances = CreateList(instancesField.FieldType);
        IList cache = CreateList(cacheField.FieldType);
        object entry = FormatterServices.GetUninitializedObject(hitListType);
        instances.Add(entry);
        cache.Add(entry);
        instancesField.SetValue(null, instances);
        cacheField.SetValue(null, cache);

        Exception failure = null;
        if (runtimePatchEnabled)
        {
            failure = InvokeRuntimeCleanup();
        }
        else
        {
            MethodInfo dispose = hitListType.GetMethod(
                "DisposeCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose != null)
                failure = Invoke(dispose, null, new object[0]);
        }

        bool released = instances.Count == 0 && cache.Count == 0;
        instances.Clear();
        cache.Clear();
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        return new ScenarioResult(
            failure == null && released,
            "exception:" + exception + ",caches_released:" + released,
            "exception:none,caches_released:True");
    }

    private static Exception InvokeRuntimeCleanup()
    {
        Type patch = typeof(
            Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.BarrierTeardownPatch",
                false);
        MethodInfo cleanup = patch == null
            ? null
            : patch.GetMethod(
                "CleanupAll",
                BindingFlags.Static | BindingFlags.Public);
        return cleanup == null
            ? new MissingMethodException(
                "Magicka.CommunityPatch.Runtime.BarrierTeardownPatch",
                "CleanupAll")
            : Invoke(cleanup, null, new object[0]);
    }

    private static IList CreateList(Type type)
    {
        IList value = Activator.CreateInstance(type) as IList;
        if (value == null)
            throw new InvalidOperationException(type.FullName + " is not a list.");
        return value;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static FieldInfo RequireStaticField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static Exception Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException ?? exception;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
