using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GibTeardownPatch
    {
        private static Type gibType;
        private static FieldInfo entityInstancesField;
        private static FieldInfo gibCacheField;
        private static FieldInfo bloodEffectField;
        private static FieldInfo trailEffectField;
        private static FieldInfo modelField;
        private static FieldInfo renderDataField;
        private static FieldInfo meshField;
        private static FieldInfo meshPartField;
        private static FieldInfo vertexBufferField;
        private static FieldInfo indexBufferField;
        private static FieldInfo vertexDeclarationField;
        private static FieldInfo meshDirtyField;
        private static MethodInfo effectManagerInstanceGetter;
        private static MethodInfo effectStopMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Gib level teardown",
                "org.magickacommunitypatch.gib-level-teardown",
                FindClearHandles,
                target => typeof(GibTeardownPatch).GetMethod("Prefix"));

        private static MethodInfo FindClearHandles(Assembly assembly)
        {
            Type entityType = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            gibType = assembly.GetType(
                "Magicka.GameLogic.Entities.Gib",
                true);
            Type effectManagerType = assembly.GetType(
                "Magicka.Graphics.EffectManager",
                true);
            Type renderDataType = gibType.GetNestedType(
                "RenderData",
                BindingFlags.NonPublic);
            if (renderDataType == null)
                throw new MissingMemberException(gibType.FullName, "RenderData");

            entityInstancesField = RequireField(entityType, "mInstances", true);
            gibCacheField = RequireField(gibType, "GibCache", true);
            bloodEffectField = RequireField(gibType, "mBloodEffect", false);
            trailEffectField = RequireField(gibType, "mTrailEffect", false);
            modelField = RequireField(gibType, "mModel", false);
            renderDataField = RequireField(gibType, "mRenderData", false);
            meshField = RequireField(gibType, "mMesh", false);
            meshPartField = RequireField(gibType, "mMeshPart", false);
            vertexBufferField = RequireField(
                renderDataType,
                "mVertexBuffer",
                false);
            indexBufferField = RequireField(renderDataType, "mIndexBuffer", false);
            vertexDeclarationField = RequireField(
                renderDataType,
                "mVertexDeclaration",
                false);
            meshDirtyField = RequireField(renderDataType, "mMeshDirty", false);

            PropertyInfo effectManagerInstance = effectManagerType.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            effectManagerInstanceGetter = effectManagerInstance == null
                ? null
                : effectManagerInstance.GetGetMethod();
            effectStopMethod = effectManagerType.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { bloodEffectField.FieldType.MakeByRefType() },
                null);

            MethodInfo clearHandles = entityType.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (!typeof(IEnumerable).IsAssignableFrom(entityInstancesField.FieldType) ||
                !typeof(IList).IsAssignableFrom(gibCacheField.FieldType) ||
                bloodEffectField.FieldType != trailEffectField.FieldType ||
                !renderDataField.FieldType.IsArray ||
                renderDataField.FieldType.GetElementType() != renderDataType ||
                meshDirtyField.FieldType != typeof(bool) ||
                effectManagerInstanceGetter == null ||
                effectManagerInstanceGetter.ReturnType != effectManagerType ||
                effectStopMethod == null ||
                effectStopMethod.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "Gib level teardown members are incomplete.");
            if (clearHandles == null || clearHandles.ReturnType != typeof(void))
                throw new MissingMethodException(entityType.FullName, "ClearHandles");
            return clearHandles;
        }

        public static void Prefix()
        {
            try
            {
                CleanupAll();
            }
            catch
            {
            }
        }

        public static void CleanupAll()
        {
            ArrayList gibs = new ArrayList();
            AddGibs(gibs, entityInstancesField.GetValue(null));
            object cacheValue = gibCacheField.GetValue(null);
            AddGibs(gibs, cacheValue);
            IList cache = cacheValue as IList;
            if (cache != null)
                cache.Clear();

            for (int index = 0; index < gibs.Count; index++)
            {
                try
                {
                    CleanupGib(gibs[index]);
                }
                catch
                {
                }
            }
        }

        private static void AddGibs(ArrayList gibs, object source)
        {
            IEnumerable values = source as IEnumerable;
            if (values == null)
                return;
            foreach (object value in values)
            {
                if (value == null || !gibType.IsInstanceOfType(value))
                    continue;
                bool found = false;
                for (int index = 0; index < gibs.Count; index++)
                {
                    if (Object.ReferenceEquals(gibs[index], value))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    gibs.Add(value);
            }
        }

        private static void CleanupGib(object gib)
        {
            StopEffect(gib, bloodEffectField);
            StopEffect(gib, trailEffectField);
            modelField.SetValue(gib, null);
            Array renderData = renderDataField.GetValue(gib) as Array;
            if (renderData != null)
            {
                for (int index = 0; index < renderData.Length; index++)
                {
                    object data = renderData.GetValue(index);
                    if (data == null)
                        continue;
                    vertexBufferField.SetValue(data, null);
                    indexBufferField.SetValue(data, null);
                    vertexDeclarationField.SetValue(data, null);
                    meshDirtyField.SetValue(data, true);
                }
            }
            renderDataField.SetValue(gib, null);
            meshField.SetValue(gib, null);
            meshPartField.SetValue(gib, null);
        }

        private static void StopEffect(object gib, FieldInfo field)
        {
            object[] arguments = new object[] { field.GetValue(gib) };
            try
            {
                object manager = effectManagerInstanceGetter.Invoke(null, null);
                effectStopMethod.Invoke(manager, arguments);
            }
            catch
            {
            }
            field.SetValue(gib, arguments[0]);
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            bool isStatic)
        {
            BindingFlags scope = isStatic
                ? BindingFlags.Static
                : BindingFlags.Instance;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    scope | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }
    }
}
