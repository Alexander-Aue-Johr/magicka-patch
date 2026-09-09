using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class LevelModelTeardownPatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static FieldInfo collisionSkinField;
        private static FieldInfo lightsField;
        private static FieldInfo animatedPartsField;
        private static FieldInfo watersField;
        private static FieldInfo forceFieldsField;
        private static FieldInfo modelField;
        private static FieldInfo cameraMeshField;
        private static FieldInfo navMeshField;
        private static FieldInfo triggerAreasField;
        private static FieldInfo locatorsField;
        private static FieldInfo effectsField;
        private static FieldInfo physicsEntitiesField;
        private static PropertyInfo skinTagProperty;
        private static PropertyInfo skinCollisionSystemProperty;
        private static PropertyInfo collisionSkinsProperty;
        private static MethodInfo removeCollisionSkinMethod;
        private static MethodInfo disposeShadowMapMethod;
        private static MethodInfo disposeAnimatedPartMethod;
        private static MethodInfo disposeModelMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "LevelModel complete teardown",
                "org.magickacommunitypatch.level-model-teardown",
                FindDispose,
                target => typeof(LevelModelTeardownPatch).GetMethod("Prefix"));

        private static MethodInfo FindDispose(Assembly targetAssembly)
        {
            Type type = targetAssembly.GetType(
                "Magicka.Levels.LevelModel",
                true);
            collisionSkinField = RequireField(type, "mCollisionSkin");
            lightsField = RequireField(type, "mLights");
            animatedPartsField = RequireField(type, "mAnimatedLevelParts");
            watersField = RequireField(type, "mWaters");
            forceFieldsField = RequireField(type, "mForceFields");
            modelField = RequireField(type, "mModel");
            cameraMeshField = RequireField(type, "mCameraMesh");
            navMeshField = RequireField(type, "mNavMesh");
            triggerAreasField = RequireField(type, "mTriggerAreas");
            locatorsField = RequireField(type, "mLocators");
            effectsField = RequireField(type, "mEffects");
            physicsEntitiesField = RequireField(type, "mPhysEntities");

            Type skinType = collisionSkinField.FieldType;
            skinTagProperty = RequireProperty(skinType, "Tag");
            skinCollisionSystemProperty = RequireProperty(
                skinType,
                "CollisionSystem");
            Type collisionSystemType =
                skinCollisionSystemProperty.PropertyType;
            collisionSkinsProperty = RequireProperty(
                collisionSystemType,
                "CollisionSkins");
            removeCollisionSkinMethod = RequireMethod(
                collisionSystemType,
                "RemoveCollisionSkin",
                new Type[] { skinType },
                typeof(bool));

            disposeShadowMapMethod = RequireMethod(
                DictionaryValueType(lightsField),
                "DisposeShadowMap",
                Type.EmptyTypes,
                typeof(void));
            disposeAnimatedPartMethod = RequireMethod(
                DictionaryValueType(animatedPartsField),
                "Dispose",
                Type.EmptyTypes,
                typeof(void));
            ArrayElementType(watersField);
            disposeModelMethod = RequireMethod(
                modelField.FieldType,
                "Dispose",
                Type.EmptyTypes,
                typeof(void));

            MethodInfo dispose = type.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, "Dispose");
            return dispose;
        }

        public static bool Prefix(object __instance)
        {
            if (__instance != null)
                Cleanup(__instance);
            return false;
        }

        public static void Cleanup(object levelModel)
        {
            ReleaseCollisionSkin(levelModel);
            DisposeDictionary(levelModel, lightsField, disposeShadowMapMethod);
            DisposeDictionary(
                levelModel,
                animatedPartsField,
                disposeAnimatedPartMethod);
            DisposeWaters(levelModel);
            DisposeForceFields(levelModel);
            DisposeField(levelModel, modelField, disposeModelMethod);
            cameraMeshField.SetValue(levelModel, null);
            navMeshField.SetValue(levelModel, null);
            ClearCollection(levelModel, triggerAreasField);
            ClearCollection(levelModel, locatorsField);
            effectsField.SetValue(levelModel, null);
            physicsEntitiesField.SetValue(levelModel, null);
            GC.SuppressFinalize(levelModel);
        }

        private static void ReleaseCollisionSkin(object levelModel)
        {
            object skin = collisionSkinField.GetValue(levelModel);
            if (skin == null)
                return;
            try
            {
                object collisionSystem = skinCollisionSystemProperty.GetValue(
                    skin,
                    null);
                if (collisionSystem != null &&
                    Contains(
                        collisionSkinsProperty.GetValue(collisionSystem, null),
                        skin))
                    removeCollisionSkinMethod.Invoke(
                        collisionSystem,
                        new object[] { skin });
            }
            catch
            {
            }
            skinTagProperty.SetValue(skin, null, null);
            collisionSkinField.SetValue(levelModel, null);
        }

        private static void DisposeDictionary(
            object owner,
            FieldInfo field,
            MethodInfo dispose)
        {
            IDictionary dictionary = field.GetValue(owner) as IDictionary;
            if (dictionary == null)
            {
                field.SetValue(owner, null);
                return;
            }
            foreach (object value in dictionary.Values)
            {
                if (value != null)
                    dispose.Invoke(value, null);
            }
            dictionary.Clear();
            field.SetValue(owner, null);
        }

        private static void DisposeWaters(object owner)
        {
            Array values = watersField.GetValue(owner) as Array;
            if (values != null)
            {
                for (int index = 0; index < values.Length; index++)
                {
                    object value = values.GetValue(index);
                    if (value != null)
                        AnimatedLevelPartDisposePatch
                            .DisposeStandaloneLiquid(value);
                    values.SetValue(null, index);
                }
            }
            watersField.SetValue(owner, null);
        }

        private static void DisposeForceFields(object owner)
        {
            Array values = forceFieldsField.GetValue(owner) as Array;
            if (values != null)
            {
                for (int index = 0; index < values.Length; index++)
                {
                    object forceField = values.GetValue(index);
                    if (forceField != null)
                        ForceFieldLifecyclePatch.Cleanup(forceField);
                    values.SetValue(null, index);
                }
            }
            forceFieldsField.SetValue(owner, null);
        }

        private static void DisposeField(
            object owner,
            FieldInfo field,
            MethodInfo dispose)
        {
            object value = field.GetValue(owner);
            if (value != null)
                dispose.Invoke(value, null);
            field.SetValue(owner, null);
        }

        private static void ClearCollection(object owner, FieldInfo field)
        {
            IDictionary dictionary = field.GetValue(owner) as IDictionary;
            if (dictionary != null)
                dictionary.Clear();
            field.SetValue(owner, null);
        }

        private static bool Contains(object values, object expected)
        {
            IEnumerable enumerable = values as IEnumerable;
            if (enumerable == null)
                return false;
            foreach (object value in enumerable)
            {
                if (Object.ReferenceEquals(value, expected))
                    return true;
            }
            return false;
        }

        private static Type DictionaryValueType(FieldInfo field)
        {
            Type[] arguments = field.FieldType.GetGenericArguments();
            if (arguments.Length != 2)
                throw new MissingMemberException(
                    field.DeclaringType.FullName,
                    field.Name + " value type");
            return arguments[1];
        }

        private static Type ArrayElementType(FieldInfo field)
        {
            Type elementType = field.FieldType.GetElementType();
            if (elementType == null)
                throw new MissingMemberException(
                    field.DeclaringType.FullName,
                    field.Name + " element type");
            return elementType;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, InstanceMembers);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
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

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] arguments,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                arguments,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
