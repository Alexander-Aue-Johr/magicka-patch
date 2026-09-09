using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ForceFieldLifecyclePatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static FieldInfo playStateField;
        private static FieldInfo renderDataField;
        private static FieldInfo materialField;
        private static FieldInfo collisionPointsField;
        private static FieldInfo verticesField;
        private static FieldInfo indicesField;
        private static FieldInfo declarationField;
        private static FieldInfo collisionField;
        private static FieldInfo renderPointsField;
        private static FieldInfo renderMaterialField;
        private static FieldInfo renderVerticesField;
        private static FieldInfo renderIndicesField;
        private static FieldInfo renderDeclarationField;
        private static FieldInfo displacementMapField;
        private static FieldInfo callbackField;
        private static PropertyInfo skinTagProperty;
        private static PropertyInfo skinCollisionSystemProperty;
        private static PropertyInfo collisionSkinsProperty;
        private static MethodInfo removeCollisionSkinMethod;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Postfix(
                "ForceField play-state release",
                "org.magickacommunitypatch.force-field-play-state-release",
                FindInitialize,
                target => typeof(ForceFieldLifecyclePatch).GetMethod(
                    "InitializePostfix"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "ForceField current play state",
                "org.magickacommunitypatch.force-field-current-play-state",
                FindUpdate,
                typeof(ForceFieldLifecyclePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindInitialize(Assembly targetAssembly)
        {
            Type forceField = Configure(targetAssembly);
            Type playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            MethodInfo method = forceField.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playState },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(forceField.FullName, "Initialize");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type forceField = Configure(targetAssembly);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = forceField.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(forceField.FullName, "Update");
            return method;
        }

        private static Type Configure(Assembly targetAssembly)
        {
            Type forceField = targetAssembly.GetType(
                "Magicka.Levels.ForceField",
                true);
            Type playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = RequireField(forceField, "mPlayState");
            if (playStateField.FieldType != playState)
                throw new MissingFieldException(forceField.FullName, "mPlayState");
            renderDataField = RequireField(forceField, "mRenderData");
            materialField = RequireField(forceField, "mMaterial");
            collisionPointsField = RequireField(forceField, "mCollPoints");
            verticesField = RequireField(forceField, "mVertices");
            indicesField = RequireField(forceField, "mIndices");
            declarationField = RequireField(forceField, "mDeclaration");
            collisionField = RequireField(forceField, "mCollision");

            Type renderData = renderDataField.FieldType.GetElementType();
            if (renderData == null)
                throw new MissingMemberException(forceField.FullName, "mRenderData");
            renderPointsField = RequireField(renderData, "CollPoints");
            renderMaterialField = RequireField(renderData, "Material");
            renderVerticesField = RequireField(renderData, "mVertices");
            renderIndicesField = RequireField(renderData, "mIndices");
            renderDeclarationField = RequireField(renderData, "mDeclaration");
            if (renderMaterialField.FieldType != materialField.FieldType)
                throw new MissingMemberException(
                    forceField.FullName,
                    "ForceFieldMaterial");
            displacementMapField = RequireField(
                materialField.FieldType,
                "DisplacementMap");

            Type skin = collisionField.FieldType;
            callbackField = RequireField(skin, "postCollisionCallbackFn");
            skinTagProperty = RequireProperty(skin, "Tag");
            skinCollisionSystemProperty = RequireProperty(
                skin,
                "CollisionSystem");
            Type collisionSystem = skinCollisionSystemProperty.PropertyType;
            collisionSkinsProperty = RequireProperty(
                collisionSystem,
                "CollisionSkins");
            removeCollisionSkinMethod = RequireMethod(
                collisionSystem,
                "RemoveCollisionSkin",
                new Type[] { skin },
                typeof(bool));

            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
            return forceField;
        }

        public static void InitializePostfix(object __instance)
        {
            if (__instance != null)
                playStateField.SetValue(__instance, null);
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int matches = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                result[index - 1].opcode = OpCodes.Nop;
                result[index - 1].operand = null;
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one ForceField play-state read, found " +
                    matches + ".");
            return result;
        }

        public static void Cleanup(object forceField)
        {
            if (forceField == null)
                return;
            ReleaseCollision(forceField);
            ReleaseRenderData(forceField);
            DisposeAndClear(forceField, verticesField);
            DisposeAndClear(forceField, indicesField);
            DisposeAndClear(forceField, declarationField);
            ClearDisplacementMap(forceField, materialField);
            collisionPointsField.SetValue(forceField, null);
            playStateField.SetValue(forceField, null);
        }

        private static void ReleaseCollision(object forceField)
        {
            object skin = collisionField.GetValue(forceField);
            if (skin == null)
                return;
            Delegate callbacks = callbackField.GetValue(skin) as Delegate;
            if (callbacks != null)
            {
                Delegate[] entries = callbacks.GetInvocationList();
                for (int index = 0; index < entries.Length; index++)
                {
                    if (Object.ReferenceEquals(
                        entries[index].Target,
                        forceField))
                        callbacks = Delegate.Remove(callbacks, entries[index]);
                }
                callbackField.SetValue(skin, callbacks);
            }
            try
            {
                object system = skinCollisionSystemProperty.GetValue(skin, null);
                if (system != null &&
                    Contains(collisionSkinsProperty.GetValue(system, null), skin))
                    removeCollisionSkinMethod.Invoke(
                        system,
                        new object[] { skin });
            }
            catch
            {
            }
            skinTagProperty.SetValue(skin, null, null);
            collisionField.SetValue(forceField, null);
        }

        private static void ReleaseRenderData(object forceField)
        {
            Array values = renderDataField.GetValue(forceField) as Array;
            if (values != null)
            {
                for (int index = 0; index < values.Length; index++)
                {
                    object value = values.GetValue(index);
                    if (value == null)
                        continue;
                    ClearDisplacementMap(value, renderMaterialField);
                    renderPointsField.SetValue(value, null);
                    renderMaterialField.SetValue(
                        value,
                        Activator.CreateInstance(renderMaterialField.FieldType));
                    renderVerticesField.SetValue(value, null);
                    renderIndicesField.SetValue(value, null);
                    renderDeclarationField.SetValue(value, null);
                    values.SetValue(null, index);
                }
            }
            renderDataField.SetValue(forceField, null);
        }

        private static void ClearDisplacementMap(
            object owner,
            FieldInfo field)
        {
            object material = field.GetValue(owner);
            displacementMapField.SetValue(material, null);
            field.SetValue(owner, material);
        }

        private static void DisposeAndClear(object owner, FieldInfo field)
        {
            object value = field.GetValue(owner);
            if (value != null)
            {
                PropertyInfo disposed = value.GetType().GetProperty(
                    "IsDisposed",
                    BindingFlags.Instance | BindingFlags.Public);
                bool isDisposed = disposed != null &&
                    (bool)disposed.GetValue(value, null);
                if (!isDisposed)
                {
                    MethodInfo dispose = value.GetType().GetMethod(
                        "Dispose",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (dispose == null)
                        throw new MissingMethodException(
                            value.GetType().FullName,
                            "Dispose");
                    dispose.Invoke(value, null);
                }
            }
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
