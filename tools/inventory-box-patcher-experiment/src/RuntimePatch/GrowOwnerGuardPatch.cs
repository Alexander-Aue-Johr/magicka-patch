using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GrowOwnerGuardPatch
    {
        private const string GrowTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grow";

        private static FieldInfo ownerField;
        private static FieldInfo ttlField;
        private static FieldInfo animationTimeField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Grow orphaned owner guard",
                "org.magickacommunitypatch.grow-owner-guard",
                FindUpdate,
                target => typeof(GrowOwnerGuardPatch).GetMethod("Prefix"));

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type grow = targetAssembly.GetType(GrowTypeName, true);
            Type character = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");

            ownerField = RequireField(grow, "mOwner", character);
            ttlField = RequireField(grow, "mTTL", typeof(float));
            animationTimeField = RequireField(
                grow,
                "mAnimationTime",
                typeof(float));

            MethodInfo update = grow.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (update == null || update.ReturnType != typeof(void))
                throw new MissingMethodException(grow.FullName, "Update");
            return update;
        }

        public static bool Prefix(object __instance)
        {
            if (ownerField.GetValue(__instance) != null)
                return true;

            ttlField.SetValue(__instance, 0f);
            animationTimeField.SetValue(__instance, 0f);
            return false;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
