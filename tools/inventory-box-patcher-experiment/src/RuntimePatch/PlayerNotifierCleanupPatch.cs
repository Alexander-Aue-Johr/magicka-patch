using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PlayerNotifierCleanupPatch
    {
        private static FieldInfo notifierField;
        private static FieldInfo ownerField;
        private static FieldInfo dialogAttachField;
        private static FieldInfo alphaField;
        private static FieldInfo targetAlphaField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Player notifier level release",
                "org.magickacommunitypatch.player-notifier-release",
                FindDeinitializeGame,
                CreatePrefix);

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            return typeof(PlayerNotifierCleanupPatch).GetMethod("Prefix");
        }

        private static MethodInfo FindDeinitializeGame(Assembly targetAssembly)
        {
            Type playerType = targetAssembly.GetType(
                "Magicka.GameLogic.Player",
                true);
            Type notifierType = targetAssembly.GetType(
                "Magicka.Graphics.NotifierButton",
                true);
            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type textBoxType = targetAssembly.GetType(
                "Magicka.Graphics.TextBox",
                true);

            notifierField = RequireField(
                playerType,
                "mNotifierButton",
                notifierType);
            ownerField = RequireField(notifierType, "mOwner", entityType);
            dialogAttachField = RequireField(
                notifierType,
                "mDialogAttach",
                textBoxType);
            alphaField = RequireField(notifierType, "mAlpha", typeof(float));
            targetAlphaField = RequireField(
                notifierType,
                "mTargetAlpha",
                typeof(float));

            MethodInfo method = playerType.GetMethod(
                "DeinitializeGame",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    playerType.FullName,
                    "DeinitializeGame");
            return method;
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

        public static void Prefix(object __instance)
        {
            if (notifierField == null || ownerField == null ||
                dialogAttachField == null || alphaField == null ||
                targetAlphaField == null)
                throw new InvalidOperationException(
                    "Player notifier cleanup contract has not been initialized.");

            object notifier = notifierField.GetValue(__instance);
            if (notifier == null)
                return;

            alphaField.SetValue(notifier, 0f);
            targetAlphaField.SetValue(notifier, 0f);
            ownerField.SetValue(notifier, null);
            dialogAttachField.SetValue(notifier, null);
        }
    }
}
