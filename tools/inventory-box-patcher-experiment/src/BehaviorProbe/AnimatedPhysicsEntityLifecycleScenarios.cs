using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class AnimatedPhysicsEntityLifecycleScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        Type animated = magicka.GetType(
            "Magicka.GameLogic.Entities.AnimatedPhysicsEntity",
            false);
        Type entity = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            false);
        if (animated == null || entity == null)
        {
            report.AddNotApplicable(
                "animated_physics_lifecycle.deinitialize",
                "AnimatedPhysicsEntity is not present in this version");
            report.AddNotApplicable(
                "animated_physics_lifecycle.final_teardown",
                "AnimatedPhysicsEntity is not present in this version");
            return;
        }

        AnimatedPhysicsEntityLifecycleHarness harness =
            new AnimatedPhysicsEntityLifecycleHarness(animated, entity);
        report.Add(
            "animated_physics_lifecycle.deinitialize",
            harness.Deinitialize());
        report.Add(
            "animated_physics_lifecycle.final_teardown",
            harness.FinalTeardown());
    }
}

internal sealed class AnimatedPhysicsEntityLifecycleHarness
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic;

    private readonly Type animatedType;
    private readonly FieldInfo modelField;
    private readonly FieldInfo controllerField;
    private readonly FieldInfo clipsField;
    private readonly FieldInfo actionsField;
    private readonly FieldInfo animatedRenderDataField;
    private readonly FieldInfo bodyField;
    private readonly FieldInfo liveEffectsField;
    private readonly FieldInfo gibsField;
    private readonly FieldInfo damageableAnimationsField;
    private readonly FieldInfo resistancesField;
    private readonly FieldInfo statusEffectsField;
    private readonly FieldInfo currentStatusField;
    private readonly FieldInfo instancesField;
    private readonly FieldInfo uniqueEntitiesField;
    private readonly FieldInfo damageableCacheField;
    private readonly FieldInfo damageableCacheLockField;
    private readonly MethodInfo deinitialize;
    private readonly MethodInfo clearHandles;
    private readonly EventInfo animationLoopedEvent;
    private readonly EventInfo crossfadeFinishedEvent;
    private readonly FieldInfo animationLoopedField;
    private readonly FieldInfo crossfadeFinishedField;
    private readonly FieldInfo controllerSkeletonField;
    private readonly FieldInfo controllerQueueField;
    private readonly FieldInfo animatedVerticesField;
    private readonly FieldInfo animatedIndicesField;
    private readonly FieldInfo animatedDeclarationField;
    private readonly FieldInfo animatedSkeletonField;

    internal AnimatedPhysicsEntityLifecycleHarness(
        Type animatedType,
        Type entityType)
    {
        this.animatedType = animatedType;
        for (Type current = animatedType;
            current != null;
            current = current.BaseType)
            RuntimeHelpers.RunClassConstructor(current.TypeHandle);

        modelField = RequireField(animatedType, "mModel");
        controllerField = RequireField(animatedType, "mAnimationController");
        clipsField = RequireField(animatedType, "mAnimationClips");
        actionsField = RequireField(animatedType, "mCurrentActions");
        animatedRenderDataField = RequireField(
            animatedType,
            "mAnimatedRenderData");
        bodyField = RequireField(animatedType, "mBody");
        liveEffectsField = RequireField(animatedType, "mLiveEffects");
        gibsField = RequireField(animatedType, "mGibs");
        damageableAnimationsField = RequireField(animatedType, "mAnimations");
        resistancesField = RequireField(animatedType, "mResistances");
        statusEffectsField = RequireField(animatedType, "mStatusEffects");
        currentStatusField = RequireField(
            animatedType,
            "mCurrentStatusEffects");
        instancesField = RequireField(entityType, "mInstances");
        uniqueEntitiesField = RequireField(entityType, "mUniqueEntities");
        Type damageableType = animatedType.BaseType;
        damageableCacheField = FindField(damageableType, "sCache");
        damageableCacheLockField = FindField(damageableType, "sCacheLock");

        deinitialize = animatedType.GetMethod(
            "Deinitialize",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        clearHandles = entityType.GetMethod(
            "ClearHandles",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (deinitialize == null)
            throw new MissingMethodException(animatedType.FullName, "Deinitialize");
        if (clearHandles == null)
            throw new MissingMethodException(entityType.FullName, "ClearHandles");

        Type controllerType = controllerField.FieldType;
        animationLoopedEvent = RequireEvent(
            controllerType,
            "AnimationLooped");
        crossfadeFinishedEvent = RequireEvent(
            controllerType,
            "CrossfadeFinished");
        animationLoopedField = RequireField(
            controllerType,
            "AnimationLooped");
        crossfadeFinishedField = RequireField(
            controllerType,
            "CrossfadeFinished");
        controllerSkeletonField = RequireField(controllerType, "skeleton");
        controllerQueueField = RequireField(
            controllerType,
            "crossFadeAnimationClipQueue");

        Type renderDataType = animatedRenderDataField.FieldType.GetElementType();
        if (renderDataType == null)
            throw new MissingMemberException(
                animatedType.FullName,
                "mAnimatedRenderData element type");
        animatedVerticesField = RequireField(renderDataType, "mVertexBuffer");
        animatedIndicesField = RequireField(renderDataType, "mIndexBuffer");
        animatedDeclarationField = RequireField(
            renderDataType,
            "mVertexDeclaration");
        animatedSkeletonField = RequireField(renderDataType, "mSkeleton");
    }

    internal ScenarioResult Deinitialize()
    {
        ResetLifecycleStatics();
        AnimatedFixture fixture = CreateFixture(false);
        if (damageableCacheField != null)
            ((IList)damageableCacheField.GetValue(null)).Add(fixture.Entity);
        Exception failure = Invoke(deinitialize, fixture.Entity, new object[0]);
        object controller = controllerField.GetValue(fixture.Entity);
        Array renderData = animatedRenderDataField.GetValue(fixture.Entity) as Array;
        bool rebuilt =
            modelField.GetValue(fixture.Entity) == null &&
            clipsField.GetValue(fixture.Entity) == null &&
            controller != null &&
            !ReferenceEquals(controller, fixture.Controller) &&
            HasHandler(animationLoopedField, controller, fixture.Entity) &&
            HasHandler(crossfadeFinishedField, controller, fixture.Entity) &&
            renderData != null &&
            renderData.Length == 3 &&
            AllItemsPresent(renderData) &&
            !ReferenceEquals(renderData, fixture.RenderData);
        return Result(failure, rebuilt, "reusable_state_rebuilt");
    }

    internal ScenarioResult FinalTeardown()
    {
        AnimatedFixture fixture = CreateFixture(true);
        IList instances = instancesField.GetValue(null) as IList;
        if (instances == null)
            throw new InvalidOperationException("Entity handle list is unavailable.");
        instances.Clear();
        instances.Add(fixture.Entity);
        Exception failure = Invoke(clearHandles, null, new object[0]);
        bool released =
            modelField.GetValue(fixture.Entity) == null &&
            controllerField.GetValue(fixture.Entity) == null &&
            clipsField.GetValue(fixture.Entity) == null &&
            actionsField.GetValue(fixture.Entity) == null &&
            animatedRenderDataField.GetValue(fixture.Entity) == null &&
            controllerSkeletonField.GetValue(fixture.Controller) == null &&
            controllerQueueField.GetValue(fixture.Controller) == null &&
            animationLoopedField.GetValue(fixture.Controller) == null &&
            crossfadeFinishedField.GetValue(fixture.Controller) == null &&
            RenderResourcesReleased(fixture.RenderData);
        instances.Clear();
        return Result(failure, released, "references_released");
    }

    private AnimatedFixture CreateFixture(bool includeFinalFields)
    {
        object entity = FormatterServices.GetUninitializedObject(animatedType);
        object controller = Activator.CreateInstance(controllerField.FieldType);
        Subscribe(controller, animationLoopedEvent, entity, "OnAnimationLooped");
        Subscribe(
            controller,
            crossfadeFinishedEvent,
            entity,
            "OnCrossfadeFinished");
        controllerSkeletonField.SetValue(
            controller,
            FormatterServices.GetUninitializedObject(
                controllerSkeletonField.FieldType));
        controllerQueueField.SetValue(
            controller,
            FormatterServices.GetUninitializedObject(
                controllerQueueField.FieldType));

        Array renderData = CreateRenderData(1, true);
        controllerField.SetValue(entity, controller);
        clipsField.SetValue(
            entity,
            Array.CreateInstance(clipsField.FieldType.GetElementType(), 0));
        actionsField.SetValue(
            entity,
            Array.CreateInstance(actionsField.FieldType.GetElementType(), 0));
        animatedRenderDataField.SetValue(entity, renderData);
        bodyField.SetValue(entity, Activator.CreateInstance(bodyField.FieldType));
        liveEffectsField.SetValue(
            entity,
            Activator.CreateInstance(liveEffectsField.FieldType));
        gibsField.SetValue(entity, Activator.CreateInstance(gibsField.FieldType));

        if (includeFinalFields)
        {
            damageableAnimationsField.SetValue(
                entity,
                Activator.CreateInstance(damageableAnimationsField.FieldType));
            resistancesField.SetValue(
                entity,
                Array.CreateInstance(resistancesField.FieldType.GetElementType(), 0));
            statusEffectsField.SetValue(
                entity,
                Array.CreateInstance(statusEffectsField.FieldType.GetElementType(), 0));
            currentStatusField.SetValue(
                entity,
                Activator.CreateInstance(currentStatusField.FieldType));
        }
        return new AnimatedFixture(entity, controller, renderData);
    }

    private void ResetLifecycleStatics()
    {
        if (damageableCacheField == null ||
            damageableCacheLockField == null)
            return;
        object cache = damageableCacheField.GetValue(null);
        if (cache == null)
        {
            cache = Activator.CreateInstance(damageableCacheField.FieldType);
            damageableCacheField.SetValue(null, cache);
        }
        else
        {
            ((IList)cache).Clear();
        }
        if (damageableCacheLockField.GetValue(null) == null)
            damageableCacheLockField.SetValue(null, new object());
        if (uniqueEntitiesField.GetValue(null) == null)
            uniqueEntitiesField.SetValue(
                null,
                Activator.CreateInstance(uniqueEntitiesField.FieldType));
    }

    private Array CreateRenderData(int length, bool includeResources)
    {
        Type elementType = animatedRenderDataField.FieldType.GetElementType();
        Array result = Array.CreateInstance(elementType, length);
        for (int index = 0; index < length; index++)
        {
            object item = FormatterServices.GetUninitializedObject(elementType);
            if (includeResources)
            {
                animatedSkeletonField.SetValue(
                    item,
                    Array.CreateInstance(
                        animatedSkeletonField.FieldType.GetElementType(),
                        1));
            }
            result.SetValue(item, index);
        }
        return result;
    }

    private static void Subscribe(
        object controller,
        EventInfo eventInfo,
        object entity,
        string methodName)
    {
        MethodInfo method = entity.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(entity.GetType().FullName, methodName);
        Delegate handler = Delegate.CreateDelegate(
            eventInfo.EventHandlerType,
            entity,
            method);
        eventInfo.AddEventHandler(controller, handler);
    }

    private bool RenderResourcesReleased(Array renderData)
    {
        for (int index = 0; index < renderData.Length; index++)
        {
            object item = renderData.GetValue(index);
            if (item != null &&
                (animatedVerticesField.GetValue(item) != null ||
                    animatedIndicesField.GetValue(item) != null ||
                    animatedDeclarationField.GetValue(item) != null ||
                    animatedSkeletonField.GetValue(item) != null))
                return false;
        }
        return true;
    }

    private static bool HasHandler(
        FieldInfo eventField,
        object source,
        object expectedTarget)
    {
        Delegate handlers = eventField.GetValue(source) as Delegate;
        if (handlers == null)
            return false;
        Delegate[] invocationList = handlers.GetInvocationList();
        for (int index = 0; index < invocationList.Length; index++)
        {
            if (ReferenceEquals(invocationList[index].Target, expectedTarget))
                return true;
        }
        return false;
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

    private static ScenarioResult Result(
        Exception failure,
        bool state,
        string stateName)
    {
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        string target = failure == null || failure.TargetSite == null
            ? "none"
            : failure.TargetSite.DeclaringType.FullName + "." +
                failure.TargetSite.Name;
        return new ScenarioResult(
            failure == null && state,
            "exception:" + exception + ",target:" + target + "," +
                stateName + ":" + state,
            "exception:none,target:none," + stateName + ":True");
    }

    private static EventInfo RequireEvent(Type type, string name)
    {
        EventInfo eventInfo = type.GetEvent(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (eventInfo == null)
            throw new MissingMemberException(type.FullName, name);
        return eventInfo;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = FindField(type, name);
        if (field != null)
            return field;
        throw new MissingFieldException(type.FullName, name);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                InstanceFields | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
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

    private sealed class AnimatedFixture
    {
        internal object Entity { get; private set; }
        internal object Controller { get; private set; }
        internal Array RenderData { get; private set; }

        internal AnimatedFixture(
            object entity,
            object controller,
            Array renderData)
        {
            Entity = entity;
            Controller = controller;
            RenderData = renderData;
        }
    }
}
