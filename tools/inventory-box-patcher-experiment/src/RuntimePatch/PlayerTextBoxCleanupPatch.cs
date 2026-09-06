using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PlayerTextBoxCleanupPatch
    {
        private static FieldInfo obtainedTextBoxField;
        private static FieldInfo ownerField;
        private static FieldInfo sceneField;
        private static FieldInfo automaticAdvanceField;
        private static FieldInfo timeToLiveField;
        private static FieldInfo growField;
        private static FieldInfo scaleField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Player obtained text-box level release",
                "org.magickacommunitypatch.player-text-box-release",
                FindDeinitializeGame,
                CreatePrefix);

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            return typeof(PlayerTextBoxCleanupPatch).GetMethod("Prefix");
        }

        private static MethodInfo FindDeinitializeGame(Assembly targetAssembly)
        {
            Type playerType = targetAssembly.GetType(
                "Magicka.GameLogic.Player",
                true);
            Type textBoxType = targetAssembly.GetType(
                "Magicka.Graphics.TextBox",
                true);
            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);

            obtainedTextBoxField = RequireField(
                playerType,
                "mObtainedTextBox",
                textBoxType);
            ownerField = RequireField(textBoxType, "mOwner", entityType);
            sceneField = RequireField(textBoxType, "mScene", "PolygonHead.Scene");
            automaticAdvanceField = RequireField(
                textBoxType,
                "mAutomaticAdvance",
                typeof(bool));
            timeToLiveField = RequireField(textBoxType, "mTTL", typeof(float));
            growField = RequireField(textBoxType, "mGrow", typeof(bool));
            scaleField = RequireField(textBoxType, "mScale", typeof(float));

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

        private static FieldInfo RequireField(
            Type type,
            string name,
            string fieldTypeName)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType.FullName != fieldTypeName)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static void Prefix(object __instance)
        {
            if (obtainedTextBoxField == null || ownerField == null ||
                sceneField == null || automaticAdvanceField == null ||
                timeToLiveField == null || growField == null || scaleField == null)
                throw new InvalidOperationException(
                    "Player text-box cleanup contract has not been initialized.");

            object textBox = obtainedTextBoxField.GetValue(__instance);
            if (textBox == null)
                return;

            ownerField.SetValue(textBox, null);
            sceneField.SetValue(textBox, null);
            automaticAdvanceField.SetValue(textBox, false);
            timeToLiveField.SetValue(textBox, 0f);
            growField.SetValue(textBox, false);
            scaleField.SetValue(textBox, 0f);
        }
    }
}
