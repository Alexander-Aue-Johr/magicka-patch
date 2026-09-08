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
                    DetachEntity(entity);
                    TryClear(inboundStampField.GetValue(entity));
                    TrySet(playStateField, entity, null);
                }
            }
            TryClear(uniqueEntitiesField.GetValue(null));
        }

        public static void DetachEntity(object entity)
        {
            if (entity == null || bodyField == null || collisionField == null)
                return;

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
