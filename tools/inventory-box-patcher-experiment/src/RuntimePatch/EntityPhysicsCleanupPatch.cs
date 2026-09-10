using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class EntityPhysicsCleanupPatch
    {
        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic;

        private static FieldInfo entityInstancesField;
        private static FieldInfo uniqueEntitiesField;
        private static FieldInfo bodyField;
        private static FieldInfo collisionField;
        private static FieldInfo playStateField;
        private static FieldInfo inboundStampField;
        private static FieldInfo callbackField;
        private static FieldInfo postCollisionCallbackField;
        private static FieldInfo renderDataField;
        private static FieldInfo highlightRenderDataField;
        private static FieldInfo conditionsField;
        private static FieldInfo effectsField;
        private static FieldInfo liveEffectsField;
        private static FieldInfo hitListField;
        private static FieldInfo templateField;
        private static FieldInfo renderVerticesField;
        private static FieldInfo renderIndicesField;
        private static FieldInfo renderDeclarationField;
        private static FieldInfo highlightVerticesField;
        private static FieldInfo highlightIndicesField;
        private static FieldInfo highlightDeclarationField;
        private static FieldInfo highlightMaterialField;
        private static MethodInfo effectManagerInstanceGetter;
        private static MethodInfo effectStopMethod;
        private static Type physicsEntityType;
        private static Type damageablePhysicsEntityType;
        private static FieldInfo damageableStatusEffectsField;
        private static FieldInfo damageableStatusLightField;
        private static FieldInfo damageableGibsField;
        private static FieldInfo damageableAnimationsField;
        private static FieldInfo damageableResistancesField;
        private static FieldInfo damageableCurrentStatusField;
        private static MethodInfo statusEffectStopMethod;
        private static MethodInfo statusLightDisableMethod;
        private static PropertyInfo animationLevelPartProperty;
        private static FieldInfo animationLevelPartField;
        private static Type animatedPhysicsEntityType;
        private static FieldInfo animatedModelField;
        private static FieldInfo animatedControllerField;
        private static FieldInfo animatedClipsField;
        private static FieldInfo animatedActionsField;
        private static FieldInfo animatedRenderDataField;
        private static ConstructorInfo animatedControllerConstructor;
        private static ConstructorInfo animatedRenderDataConstructor;
        private static EventInfo animationLoopedEvent;
        private static EventInfo crossfadeFinishedEvent;
        private static MethodInfo onAnimationLoopedMethod;
        private static MethodInfo onCrossfadeFinishedMethod;
        private static MethodInfo clearCrossfadeQueueMethod;
        private static PropertyInfo controllerSkeletonProperty;
        private static FieldInfo animatedRenderVerticesField;
        private static FieldInfo animatedRenderIndicesField;
        private static FieldInfo animatedRenderDeclarationField;
        private static FieldInfo animatedRenderSkeletonField;
        private static MethodInfo disableBodyMethod;
        private static PropertyInfo bodyCollisionSkinProperty;
        private static FieldInfo bodyTagField;
        private static PropertyInfo skinCollisionsProperty;
        private static PropertyInfo skinNonCollidablesProperty;
        private static PropertyInfo skinTagProperty;
        private static PropertyInfo skinOwnerProperty;
        private static PropertyInfo skinCollisionSystemProperty;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Prefix(
                "PhysicsEntity stale physics replacement cleanup",
                "org.magickacommunitypatch.entity-physics-initialize-cleanup",
                FindInitialize,
                target => typeof(EntityPhysicsCleanupPatch).GetMethod(
                    "InitializePrefix"));

        internal static readonly RuntimePatchDefinition DeinitializeDefinition =
            RuntimePatchDefinition.Postfix(
                "PhysicsEntity deinitialization cleanup",
                "org.magickacommunitypatch.entity-physics-deinitialize-cleanup",
                FindDeinitialize,
                target => typeof(EntityPhysicsCleanupPatch).GetMethod(
                    "DeinitializePostfix"));

        internal static readonly RuntimePatchDefinition ClearHandlesDefinition =
            RuntimePatchDefinition.Prefix(
                "Entity level teardown cleanup",
                "org.magickacommunitypatch.entity-level-teardown-cleanup",
                FindClearHandles,
                target => typeof(EntityPhysicsCleanupPatch).GetMethod(
                    "ClearHandlesPrefix"));

        private static MethodInfo FindInitialize(Assembly targetAssembly)
        {
            Type physicsEntity = ResolveContracts(targetAssembly);
            MethodInfo[] methods = physicsEntity.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            MethodInfo found = null;
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo candidate = methods[index];
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name != "Initialize" ||
                    parameters.Length != 3 ||
                    parameters[0].ParameterType.FullName !=
                        "Magicka.GameLogic.Entities.PhysicsEntityTemplate" ||
                    parameters[2].ParameterType != typeof(int))
                    continue;
                if (found != null)
                    throw new AmbiguousMatchException(
                        physicsEntity.FullName + ".Initialize");
                found = candidate;
            }
            if (found == null || found.ReturnType != typeof(void))
                throw new MissingMethodException(
                    physicsEntity.FullName,
                    "Initialize(PhysicsEntityTemplate, Matrix, Int32)");
            return found;
        }

        private static MethodInfo FindDeinitialize(Assembly targetAssembly)
        {
            Type physicsEntity = ResolveContracts(targetAssembly);
            MethodInfo method = physicsEntity.GetMethod(
                "Deinitialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    physicsEntity.FullName,
                    "Deinitialize");
            return method;
        }

        private static MethodInfo FindClearHandles(Assembly targetAssembly)
        {
            Type physicsEntity = ResolveContracts(targetAssembly);
            Type entity = physicsEntity.BaseType;
            MethodInfo method = entity.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(entity.FullName, "ClearHandles");
            return method;
        }

        private static Type ResolveContracts(Assembly targetAssembly)
        {
            Type entity = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type physicsEntity = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.PhysicsEntity",
                true);
            physicsEntityType = physicsEntity;
            if (physicsEntity.BaseType != entity)
                throw new InvalidOperationException(
                    "PhysicsEntity no longer directly derives from Entity.");

            entityInstancesField = RequireField(
                entity,
                "mInstances",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            uniqueEntitiesField = RequireField(
                entity,
                "mUniqueEntities",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            bodyField = RequireField(entity, "mBody", InstanceFields);
            collisionField = RequireField(entity, "mCollision", InstanceFields);
            playStateField = RequireField(entity, "mPlayState", InstanceFields);
            inboundStampField = RequireField(
                entity,
                "mInBoundUDPStamp",
                InstanceFields);

            Type bodyType = bodyField.FieldType;
            Type collisionType = collisionField.FieldType;
            disableBodyMethod = RequireMethod(
                bodyType,
                "DisableBody",
                Type.EmptyTypes);
            bodyCollisionSkinProperty = RequireProperty(
                bodyType,
                "CollisionSkin");
            bodyTagField = RequireField(bodyType, "Tag", InstanceFields);

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
            callbackField = RequireField(
                collisionType,
                "callbackFn",
                InstanceFields | BindingFlags.DeclaredOnly);
            postCollisionCallbackField = RequireField(
                collisionType,
                "postCollisionCallbackFn",
                InstanceFields | BindingFlags.DeclaredOnly);

            renderDataField = RequireField(
                physicsEntity,
                "mRenderData",
                InstanceFields | BindingFlags.DeclaredOnly);
            highlightRenderDataField = RequireField(
                physicsEntity,
                "mHighlightRenderData",
                InstanceFields | BindingFlags.DeclaredOnly);
            conditionsField = RequireField(
                physicsEntity,
                "mConditions",
                InstanceFields | BindingFlags.DeclaredOnly);
            effectsField = RequireField(
                physicsEntity,
                "mEffects",
                InstanceFields | BindingFlags.DeclaredOnly);
            liveEffectsField = RequireField(
                physicsEntity,
                "mLiveEffects",
                InstanceFields | BindingFlags.DeclaredOnly);
            hitListField = RequireField(
                physicsEntity,
                "mHitList",
                InstanceFields | BindingFlags.DeclaredOnly);
            templateField = RequireField(
                physicsEntity,
                "mTemplate",
                InstanceFields | BindingFlags.DeclaredOnly);

            Type renderDataType = renderDataField.FieldType.GetElementType();
            Type highlightDataType =
                highlightRenderDataField.FieldType.GetElementType();
            if (renderDataType == null || highlightDataType == null)
                throw new MissingMemberException(
                    physicsEntity.FullName,
                    "render data arrays");
            renderVerticesField = RequireField(
                renderDataType,
                "mVertices",
                InstanceFields | BindingFlags.DeclaredOnly);
            renderIndicesField = RequireField(
                renderDataType,
                "mIndices",
                InstanceFields | BindingFlags.DeclaredOnly);
            renderDeclarationField = RequireField(
                renderDataType,
                "mVertexDeclaration",
                InstanceFields | BindingFlags.DeclaredOnly);
            highlightVerticesField = RequireField(
                highlightDataType,
                "mVertexBuffer",
                InstanceFields | BindingFlags.DeclaredOnly);
            highlightIndicesField = RequireField(
                highlightDataType,
                "mIndexBuffer",
                InstanceFields | BindingFlags.DeclaredOnly);
            highlightDeclarationField = RequireField(
                highlightDataType,
                "mVertexDeclaration",
                InstanceFields | BindingFlags.DeclaredOnly);
            highlightMaterialField = RequireField(
                highlightDataType,
                "mMaterial",
                InstanceFields | BindingFlags.DeclaredOnly);

            Type effectManager = targetAssembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);
            PropertyInfo effectManagerInstance = effectManager.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            effectManagerInstanceGetter = effectManagerInstance == null
                ? null
                : effectManagerInstance.GetGetMethod();
            Type[] liveEffectArguments = liveEffectsField.FieldType.IsGenericType
                ? liveEffectsField.FieldType.GetGenericArguments()
                : Type.EmptyTypes;
            if (liveEffectArguments.Length != 1)
                throw new MissingMemberException(
                    physicsEntity.FullName,
                    "mLiveEffects item type");
            effectStopMethod = effectManager.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { liveEffectArguments[0].MakeByRefType() },
                null);
            if (effectManagerInstanceGetter == null ||
                effectManagerInstanceGetter.ReturnType != effectManager ||
                effectStopMethod == null ||
                effectStopMethod.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "PhysicsEntity effect cleanup members are incomplete.");

            damageablePhysicsEntityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.DamageablePhysicsEntity",
                true);
            damageableStatusEffectsField = RequireField(
                damageablePhysicsEntityType,
                "mStatusEffects",
                InstanceFields | BindingFlags.DeclaredOnly);
            damageableStatusLightField = RequireField(
                damageablePhysicsEntityType,
                "mStatusEffectLight",
                InstanceFields | BindingFlags.DeclaredOnly);
            damageableGibsField = RequireField(
                damageablePhysicsEntityType,
                "mGibs",
                InstanceFields | BindingFlags.DeclaredOnly);
            damageableAnimationsField = RequireField(
                damageablePhysicsEntityType,
                "mAnimations",
                InstanceFields | BindingFlags.DeclaredOnly);
            damageableResistancesField = RequireField(
                damageablePhysicsEntityType,
                "mResistances",
                InstanceFields | BindingFlags.DeclaredOnly);
            damageableCurrentStatusField = RequireField(
                damageablePhysicsEntityType,
                "mCurrentStatusEffects",
                InstanceFields | BindingFlags.DeclaredOnly);
            Type statusEffectType =
                damageableStatusEffectsField.FieldType.GetElementType();
            statusEffectStopMethod = statusEffectType == null
                ? null
                : statusEffectType.GetMethod(
                    "Stop",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
            statusLightDisableMethod = damageableStatusLightField.FieldType.GetMethod(
                "Disable",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            Type[] animationArguments =
                damageableAnimationsField.FieldType.IsGenericType
                    ? damageableAnimationsField.FieldType.GetGenericArguments()
                    : Type.EmptyTypes;
            Type animationType = animationArguments.Length == 1
                ? animationArguments[0]
                : null;
            animationLevelPartProperty = animationType == null
                ? null
                : animationType.GetProperty(
                    "AnimatedLevelPart",
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic);
            animationLevelPartField = animationType == null
                ? null
                : animationType.GetField(
                    "AnimatedLevelPart",
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic);
            if (!damageableStatusEffectsField.FieldType.IsArray ||
                statusEffectStopMethod == null ||
                statusEffectStopMethod.ReturnType != typeof(void) ||
                statusLightDisableMethod == null ||
                statusLightDisableMethod.ReturnType != typeof(void) ||
                animationType == null ||
                ((animationLevelPartProperty == null ||
                    !animationLevelPartProperty.CanWrite) &&
                    animationLevelPartField == null) ||
                !damageableCurrentStatusField.FieldType.IsEnum)
                throw new MissingMemberException(
                    "DamageablePhysicsEntity final cleanup members are incomplete.");

            animatedPhysicsEntityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.AnimatedPhysicsEntity",
                true);
            if (animatedPhysicsEntityType.BaseType != damageablePhysicsEntityType)
                throw new InvalidOperationException(
                    "AnimatedPhysicsEntity no longer derives from DamageablePhysicsEntity.");
            animatedModelField = RequireField(
                animatedPhysicsEntityType,
                "mModel",
                InstanceFields | BindingFlags.DeclaredOnly);
            animatedControllerField = RequireField(
                animatedPhysicsEntityType,
                "mAnimationController",
                InstanceFields | BindingFlags.DeclaredOnly);
            animatedClipsField = RequireField(
                animatedPhysicsEntityType,
                "mAnimationClips",
                InstanceFields | BindingFlags.DeclaredOnly);
            animatedActionsField = RequireField(
                animatedPhysicsEntityType,
                "mCurrentActions",
                InstanceFields | BindingFlags.DeclaredOnly);
            animatedRenderDataField = RequireField(
                animatedPhysicsEntityType,
                "mAnimatedRenderData",
                InstanceFields | BindingFlags.DeclaredOnly);

            Type controllerType = animatedControllerField.FieldType;
            animatedControllerConstructor = controllerType.GetConstructor(
                Type.EmptyTypes);
            animationLoopedEvent = controllerType.GetEvent(
                "AnimationLooped",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            crossfadeFinishedEvent = controllerType.GetEvent(
                "CrossfadeFinished",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            clearCrossfadeQueueMethod = controllerType.GetMethod(
                "ClearCrossfadeQueue",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            controllerSkeletonProperty = controllerType.GetProperty(
                "Skeleton",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            onAnimationLoopedMethod = animatedPhysicsEntityType.GetMethod(
                "OnAnimationLooped",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            onCrossfadeFinishedMethod = animatedPhysicsEntityType.GetMethod(
                "OnCrossfadeFinished",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            Type animatedRenderDataType =
                animatedRenderDataField.FieldType.GetElementType();
            animatedRenderDataConstructor = animatedRenderDataType == null
                ? null
                : animatedRenderDataType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic,
                    null,
                    new Type[] { animatedPhysicsEntityType },
                    null);
            animatedRenderVerticesField = animatedRenderDataType == null
                ? null
                : animatedRenderDataType.GetField(
                    "mVertexBuffer",
                    InstanceFields | BindingFlags.DeclaredOnly);
            animatedRenderIndicesField = animatedRenderDataType == null
                ? null
                : animatedRenderDataType.GetField(
                    "mIndexBuffer",
                    InstanceFields | BindingFlags.DeclaredOnly);
            animatedRenderDeclarationField = animatedRenderDataType == null
                ? null
                : animatedRenderDataType.GetField(
                    "mVertexDeclaration",
                    InstanceFields | BindingFlags.DeclaredOnly);
            animatedRenderSkeletonField = animatedRenderDataType == null
                ? null
                : animatedRenderDataType.GetField(
                    "mSkeleton",
                    InstanceFields | BindingFlags.DeclaredOnly);
            if (animatedControllerConstructor == null ||
                animationLoopedEvent == null ||
                crossfadeFinishedEvent == null ||
                onAnimationLoopedMethod == null ||
                onCrossfadeFinishedMethod == null ||
                clearCrossfadeQueueMethod == null ||
                clearCrossfadeQueueMethod.ReturnType != typeof(void) ||
                controllerSkeletonProperty == null ||
                !controllerSkeletonProperty.CanWrite ||
                animatedRenderDataType == null ||
                animatedRenderDataConstructor == null ||
                animatedRenderVerticesField == null ||
                animatedRenderIndicesField == null ||
                animatedRenderDeclarationField == null ||
                animatedRenderSkeletonField == null)
                throw new MissingMemberException(
                    "AnimatedPhysicsEntity lifecycle cleanup members are incomplete.");
            return physicsEntity;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (property == null || !property.CanRead)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        public static void InitializePrefix(object __instance)
        {
            DetachEntity(__instance);
        }

        public static void DeinitializePostfix(object __instance)
        {
            DetachEntity(__instance);
            ResetPhysicsEntityReferences(__instance);
            if (animatedPhysicsEntityType.IsInstanceOfType(__instance))
                ResetAnimatedPhysicsEntityReferences(__instance);
        }

        public static void ClearHandlesPrefix()
        {
            IList entities = entityInstancesField.GetValue(null) as IList;
            if (entities != null)
            {
                object[] snapshot = new object[entities.Count];
                entities.CopyTo(snapshot, 0);
                for (int index = 0; index < snapshot.Length; index++)
                {
                    object entity = snapshot[index];
                    if (entity == null)
                        continue;
                    if (animatedPhysicsEntityType.IsInstanceOfType(entity))
                        CleanupFinalAnimatedPhysicsEntity(entity);
                    if (damageablePhysicsEntityType.IsInstanceOfType(entity))
                        CleanupFinalDamageableEntity(entity);
                    if (physicsEntityType.IsInstanceOfType(entity))
                        CleanupFinalPhysicsEntity(entity);
                    DetachEntity(entity);
                    TryClear(inboundStampField.GetValue(entity));
                    TrySet(playStateField, entity, null);
                }
            }
            TryClear(uniqueEntitiesField.GetValue(null));
        }

        private static void CleanupFinalDamageableEntity(object entity)
        {
            Array statusEffects =
                damageableStatusEffectsField.GetValue(entity) as Array;
            if (statusEffects != null)
            {
                for (int index = 0; index < statusEffects.Length; index++)
                {
                    object statusEffect = statusEffects.GetValue(index);
                    if (statusEffect == null)
                        continue;
                    try
                    {
                        statusEffectStopMethod.Invoke(statusEffect, null);
                    }
                    catch
                    {
                    }
                }
            }
            TrySet(damageableStatusEffectsField, entity, null);

            object statusLight = damageableStatusLightField.GetValue(entity);
            if (statusLight != null)
                TryInvoke(statusLightDisableMethod, statusLight);
            TrySet(damageableStatusLightField, entity, null);

            TryClear(damageableGibsField.GetValue(entity));
            TrySet(damageableGibsField, entity, null);
            ClearDamageableAnimations(entity);
            TrySet(damageableResistancesField, entity, null);
            try
            {
                damageableCurrentStatusField.SetValue(
                    entity,
                    Activator.CreateInstance(
                        damageableCurrentStatusField.FieldType));
            }
            catch
            {
            }
        }

        private static void ResetAnimatedPhysicsEntityReferences(object entity)
        {
            animatedClipsField.SetValue(entity, null);
            animatedModelField.SetValue(entity, null);

            object controller = animatedControllerConstructor.Invoke(null);
            animationLoopedEvent.AddEventHandler(
                controller,
                Delegate.CreateDelegate(
                    animationLoopedEvent.EventHandlerType,
                    entity,
                    onAnimationLoopedMethod));
            crossfadeFinishedEvent.AddEventHandler(
                controller,
                Delegate.CreateDelegate(
                    crossfadeFinishedEvent.EventHandlerType,
                    entity,
                    onCrossfadeFinishedMethod));
            animatedControllerField.SetValue(entity, controller);

            Array renderData = Array.CreateInstance(
                animatedRenderDataField.FieldType.GetElementType(),
                3);
            for (int index = 0; index < renderData.Length; index++)
                renderData.SetValue(
                    animatedRenderDataConstructor.Invoke(
                        new object[] { entity }),
                    index);
            animatedRenderDataField.SetValue(entity, renderData);
        }

        private static void CleanupFinalAnimatedPhysicsEntity(object entity)
        {
            object controller = animatedControllerField.GetValue(entity);
            if (controller != null)
            {
                TryRemoveEvent(
                    animationLoopedEvent,
                    controller,
                    entity,
                    onAnimationLoopedMethod);
                TryRemoveEvent(
                    crossfadeFinishedEvent,
                    controller,
                    entity,
                    onCrossfadeFinishedMethod);
                TryInvoke(clearCrossfadeQueueMethod, controller);
                try
                {
                    controllerSkeletonProperty.SetValue(
                        controller,
                        null,
                        null);
                }
                catch
                {
                }
            }
            TrySet(animatedControllerField, entity, null);
            TrySet(animatedClipsField, entity, null);
            TrySet(animatedActionsField, entity, null);
            TrySet(animatedModelField, entity, null);

            Array renderData =
                animatedRenderDataField.GetValue(entity) as Array;
            if (renderData != null)
            {
                for (int index = 0; index < renderData.Length; index++)
                {
                    object item = renderData.GetValue(index);
                    if (item == null)
                        continue;
                    TrySet(animatedRenderVerticesField, item, null);
                    TrySet(animatedRenderIndicesField, item, null);
                    TrySet(animatedRenderDeclarationField, item, null);
                    TrySet(animatedRenderSkeletonField, item, null);
                }
            }
            TrySet(animatedRenderDataField, entity, null);
        }

        private static void TryRemoveEvent(
            EventInfo eventInfo,
            object source,
            object target,
            MethodInfo method)
        {
            try
            {
                eventInfo.RemoveEventHandler(
                    source,
                    Delegate.CreateDelegate(
                        eventInfo.EventHandlerType,
                        target,
                        method));
            }
            catch
            {
            }
        }

        private static void ClearDamageableAnimations(object entity)
        {
            IList animations =
                damageableAnimationsField.GetValue(entity) as IList;
            if (animations != null)
            {
                for (int index = 0; index < animations.Count; index++)
                {
                    object animation = animations[index];
                    if (animation == null)
                        continue;
                    try
                    {
                        if (animationLevelPartProperty != null &&
                            animationLevelPartProperty.CanWrite)
                            animationLevelPartProperty.SetValue(
                                animation,
                                null,
                                null);
                        else
                            animationLevelPartField.SetValue(animation, null);
                        animations[index] = animation;
                    }
                    catch
                    {
                    }
                }
                TryClear(animations);
            }
            TrySet(damageableAnimationsField, entity, null);
        }

        private static void CleanupFinalPhysicsEntity(object entity)
        {
            object liveEffects = liveEffectsField.GetValue(entity);
            IList effects = liveEffects as IList;
            if (effects != null)
            {
                for (int index = 0; index < effects.Count; index++)
                {
                    object[] arguments = new object[] { effects[index] };
                    try
                    {
                        object manager = effectManagerInstanceGetter.Invoke(null, null);
                        effectStopMethod.Invoke(manager, arguments);
                    }
                    catch
                    {
                    }
                }
                TryClear(effects);
            }
            TrySet(liveEffectsField, entity, null);

            ClearRenderData(renderDataField.GetValue(entity));
            TrySet(renderDataField, entity, null);
            ClearHighlightRenderData(highlightRenderDataField.GetValue(entity));
            TrySet(highlightRenderDataField, entity, null);
            TryClear(hitListField.GetValue(entity));
            TrySet(hitListField, entity, null);
            TrySet(conditionsField, entity, null);
            TrySet(effectsField, entity, null);
            TrySet(templateField, entity, null);
        }

        private static void ClearRenderData(object value)
        {
            Array entries = value as Array;
            if (entries == null)
                return;
            for (int index = 0; index < entries.Length; index++)
            {
                object entry = entries.GetValue(index);
                if (entry == null)
                    continue;
                TrySet(renderVerticesField, entry, null);
                TrySet(renderIndicesField, entry, null);
                TrySet(renderDeclarationField, entry, null);
            }
        }

        private static void ClearHighlightRenderData(object value)
        {
            Array entries = value as Array;
            if (entries == null)
                return;
            for (int index = 0; index < entries.Length; index++)
            {
                object entry = entries.GetValue(index);
                if (entry == null)
                    continue;
                TrySet(highlightVerticesField, entry, null);
                TrySet(highlightIndicesField, entry, null);
                TrySet(highlightDeclarationField, entry, null);
                try
                {
                    highlightMaterialField.SetValue(
                        entry,
                        Activator.CreateInstance(highlightMaterialField.FieldType));
                }
                catch
                {
                }
            }
        }

        public static void DetachEntity(object entity)
        {
            if (entity == null || bodyField == null || collisionField == null)
                return;

            ItemWeaponCachePatch.RemoveIfCurrent(entity);

            object body = bodyField.GetValue(entity);
            object collision = collisionField.GetValue(entity);
            if (body != null)
            {
                TryInvoke(disableBodyMethod, body);
                object attachedSkin = TryGet(bodyCollisionSkinProperty, body);
                if (Object.ReferenceEquals(attachedSkin, collision))
                    TrySet(bodyCollisionSkinProperty, body, null);
                TrySet(bodyTagField, body, null);
            }
            if (collision != null)
            {
                TrySet(callbackField, collision, null);
                TrySet(postCollisionCallbackField, collision, null);
                TryClear(TryGet(skinCollisionsProperty, collision));
                TryClear(TryGet(skinNonCollidablesProperty, collision));
                TrySet(skinTagProperty, collision, null);
                TrySet(skinOwnerProperty, collision, null);
                TrySet(skinCollisionSystemProperty, collision, null);
            }
            TrySet(bodyField, entity, null);
            TrySet(collisionField, entity, null);
        }

        private static void ResetPhysicsEntityReferences(object entity)
        {
            TrySet(renderDataField, entity, CreateThreeElementArray(renderDataField));
            TrySet(
                highlightRenderDataField,
                entity,
                CreateThreeElementArray(highlightRenderDataField));
            TrySet(conditionsField, entity, null);
            TrySet(effectsField, entity, null);
        }

        private static Array CreateThreeElementArray(FieldInfo field)
        {
            try
            {
                Type elementType = field.FieldType.GetElementType();
                Array array = Array.CreateInstance(elementType, 3);
                for (int index = 0; index < array.Length; index++)
                    array.SetValue(Activator.CreateInstance(elementType, true), index);
                return array;
            }
            catch
            {
                return null;
            }
        }

        private static object TryGet(PropertyInfo property, object target)
        {
            try
            {
                return property.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static void TryInvoke(MethodInfo method, object target)
        {
            try
            {
                method.Invoke(target, null);
            }
            catch
            {
            }
        }

        private static void TryClear(object collection)
        {
            try
            {
                IList list = collection as IList;
                if (list != null)
                {
                    list.Clear();
                    return;
                }
                IDictionary dictionary = collection as IDictionary;
                if (dictionary != null)
                    dictionary.Clear();
            }
            catch
            {
            }
        }

        private static void TrySet(
            FieldInfo field,
            object target,
            object value)
        {
            try
            {
                field.SetValue(target, value);
            }
            catch
            {
            }
        }

        private static void TrySet(
            PropertyInfo property,
            object target,
            object value)
        {
            try
            {
                if (property.CanWrite)
                    property.SetValue(target, value, null);
            }
            catch
            {
            }
        }
    }
}
