using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class PhysicsEntityTemplateCachePatch
    {
        private static readonly string[] OwnedFieldNames = new string[]
        {
            "mConditions",
            "mMeshVertices",
            "mMeshIndices",
            "mEffects",
            "mModel",
            "mResistances",
            "mGibs",
            "mModels",
            "mSkeleton",
            "mAnimationClips",
            "mAttachedEffects"
        };

        private static FieldInfo cacheField;
        private static FieldInfo skeletonVerticesField;
        private static FieldInfo[] ownedFields;
        private static MethodInfo physicsClearMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Physics entity template cache cleanup",
                "org.magickacommunitypatch.physics-entity-template-cache-cleanup",
                FindPlayStateDispose,
                typeof(PhysicsEntityTemplateCachePatch).GetMethod("Transpiler"));

        internal static bool IsAvailableIn(Assembly targetAssembly)
        {
            Type templateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.PhysicsEntityTemplate",
                false);
            return templateType != null &&
                templateType.GetField(
                    "sCache",
                    BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null;
        }

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type templateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.PhysicsEntityTemplate",
                true);
            cacheField = templateType.GetField(
                "sCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (cacheField == null ||
                !typeof(IDictionary).IsAssignableFrom(cacheField.FieldType) ||
                !cacheField.FieldType.IsGenericType)
                throw new MissingFieldException(templateType.FullName, "sCache");
            Type[] cacheArguments = cacheField.FieldType.GetGenericArguments();
            if (cacheArguments.Length != 2 ||
                cacheArguments[0] != typeof(int) ||
                cacheArguments[1] != templateType)
                throw new MissingFieldException(templateType.FullName, "sCache");

            skeletonVerticesField = RequireInstanceField(
                templateType,
                "mSkeletonVertices");
            if (!typeof(IDisposable).IsAssignableFrom(
                skeletonVerticesField.FieldType))
                throw new MissingFieldException(
                    templateType.FullName,
                    "mSkeletonVertices");

            ownedFields = new FieldInfo[OwnedFieldNames.Length];
            for (int index = 0; index < OwnedFieldNames.Length; index++)
            {
                ownedFields[index] = RequireInstanceField(
                    templateType,
                    OwnedFieldNames[index]);
                if (ownedFields[index].FieldType.IsValueType)
                    throw new MissingFieldException(
                        templateType.FullName,
                        OwnedFieldNames[index]);
            }

            Type physicsManagerType = targetAssembly.GetType(
                "Magicka.Physics.PhysicsManager",
                true);
            physicsClearMethod = physicsManagerType.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (physicsClearMethod == null ||
                physicsClearMethod.ReturnType != typeof(void))
                throw new MissingMethodException(
                    physicsManagerType.FullName,
                    "Clear");

            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            MethodInfo dispose = playStateType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(playStateType.FullName, "Dispose");
            return dispose;
        }

        private static FieldInfo RequireInstanceField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int anchor = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(called, physicsClearMethod))
                {
                    anchor = index;
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one PhysicsManager.Clear call in PlayState.Dispose, found " +
                    matches + ".");

            result.Insert(
                anchor + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(PhysicsEntityTemplateCachePatch).GetMethod("Clear")));
            return result;
        }

        public static void Clear()
        {
            if (cacheField == null ||
                skeletonVerticesField == null ||
                ownedFields == null)
                throw new InvalidOperationException(
                    "Physics entity template cache contract has not been initialized.");

            IDictionary cache = cacheField.GetValue(null) as IDictionary;
            if (cache == null)
                return;

            object[] templates = new object[cache.Values.Count];
            cache.Values.CopyTo(templates, 0);
            for (int index = 0; index < templates.Length; index++)
            {
                object template = templates[index];
                if (template == null)
                    continue;

                object skeletonVertices =
                    skeletonVerticesField.GetValue(template);
                if (skeletonVertices != null)
                {
                    ((IDisposable)skeletonVertices).Dispose();
                    skeletonVerticesField.SetValue(template, null);
                }

                ClearList(template, "mMeshVertices");
                ClearList(template, "mMeshIndices");
                for (int fieldIndex = 0;
                    fieldIndex < ownedFields.Length;
                    fieldIndex++)
                    ownedFields[fieldIndex].SetValue(template, null);
            }
            cache.Clear();
        }

        private static void ClearList(object template, string name)
        {
            for (int index = 0; index < ownedFields.Length; index++)
            {
                if (ownedFields[index].Name != name)
                    continue;
                IList list = ownedFields[index].GetValue(template) as IList;
                if (list != null)
                    list.Clear();
                return;
            }
            throw new MissingFieldException(template.GetType().FullName, name);
        }
    }
}
