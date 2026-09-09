using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GibTeardownScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type gib = magicka.GetType(
            "Magicka.GameLogic.Entities.Gib",
            false);
        Type entity = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            false);
        if (gib == null || entity == null)
        {
            report.AddNotApplicable(
                "gib_teardown.active_instance",
                "Gib or Entity is not present in this version");
            report.AddNotApplicable(
                "gib_teardown.cached_instance",
                "Gib or Entity is not present in this version");
            return;
        }

        report.Add(
            "gib_teardown.active_instance",
            RunCase(magicka, runtimePatchEnabled, gib, entity, false));
        report.Add(
            "gib_teardown.cached_instance",
            RunCase(magicka, runtimePatchEnabled, gib, entity, true));
    }

    private static ScenarioResult RunCase(
        Assembly magicka,
        bool runtimePatchEnabled,
        Type gibType,
        Type entityType,
        bool cached)
    {
        FieldInfo modelField = RequireField(gibType, "mModel");
        FieldInfo renderDataField = RequireField(gibType, "mRenderData");
        FieldInfo meshField = RequireField(gibType, "mMesh");
        FieldInfo meshPartField = RequireField(gibType, "mMeshPart");
        FieldInfo bloodEffectField = RequireField(gibType, "mBloodEffect");
        FieldInfo trailEffectField = RequireField(gibType, "mTrailEffect");
        FieldInfo cacheField = RequireStaticField(gibType, "GibCache");
        FieldInfo instancesField = RequireStaticField(entityType, "mInstances");
        MethodInfo clearHandles = entityType.GetMethod(
            "ClearHandles",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (clearHandles == null)
            throw new MissingMethodException(entityType.FullName, "ClearHandles");

        object gib = FormatterServices.GetUninitializedObject(gibType);
        SetInactiveEffect(gib, bloodEffectField);
        SetInactiveEffect(gib, trailEffectField);
        modelField.SetValue(
            gib,
            FormatterServices.GetUninitializedObject(modelField.FieldType));
        meshField.SetValue(
            gib,
            FormatterServices.GetUninitializedObject(meshField.FieldType));
        meshPartField.SetValue(
            gib,
            FormatterServices.GetUninitializedObject(meshPartField.FieldType));
        Type renderDataType = renderDataField.FieldType.GetElementType();
        Array renderData = Array.CreateInstance(renderDataType, 3);
        for (int index = 0; index < renderData.Length; index++)
        {
            renderData.SetValue(
                FormatterServices.GetUninitializedObject(renderDataType),
                index);
        }
        renderDataField.SetValue(gib, renderData);

        IList cache = CreateList(cacheField.FieldType);
        IList instances = instancesField.GetValue(null) as IList;
        if (instances == null)
            throw new InvalidOperationException("Entity handle list is unavailable.");
        cacheField.SetValue(null, cache);
        instances.Clear();
        instances.Add(gib);
        if (cached)
            cache.Add(gib);

        Exception failure = Invoke(clearHandles, null, new object[0]);
        if (!runtimePatchEnabled)
        {
            MethodInfo disposeCache = gibType.GetMethod(
                "DisposeCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (disposeCache != null && failure == null)
                failure = Invoke(disposeCache, null, new object[0]);
        }

        bool fieldsReleased =
            modelField.GetValue(gib) == null &&
            renderDataField.GetValue(gib) == null &&
            meshField.GetValue(gib) == null &&
            meshPartField.GetValue(gib) == null;
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

    private static IList CreateList(Type type)
    {
        IList value = Activator.CreateInstance(type) as IList;
        if (value == null)
            throw new InvalidOperationException(type.FullName + " is not a list.");
        return value;
    }

    private static void SetInactiveEffect(object target, FieldInfo field)
    {
        object effect = Activator.CreateInstance(field.FieldType);
        FieldInfo id = field.FieldType.GetField(
            "ID",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (id == null || id.FieldType != typeof(int))
            throw new MissingFieldException(field.FieldType.FullName, "ID");
        id.SetValue(effect, -1);
        field.SetValue(target, effect);
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
