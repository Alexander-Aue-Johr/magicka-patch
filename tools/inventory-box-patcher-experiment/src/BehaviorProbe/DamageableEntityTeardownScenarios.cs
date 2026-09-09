using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class DamageableEntityTeardownScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        Type damageable = magicka.GetType(
            "Magicka.GameLogic.Entities.DamageablePhysicsEntity",
            false);
        Type entity = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            false);
        if (damageable == null || entity == null)
        {
            report.AddNotApplicable(
                "damageable_teardown.final_references",
                "DamageablePhysicsEntity is not present in this version");
            return;
        }

        report.Add(
            "damageable_teardown.final_references",
            RunCase(damageable, entity));
    }

    private static ScenarioResult RunCase(Type damageableType, Type entityType)
    {
        FieldInfo gibsField = RequireField(damageableType, "mGibs");
        FieldInfo animationsField = RequireField(damageableType, "mAnimations");
        FieldInfo resistancesField = RequireField(damageableType, "mResistances");
        FieldInfo statusEffectsField = RequireField(damageableType, "mStatusEffects");
        FieldInfo currentStatusField = RequireField(
            damageableType,
            "mCurrentStatusEffects");
        FieldInfo liveEffectsField = RequireField(damageableType, "mLiveEffects");
        FieldInfo renderDataField = RequireField(damageableType, "mRenderData");
        FieldInfo highlightField = RequireField(
            damageableType,
            "mHighlightRenderData");
        FieldInfo hitListField = RequireField(damageableType, "mHitList");
        FieldInfo instancesField = RequireField(entityType, "mInstances");
        MethodInfo clearHandles = entityType.GetMethod(
            "ClearHandles",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (clearHandles == null)
            throw new MissingMethodException(entityType.FullName, "ClearHandles");

        object entity = FormatterServices.GetUninitializedObject(damageableType);
        gibsField.SetValue(entity, Activator.CreateInstance(gibsField.FieldType));
        animationsField.SetValue(
            entity,
            Activator.CreateInstance(animationsField.FieldType));
        resistancesField.SetValue(
            entity,
            Array.CreateInstance(resistancesField.FieldType.GetElementType(), 0));
        statusEffectsField.SetValue(
            entity,
            Array.CreateInstance(statusEffectsField.FieldType.GetElementType(), 0));
        currentStatusField.SetValue(entity, FirstNonZeroEnum(currentStatusField.FieldType));
        liveEffectsField.SetValue(
            entity,
            Activator.CreateInstance(liveEffectsField.FieldType));
        renderDataField.SetValue(
            entity,
            Array.CreateInstance(renderDataField.FieldType.GetElementType(), 0));
        highlightField.SetValue(
            entity,
            Array.CreateInstance(highlightField.FieldType.GetElementType(), 0));
        ConstructorInfo hitListConstructor = hitListField.FieldType.GetConstructor(
            new Type[] { typeof(int) });
        if (hitListConstructor == null)
            throw new MissingMethodException(hitListField.FieldType.FullName, ".ctor(Int32)");
        hitListField.SetValue(
            entity,
            hitListConstructor.Invoke(new object[] { 4 }));

        IList instances = instancesField.GetValue(null) as IList;
        if (instances == null)
            throw new InvalidOperationException("Entity handle list is unavailable.");
        instances.Clear();
        instances.Add(entity);
        Exception failure = Invoke(clearHandles, null, new object[0]);
        bool released =
            gibsField.GetValue(entity) == null &&
            animationsField.GetValue(entity) == null &&
            resistancesField.GetValue(entity) == null &&
            statusEffectsField.GetValue(entity) == null &&
            Convert.ToInt64(currentStatusField.GetValue(entity)) == 0;
        instances.Clear();
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        return new ScenarioResult(
            failure == null && released,
            "exception:" + exception + ",references_released:" + released,
            "exception:none,references_released:True");
    }

    private static object FirstNonZeroEnum(Type type)
    {
        Array values = Enum.GetValues(type);
        for (int index = 0; index < values.Length; index++)
        {
            object value = values.GetValue(index);
            if (Convert.ToInt64(value) != 0)
                return value;
        }
        throw new InvalidOperationException(type.FullName + " has no non-zero value.");
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
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
