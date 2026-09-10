using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class EntityPhysicsCleanupScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        EntityPhysicsCleanupHarness harness =
            new EntityPhysicsCleanupHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "entity_physics_cleanup.deinitialize",
            harness.Deinitialize());
        report.Add(
            "entity_physics_cleanup.reuse_fallback",
            harness.ReuseFallback());
        report.Add(
            "entity_physics_cleanup.final_teardown",
            harness.FinalTeardown());
        report.Add(
            "entity_physics_cleanup.handle_storage_reset",
            harness.HandleStorageReset());
    }
}

internal sealed class EntityPhysicsCleanupHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly Type entityType;
    private readonly Type physicsEntityType;
    private readonly Type bodyType;
    private readonly Type collisionType;
    private readonly FieldInfo bodyField;
    private readonly FieldInfo collisionField;
    private readonly FieldInfo callbackField;
    private readonly FieldInfo postCollisionCallbackField;
    private readonly FieldInfo renderDataField;
    private readonly FieldInfo highlightRenderDataField;
    private readonly FieldInfo conditionsField;
    private readonly FieldInfo effectsField;
    private readonly FieldInfo liveEffectsField;
    private readonly FieldInfo hitListField;
    private readonly FieldInfo templateField;
    private readonly FieldInfo instancesField;
    private readonly PropertyInfo bodyCollisionSkinProperty;
    private readonly FieldInfo bodyTagField;
    private readonly PropertyInfo skinCollisionsProperty;
    private readonly PropertyInfo skinNonCollidablesProperty;
    private readonly PropertyInfo skinTagProperty;
    private readonly PropertyInfo skinOwnerProperty;
    private readonly PropertyInfo skinCollisionSystemProperty;
    private readonly MethodInfo deinitialize;
    private readonly MethodInfo manualDetach;
    private readonly MethodInfo runtimeDetach;
    private readonly MethodInfo clearHandles;

    internal EntityPhysicsCleanupHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        physicsEntityType = magicka.GetType(
            "Magicka.GameLogic.Entities.PhysicsEntity",
            true);
        RuntimeHelpers.RunClassConstructor(entityType.TypeHandle);

        bodyField = RequireField(entityType, "mBody");
        collisionField = RequireField(entityType, "mCollision");
        bodyType = bodyField.FieldType;
        collisionType = collisionField.FieldType;
        callbackField = RequireField(collisionType, "callbackFn");
        postCollisionCallbackField = RequireField(
            collisionType,
            "postCollisionCallbackFn");
        renderDataField = RequireField(physicsEntityType, "mRenderData");
        highlightRenderDataField = RequireField(
            physicsEntityType,
            "mHighlightRenderData");
        conditionsField = RequireField(physicsEntityType, "mConditions");
        effectsField = RequireField(physicsEntityType, "mEffects");
        liveEffectsField = RequireField(
            physicsEntityType,
            "mLiveEffects");
        hitListField = RequireField(physicsEntityType, "mHitList");
        templateField = RequireField(physicsEntityType, "mTemplate");
        instancesField = RequireField(entityType, "mInstances");

        bodyCollisionSkinProperty = RequireProperty(
            bodyType,
            "CollisionSkin");
        bodyTagField = RequireField(bodyType, "Tag");
        skinCollisionsProperty = RequireProperty(
            collisionType,
            "Collisions");
        skinNonCollidablesProperty = RequireProperty(
            collisionType,
            "NonCollidables");
        skinTagProperty = RequireProperty(collisionType, "Tag");
        skinOwnerProperty = RequireProperty(collisionType, "Owner");
        skinCollisionSystemProperty = RequireProperty(
            collisionType,
            "CollisionSystem");

        deinitialize = physicsEntityType.GetMethod(
            "Deinitialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (deinitialize == null)
            throw new MissingMethodException(
                physicsEntityType.FullName,
                "Deinitialize");

        manualDetach = entityType.GetMethod(
            "DetachPhysicsReferences",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        Type runtimeType = typeof(
            Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.EntityPhysicsCleanupPatch",
                true);
        runtimeDetach = runtimeType.GetMethod(
            "DetachEntity",
            BindingFlags.Static | BindingFlags.Public);
        clearHandles = entityType.GetMethod(
            "ClearHandles",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (clearHandles == null)
            throw new MissingMethodException(entityType.FullName, "ClearHandles");
    }

    internal ScenarioResult Deinitialize()
    {
        PhysicsFixture fixture = CreateFixture();
        Exception failure = Invoke(deinitialize, fixture.Entity, new object[0]);
        string actual = Describe(fixture, failure, true);
        return new ScenarioResult(
            failure == null && IsDetached(fixture) &&
                HasResetPhysicsArrays(fixture.Entity),
            actual,
            Expected(true));
    }

    internal ScenarioResult ReuseFallback()
    {
        PhysicsFixture fixture = CreateFixture();
        Exception failure = null;
        if (manualDetach != null)
            failure = Invoke(manualDetach, fixture.Entity, new object[0]);
        else if (runtimePatchEnabled)
            failure = Invoke(
                runtimeDetach,
                null,
                new object[] { fixture.Entity });
        string actual = Describe(fixture, failure, false);
        return new ScenarioResult(
            failure == null && IsDetached(fixture),
            actual,
            Expected(false));
    }

    internal ScenarioResult FinalTeardown()
    {
        PhysicsFixture fixture = CreateFixture();
        IList instances = instancesField.GetValue(null) as IList;
        if (instances == null)
            throw new InvalidOperationException("Entity handle list is unavailable.");
        instances.Clear();
        instances.Add(fixture.Entity);
        Exception failure = Invoke(clearHandles, null, new object[0]);
        bool released =
            liveEffectsField.GetValue(fixture.Entity) == null &&
            renderDataField.GetValue(fixture.Entity) == null &&
            highlightRenderDataField.GetValue(fixture.Entity) == null &&
            hitListField.GetValue(fixture.Entity) == null &&
            conditionsField.GetValue(fixture.Entity) == null &&
            effectsField.GetValue(fixture.Entity) == null &&
            templateField.GetValue(fixture.Entity) == null;
        instances.Clear();
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        return new ScenarioResult(
            failure == null && released,
            "exception:" + exception + ",references_released:" + released,
            "exception:none,references_released:True");
    }

    internal ScenarioResult HandleStorageReset()
    {
        object before = instancesField.GetValue(null);
        MethodInfo disposeCache = entityType.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        Exception failure = Invoke(
            disposeCache ?? clearHandles,
            null,
            new object[0]);
        object after = instancesField.GetValue(null);
        bool replaced = !Object.ReferenceEquals(before, after);
        int count = after == null ? -1 : ((IList)after).Count;
        return new ScenarioResult(
            failure == null && replaced && count == 0,
            "exception:" + (failure == null ? "none" : failure.GetType().FullName) +
                ",replaced:" + replaced + ",count:" + count,
            "exception:none,replaced:True,count:0");
    }

    private PhysicsFixture CreateFixture()
    {
        object entity = FormatterServices.GetUninitializedObject(
            physicsEntityType);
        object body = Activator.CreateInstance(bodyType);
        ConstructorInfo skinConstructor = collisionType.GetConstructor(
            new Type[] { bodyType });
        if (skinConstructor == null)
            throw new MissingMethodException(
                collisionType.FullName,
                ".ctor(Body)");
        object skin = skinConstructor.Invoke(new object[] { body });

        bodyCollisionSkinProperty.SetValue(body, skin, null);
        bodyTagField.SetValue(body, entity);
        skinTagProperty.SetValue(skin, entity, null);
        Type collisionSystemType = collisionType.Assembly.GetType(
            "JigLibX.Collision.CollisionSystemSAP",
            true);
        skinCollisionSystemProperty.SetValue(
            skin,
            FormatterServices.GetUninitializedObject(collisionSystemType),
            null);
        ((IList)skinCollisionsProperty.GetValue(skin, null)).Add(null);
        ((IList)skinNonCollidablesProperty.GetValue(skin, null)).Add(skin);
        callbackField.SetValue(
            skin,
            CreateDelegate(callbackField.FieldType));
        postCollisionCallbackField.SetValue(
            skin,
            CreateDelegate(postCollisionCallbackField.FieldType));

        bodyField.SetValue(entity, body);
        collisionField.SetValue(entity, skin);
        liveEffectsField.SetValue(
            entity,
            Activator.CreateInstance(liveEffectsField.FieldType));
        ConstructorInfo hitListConstructor = hitListField.FieldType.GetConstructor(
            new Type[] { typeof(int) });
        if (hitListConstructor == null)
            throw new MissingMethodException(hitListField.FieldType.FullName, ".ctor(Int32)");
        hitListField.SetValue(
            entity,
            hitListConstructor.Invoke(new object[] { 4 }));
        templateField.SetValue(
            entity,
            FormatterServices.GetUninitializedObject(templateField.FieldType));
        renderDataField.SetValue(entity, CreateArray(renderDataField, 1));
        highlightRenderDataField.SetValue(
            entity,
            CreateArray(highlightRenderDataField, 1));
        conditionsField.SetValue(entity, CreateValue(conditionsField.FieldType));
        effectsField.SetValue(entity, CreateValue(effectsField.FieldType));
        return new PhysicsFixture(entity, body, skin);
    }

    private bool IsDetached(PhysicsFixture fixture)
    {
        IList collisions = (IList)skinCollisionsProperty.GetValue(
            fixture.Skin,
            null);
        IList nonCollidables = (IList)skinNonCollidablesProperty.GetValue(
            fixture.Skin,
            null);
        return bodyField.GetValue(fixture.Entity) == null &&
            collisionField.GetValue(fixture.Entity) == null &&
            bodyCollisionSkinProperty.GetValue(fixture.Body, null) == null &&
            bodyTagField.GetValue(fixture.Body) == null &&
            callbackField.GetValue(fixture.Skin) == null &&
            postCollisionCallbackField.GetValue(fixture.Skin) == null &&
            collisions.Count == 0 && nonCollidables.Count == 0 &&
            skinTagProperty.GetValue(fixture.Skin, null) == null &&
            skinOwnerProperty.GetValue(fixture.Skin, null) == null &&
            skinCollisionSystemProperty.GetValue(fixture.Skin, null) == null;
    }

    private bool HasResetPhysicsArrays(object entity)
    {
        Array renderData = renderDataField.GetValue(entity) as Array;
        Array highlight = highlightRenderDataField.GetValue(entity) as Array;
        return renderData != null && renderData.Length == 3 &&
            AllItemsPresent(renderData) &&
            highlight != null && highlight.Length == 3 &&
            AllItemsPresent(highlight) &&
            conditionsField.GetValue(entity) == null &&
            effectsField.GetValue(entity) == null;
    }

    private static bool AllItemsPresent(Array array)
    {
        for (int index = 0; index < array.Length; index++)
        {
            if (array.GetValue(index) == null)
                return false;
        }
        return true;
    }

    private string Describe(
        PhysicsFixture fixture,
        Exception failure,
        bool includeArrays)
    {
        string result = "exception:" + ExceptionName(failure) +
            ",detached:" + IsDetached(fixture);
        if (includeArrays)
            result += ",arrays_reset:" +
                HasResetPhysicsArrays(fixture.Entity);
        return result;
    }

    private static string Expected(bool includeArrays)
    {
        string result = "exception:none,detached:True";
        if (includeArrays)
            result += ",arrays_reset:True";
        return result;
    }

    private static object CreateValue(Type type)
    {
        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType(), 1);
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return FormatterServices.GetUninitializedObject(type);
    }

    private static Array CreateArray(FieldInfo field, int length)
    {
        Type elementType = field.FieldType.GetElementType();
        Array result = Array.CreateInstance(elementType, length);
        for (int index = 0; index < length; index++)
            result.SetValue(CreateValue(elementType), index);
        return result;
    }

    private static Delegate CreateDelegate(Type delegateType)
    {
        MethodInfo invoke = delegateType.GetMethod("Invoke");
        ParameterInfo[] parameters = invoke.GetParameters();
        Type[] parameterTypes = new Type[parameters.Length];
        for (int index = 0; index < parameters.Length; index++)
            parameterTypes[index] = parameters[index].ParameterType;
        DynamicMethod method = new DynamicMethod(
            "EntityPhysicsCleanupCallback",
            invoke.ReturnType,
            parameterTypes,
            typeof(EntityPhysicsCleanupHarness).Module,
            true);
        ILGenerator il = method.GetILGenerator();
        if (invoke.ReturnType == typeof(bool))
            il.Emit(OpCodes.Ldc_I4_1);
        else if (invoke.ReturnType != typeof(void))
            throw new NotSupportedException(
                "Unsupported callback return type: " +
                invoke.ReturnType.FullName);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate(delegateType);
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

    private static PropertyInfo RequireProperty(Type type, string name)
    {
        PropertyInfo property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (property == null)
            throw new MissingMemberException(type.FullName, name);
        return property;
    }

    private static string ExceptionName(Exception exception)
    {
        return exception == null ? "none" : exception.GetType().FullName;
    }

    private sealed class PhysicsFixture
    {
        internal object Entity { get; private set; }
        internal object Body { get; private set; }
        internal object Skin { get; private set; }

        internal PhysicsFixture(object entity, object body, object skin)
        {
            Entity = entity;
            Body = body;
            Skin = skin;
        }
    }
}
