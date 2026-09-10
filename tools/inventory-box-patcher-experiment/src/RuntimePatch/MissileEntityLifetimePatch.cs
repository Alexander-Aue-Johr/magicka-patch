using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MissileEntityLifetimePatch
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static FieldInfo ownerField;
        private static FieldInfo targetField;
        private static FieldInfo collisionTargetField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Postfix(
                "Missile deinitialize reference release",
                "org.magickacommunitypatch.missile-deinitialize-release",
                FindTarget,
                target => typeof(MissileEntityLifetimePatch).GetMethod(
                    "Postfix"));

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type missile = assembly.GetType(
                "Magicka.GameLogic.Entities.MissileEntity", true);
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity", true);
            ownerField = RequireEntityField(missile, "mOwner", entity);
            targetField = RequireEntityField(missile, "mTarget", entity);
            collisionTargetField = RequireEntityField(
                missile, "mCollisionTarget", entity);
            MethodInfo method = missile.GetMethod(
                "Deinitialize", Instance, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    missile.FullName, "Deinitialize");
            return method;
        }

        private static FieldInfo RequireEntityField(
            Type type, string name, Type entity)
        {
            FieldInfo field = type.GetField(name, Instance);
            if (field == null || !entity.IsAssignableFrom(field.FieldType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static void Postfix(object __instance)
        {
            ownerField.SetValue(__instance, null);
            targetField.SetValue(__instance, null);
            collisionTargetField.SetValue(__instance, null);
        }
    }
}
