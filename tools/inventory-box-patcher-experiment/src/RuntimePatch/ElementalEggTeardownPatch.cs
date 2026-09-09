using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ElementalEggTeardownPatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic;

        private static Type eggType;
        private static FieldInfo instancesField;
        private static FieldInfo controllerField;
        private static MethodInfo controllerStop;
        private static PropertyInfo controllerSkeleton;
        private static FieldInfo damageMemoryField;
        private static FieldInfo renderDataField;
        private static FieldInfo renderBonesField;
        private static FieldInfo renderVertexDeclarationField;
        private static FieldInfo summonerField;
        private static FieldInfo modelField;
        private static FieldInfo clipField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "ElementalEgg final teardown",
                "org.magickacommunitypatch.elemental-egg-final-teardown",
                FindClearHandles,
                target => typeof(ElementalEggTeardownPatch).GetMethod("Prefix"));

        private static MethodInfo FindClearHandles(Assembly assembly)
        {
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            eggType = assembly.GetType(
                "Magicka.GameLogic.Entities.ElementalEgg",
                true);
            Type renderData = eggType.GetNestedType(
                "RenderData",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (renderData == null)
                throw new TypeLoadException(eggType.FullName + "+RenderData");

            instancesField = RequireField(entity, "mInstances");
            controllerField = RequireField(eggType, "mController");
            controllerStop = controllerField.FieldType.GetMethod(
                "Stop",
                InstanceMembers,
                null,
                Type.EmptyTypes,
                null);
            controllerSkeleton = controllerField.FieldType.GetProperty(
                "Skeleton",
                InstanceMembers);
            damageMemoryField = RequireField(eggType, "mDamageMemory");
            renderDataField = RequireField(eggType, "mRenderData");
            renderBonesField = RequireField(renderData, "mBones");
            renderVertexDeclarationField = RequireField(
                renderData,
                "mVertexDeclaration");
            summonerField = RequireField(eggType, "mSummoner");
            modelField = RequireField(eggType, "mModel");
            clipField = RequireField(eggType, "mClip");

            MethodInfo clearHandles = entity.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (!typeof(IList).IsAssignableFrom(instancesField.FieldType) ||
                controllerStop == null ||
                controllerStop.ReturnType != typeof(void) ||
                controllerSkeleton == null ||
                !controllerSkeleton.CanWrite ||
                !typeof(IDictionary).IsAssignableFrom(damageMemoryField.FieldType) ||
                !renderDataField.FieldType.IsArray ||
                !renderBonesField.FieldType.IsArray ||
                clearHandles == null ||
                clearHandles.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "ElementalEgg teardown contract is incomplete.");
            return clearHandles;
        }

        public static void Prefix()
        {
            IList entities = instancesField.GetValue(null) as IList;
            if (entities == null)
                return;
            for (int index = 0; index < entities.Count; index++)
            {
                object entity = entities[index];
                if (entity != null && eggType.IsInstanceOfType(entity))
                    CleanupFinal(entity);
            }
        }

        public static void CleanupFinal(object egg)
        {
            if (egg == null || !eggType.IsInstanceOfType(egg))
                return;

            object controller = controllerField.GetValue(egg);
            if (controller != null)
            {
                try
                {
                    controllerStop.Invoke(controller, null);
                }
                catch
                {
                }
                try
                {
                    controllerSkeleton.SetValue(controller, null, null);
                }
                catch
                {
                }
            }
            controllerField.SetValue(egg, null);

            IDictionary damageMemory =
                damageMemoryField.GetValue(egg) as IDictionary;
            if (damageMemory != null)
                damageMemory.Clear();
            damageMemoryField.SetValue(egg, null);

            Array renderData = renderDataField.GetValue(egg) as Array;
            if (renderData != null)
            {
                for (int index = 0; index < renderData.Length; index++)
                {
                    object data = renderData.GetValue(index);
                    if (data == null)
                        continue;
                    renderBonesField.SetValue(data, null);
                    renderVertexDeclarationField.SetValue(data, null);
                }
            }
            renderDataField.SetValue(egg, null);
            summonerField.SetValue(egg, null);
            modelField.SetValue(egg, null);
            clipField.SetValue(egg, null);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            for (Type current = type; current != null;
                current = current.BaseType)
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
    }
}
