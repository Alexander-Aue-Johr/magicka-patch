using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AnimatedLevelPartDisposePatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic;

        private static readonly object SyncRoot = new object();
        private static readonly List<WeakReference> DisposedParts =
            new List<WeakReference>();
        private static readonly List<LiquidEffectRegistration> LiquidEffects =
            new List<LiquidEffectRegistration>();

        private static Type partType;
        private static Type liquidType;
        private static Type waterType;
        private static Type lavaType;
        private static MethodInfo disposeMethod;
        private static FieldInfo partDisposedField;
        private static FieldInfo collisionSkinField;
        private static FieldInfo liquidsField;
        private static FieldInfo navMeshField;
        private static FieldInfo levelField;
        private static FieldInfo modelField;
        private static FieldInfo childrenField;
        private static FieldInfo additiveRenderDataField;
        private static FieldInfo defaultRenderDataField;
        private static FieldInfo highlightRenderDataField;
        private static FieldInfo collidingEntitiesField;
        private static FieldInfo decalsField;
        private static FieldInfo callbackField;
        private static FieldInfo postCollisionCallbackField;
        private static PropertyInfo skinTagProperty;
        private static PropertyInfo skinCollisionSystemProperty;
        private static PropertyInfo collisionSkinsProperty;
        private static MethodInfo removeCollisionSkinMethod;
        private static PropertyInfo levelNavMeshProperty;
        private static PropertyInfo animatedPartsProperty;
        private static PropertyInfo modelMeshesProperty;
        private static PropertyInfo meshVertexBufferProperty;
        private static PropertyInfo meshIndexBufferProperty;
        private static PropertyInfo meshPartsProperty;
        private static PropertyInfo meshPartEffectProperty;
        private static PropertyInfo meshPartVertexDeclarationProperty;

        internal static readonly RuntimePatchDefinition WaterConstructorDefinition =
            RuntimePatchDefinition.ConstructorPostfix(
                "Water liquid effect ownership",
                "org.magickacommunitypatch.water-effect-ownership",
                FindWaterConstructor,
                typeof(AnimatedLevelPartDisposePatch).GetMethod(
                    "RecordLiquidEffectPostfix"));

        internal static readonly RuntimePatchDefinition LavaConstructorDefinition =
            RuntimePatchDefinition.ConstructorPostfix(
                "Lava liquid effect ownership",
                "org.magickacommunitypatch.lava-effect-ownership",
                FindLavaConstructor,
                typeof(AnimatedLevelPartDisposePatch).GetMethod(
                    "RecordLiquidEffectPostfix"));

        internal static readonly RuntimePatchDefinition DisposeDefinition =
            RuntimePatchDefinition.Prefix(
                "AnimatedLevelPart resource disposal",
                "org.magickacommunitypatch.animated-level-part-dispose",
                FindDispose,
                target => typeof(AnimatedLevelPartDisposePatch).GetMethod(
                    "DisposePrefix"));

        private static ConstructorInfo FindWaterConstructor(Assembly targetAssembly)
        {
            ResolveContracts(targetAssembly);
            return RequireLiquidConstructor(
                waterType,
                RuntimeMember.FindLoadedType(
                    "PolygonHead.Effects.RenderDeferredLiquidEffect"));
        }

        private static ConstructorInfo FindLavaConstructor(Assembly targetAssembly)
        {
            ResolveContracts(targetAssembly);
            return RequireLiquidConstructor(
                lavaType,
                RuntimeMember.FindLoadedType("PolygonHead.Effects.LavaEffect"));
        }

        private static MethodInfo FindDispose(Assembly targetAssembly)
        {
            ResolveContracts(targetAssembly);
            return disposeMethod;
        }

        private static void ResolveContracts(Assembly targetAssembly)
        {
            partType = targetAssembly.GetType(
                "Magicka.Levels.AnimatedLevelPart",
                true);
            liquidType = targetAssembly.GetType("Magicka.Levels.Liquid", true);
            waterType = targetAssembly.GetType("Magicka.Levels.Water", true);
            lavaType = targetAssembly.GetType("Magicka.Levels.Lava", true);
            if (waterType.BaseType != liquidType || lavaType.BaseType != liquidType)
                throw new InvalidOperationException(
                    "Water and Lava must directly derive from Liquid.");

            disposeMethod = RequireMethod(
                partType,
                "Dispose",
                Type.EmptyTypes,
                typeof(void));
            partDisposedField = partType.GetField(
                "mDisposed",
                InstanceMembers | BindingFlags.DeclaredOnly);
            if (partDisposedField != null &&
                partDisposedField.FieldType != typeof(bool))
                throw new MissingFieldException(partType.FullName, "mDisposed");
            collisionSkinField = RequireField(partType, "mCollisionSkin");
            liquidsField = RequireField(partType, "mLiquids");
            navMeshField = RequireField(partType, "mNavMesh");
            levelField = RequireField(partType, "mLevel");
            modelField = RequireField(partType, "mModel");
            childrenField = RequireField(partType, "mChildren");
            additiveRenderDataField = RequireField(
                partType,
                "mAdditiveRenderData");
            defaultRenderDataField = RequireField(
                partType,
                "mDefaultRenderData");
            highlightRenderDataField = RequireField(
                partType,
                "mHighlightRenderData");
            collidingEntitiesField = RequireField(
                partType,
                "mCollidingEntities");
            decalsField = RequireField(partType, "mDecals");

            Type skinType = collisionSkinField.FieldType;
            callbackField = RequireField(skinType, "callbackFn");
            postCollisionCallbackField = RequireField(
                skinType,
                "postCollisionCallbackFn");
            skinTagProperty = RequireProperty(skinType, "Tag");
            skinCollisionSystemProperty = RequireProperty(
                skinType,
                "CollisionSystem");
            Type collisionSystemType = skinCollisionSystemProperty.PropertyType;
            collisionSkinsProperty = RequireProperty(
                collisionSystemType,
                "CollisionSkins");
            removeCollisionSkinMethod = RequireMethod(
                collisionSystemType,
                "RemoveCollisionSkin",
                new Type[] { skinType },
                typeof(bool));

            levelNavMeshProperty = RequireProperty(
                levelField.FieldType,
                "NavMesh");
            animatedPartsProperty = RequireProperty(
                levelNavMeshProperty.PropertyType,
                "AnimatedParts");

            modelMeshesProperty = RequireProperty(modelField.FieldType, "Meshes");
            Type meshType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.ModelMesh");
            Type meshPartType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.ModelMeshPart");
            meshVertexBufferProperty = RequireProperty(meshType, "VertexBuffer");
            meshIndexBufferProperty = RequireProperty(meshType, "IndexBuffer");
            meshPartsProperty = RequireProperty(meshType, "MeshParts");
            meshPartEffectProperty = RequireProperty(meshPartType, "Effect");
            meshPartVertexDeclarationProperty = RequireProperty(
                meshPartType,
                "VertexDeclaration");

            RequireLiquidFields(waterType);
            RequireLiquidFields(lavaType);
        }

        private static ConstructorInfo RequireLiquidConstructor(
            Type concreteType,
            Type effectType)
        {
            Type contentReader = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Content.ContentReader");
            ConstructorInfo constructor = concreteType.GetConstructor(
                InstanceMembers,
                null,
                new Type[]
                {
                    effectType,
                    contentReader,
                    levelField.FieldType,
                    partType
                },
                null);
            if (constructor == null)
                throw new MissingMethodException(concreteType.FullName, ".ctor");
            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters[0].Name != "iEffect")
                throw new InvalidOperationException(
                    concreteType.FullName + " constructor effect parameter changed.");
            return constructor;
        }

        private static void RequireLiquidFields(Type concreteType)
        {
            string[] fields = new string[]
            {
                "mCollisionSkin",
                "mVertexDeclaration",
                "mWaterFreezeVertexBuffer",
                "mWaterIndices",
                "mWaterVertices",
                "mIceCollisionMesh",
                "mWaterCollisionMesh",
                "mWaterFreezeVertices",
                "mRenderData"
            };
            for (int index = 0; index < fields.Length; index++)
                RequireField(concreteType, fields[index]);
            FieldInfo disposed = concreteType.GetField(
                "mDisposed",
                InstanceMembers | BindingFlags.DeclaredOnly);
            if (disposed != null && disposed.FieldType != typeof(bool))
                throw new MissingFieldException(concreteType.FullName, "mDisposed");
        }

        public static void RecordLiquidEffectPostfix(
            object __instance,
            object iEffect)
        {
            if (__instance == null || iEffect == null)
                return;
            lock (SyncRoot)
            {
                PruneLiquidEffects();
                LiquidEffects.Add(
                    new LiquidEffectRegistration(__instance, iEffect));
            }
        }

        public static bool DisposePrefix(object __instance)
        {
            if (__instance != null && MarkDisposed(__instance))
                DisposePart(__instance);
            return false;
        }

        public static void DisposeStandaloneLiquid(object liquid)
        {
            if (liquid != null)
                DisposeLiquid(liquid, null);
        }

        private static bool MarkDisposed(object part)
        {
            lock (SyncRoot)
            {
                if (partDisposedField != null &&
                    (bool)partDisposedField.GetValue(part))
                    return false;
                if (partDisposedField != null)
                    partDisposedField.SetValue(part, true);

                for (int index = DisposedParts.Count - 1; index >= 0; index--)
                {
                    object existing = DisposedParts[index].Target;
                    if (existing == null)
                    {
                        DisposedParts.RemoveAt(index);
                        continue;
                    }
                    if (Object.ReferenceEquals(existing, part))
                        return false;
                }
                DisposedParts.Add(new WeakReference(part));
                return true;
            }
        }

        private static void DisposePart(object part)
        {
            ReleaseSkin(collisionSkinField.GetValue(part), part, null);
            collisionSkinField.SetValue(part, null);

            Array liquids = liquidsField.GetValue(part) as Array;
            if (liquids != null)
            {
                for (int index = 0; index < liquids.Length; index++)
                {
                    object liquid = liquids.GetValue(index);
                    if (liquid != null)
                        DisposeLiquid(liquid, part);
                }
            }
            liquidsField.SetValue(part, null);

            object navMesh = navMeshField.GetValue(part);
            object level = levelField.GetValue(part);
            if (navMesh != null && level != null)
            {
                object levelNavMesh = levelNavMeshProperty.GetValue(level, null);
                if (levelNavMesh != null)
                    RemoveFromCollection(
                        animatedPartsProperty.GetValue(levelNavMesh, null),
                        navMesh);
            }
            navMeshField.SetValue(part, null);

            DisposeModel(modelField.GetValue(part));
            modelField.SetValue(part, null);

            object children = childrenField.GetValue(part);
            if (children != null)
            {
                foreach (object child in Values(children))
                {
                    if (child != null && MarkDisposed(child))
                        DisposePart(child);
                }
                ClearCollection(children);
            }
            childrenField.SetValue(part, null);
            additiveRenderDataField.SetValue(part, null);
            defaultRenderDataField.SetValue(part, null);
            highlightRenderDataField.SetValue(part, null);
            ClearCollection(collidingEntitiesField.GetValue(part));
            ClearCollection(decalsField.GetValue(part));
            levelField.SetValue(part, null);
            GC.SuppressFinalize(part);
        }

        private static void DisposeLiquid(object liquid, object parent)
        {
            Type concreteType = liquid.GetType();
            FieldInfo disposed = concreteType.GetField(
                "mDisposed",
                InstanceMembers | BindingFlags.DeclaredOnly);
            if (disposed != null)
            {
                if ((bool)disposed.GetValue(liquid))
                    return;
                disposed.SetValue(liquid, true);
            }

            FieldInfo skinField = RequireField(concreteType, "mCollisionSkin");
            ReleaseSkin(skinField.GetValue(liquid), liquid, parent);
            skinField.SetValue(liquid, null);
            DisposeAndClear(liquid, "mVertexDeclaration");
            DisposeAndClear(liquid, "mWaterFreezeVertexBuffer");
            DisposeAndClear(liquid, "mWaterIndices");
            DisposeAndClear(liquid, "mWaterVertices");
            ClearField(liquid, "mIceCollisionMesh");
            ClearField(liquid, "mWaterCollisionMesh");
            ClearField(liquid, "mWaterFreezeVertices");
            ClearField(liquid, "mRenderData");
            DisposeRecordedLiquidEffect(liquid);
        }

        private static void ReleaseSkin(
            object skin,
            object primaryOwner,
            object secondaryOwner)
        {
            if (skin == null)
                return;
            RemoveOwnedCallbacks(callbackField, skin, primaryOwner, secondaryOwner);
            RemoveOwnedCallbacks(
                postCollisionCallbackField,
                skin,
                primaryOwner,
                secondaryOwner);
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
            skinTagProperty.SetValue(skin, null, null);
        }

        private static void RemoveOwnedCallbacks(
            FieldInfo field,
            object skin,
            object primaryOwner,
            object secondaryOwner)
        {
            Delegate callbacks = field.GetValue(skin) as Delegate;
            if (callbacks == null)
                return;
            Delegate[] entries = callbacks.GetInvocationList();
            for (int index = 0; index < entries.Length; index++)
            {
                object target = entries[index].Target;
                if (Object.ReferenceEquals(target, primaryOwner) ||
                    Object.ReferenceEquals(target, secondaryOwner))
                    callbacks = Delegate.Remove(callbacks, entries[index]);
            }
            field.SetValue(skin, callbacks);
        }

        private static void DisposeModel(object model)
        {
            if (model == null)
                return;
            IEnumerable meshes = modelMeshesProperty.GetValue(model, null)
                as IEnumerable;
            if (meshes == null)
                return;
            foreach (object mesh in meshes)
            {
                if (mesh == null)
                    continue;
                DisposeObject(meshVertexBufferProperty.GetValue(mesh, null));
                DisposeObject(meshIndexBufferProperty.GetValue(mesh, null));
                IEnumerable parts = meshPartsProperty.GetValue(mesh, null)
                    as IEnumerable;
                if (parts == null)
                    continue;
                foreach (object part in parts)
                {
                    if (part == null)
                        continue;
                    DisposeObject(meshPartEffectProperty.GetValue(part, null));
                    DisposeObject(
                        meshPartVertexDeclarationProperty.GetValue(part, null));
                }
            }
        }

        private static void DisposeAndClear(object target, string fieldName)
        {
            FieldInfo field = RequireField(target.GetType(), fieldName);
            DisposeObject(field.GetValue(target));
            field.SetValue(target, null);
        }

        private static void ClearField(object target, string fieldName)
        {
            RequireField(target.GetType(), fieldName).SetValue(target, null);
        }

        private static void DisposeRecordedLiquidEffect(object liquid)
        {
            object effect = null;
            lock (SyncRoot)
            {
                for (int index = LiquidEffects.Count - 1; index >= 0; index--)
                {
                    object owner = LiquidEffects[index].Owner.Target;
                    if (owner == null)
                    {
                        DisposeObject(LiquidEffects[index].Effect);
                        LiquidEffects.RemoveAt(index);
                    }
                    else if (Object.ReferenceEquals(owner, liquid))
                    {
                        effect = LiquidEffects[index].Effect;
                        LiquidEffects.RemoveAt(index);
                    }
                }
            }
            if (effect == null)
            {
                FieldInfo inheritedEffect = liquidType.GetField(
                    "effect",
                    InstanceMembers | BindingFlags.DeclaredOnly);
                if (inheritedEffect != null)
                    effect = inheritedEffect.GetValue(liquid);
            }
            DisposeObject(effect);
        }

        private static void PruneLiquidEffects()
        {
            for (int index = LiquidEffects.Count - 1; index >= 0; index--)
            {
                if (!LiquidEffects[index].Owner.IsAlive)
                {
                    DisposeObject(LiquidEffects[index].Effect);
                    LiquidEffects.RemoveAt(index);
                }
            }
        }

        private static IEnumerable Values(object dictionary)
        {
            PropertyInfo values = dictionary.GetType().GetProperty("Values");
            IEnumerable enumerable = values == null
                ? null
                : values.GetValue(dictionary, null) as IEnumerable;
            return enumerable ?? new object[0];
        }

        private static void ClearCollection(object collection)
        {
            if (collection == null)
                return;
            MethodInfo clear = collection.GetType().GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (clear == null || clear.ReturnType != typeof(void))
                throw new MissingMethodException(
                    collection.GetType().FullName,
                    "Clear");
            clear.Invoke(collection, null);
        }

        private static bool Contains(object collection, object item)
        {
            if (collection == null)
                return false;
            MethodInfo contains = collection.GetType().GetMethod(
                "Contains",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { item.GetType() },
                null);
            if (contains == null || contains.ReturnType != typeof(bool))
                throw new MissingMethodException(
                    collection.GetType().FullName,
                    "Contains");
            return (bool)contains.Invoke(collection, new object[] { item });
        }

        private static void RemoveFromCollection(object collection, object item)
        {
            if (collection == null)
                return;
            MethodInfo remove = collection.GetType().GetMethod(
                "Remove",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { item.GetType() },
                null);
            if (remove == null)
                throw new MissingMethodException(
                    collection.GetType().FullName,
                    "Remove");
            remove.Invoke(collection, new object[] { item });
        }

        private static void DisposeObject(object value)
        {
            IDisposable disposable = value as IDisposable;
            if (disposable != null)
                disposable.Dispose();
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                InstanceMembers | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                InstanceMembers);
            if (property == null || property.GetGetMethod(true) == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                InstanceMembers,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private sealed class LiquidEffectRegistration
        {
            internal readonly WeakReference Owner;
            internal readonly object Effect;

            internal LiquidEffectRegistration(object owner, object effect)
            {
                Owner = new WeakReference(owner);
                Effect = effect;
            }
        }
    }
}
