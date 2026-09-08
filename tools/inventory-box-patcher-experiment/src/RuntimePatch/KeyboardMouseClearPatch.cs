using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class KeyboardMouseClearPatch
    {
        private static FieldInfo cursorPressedTarget;
        private static FieldInfo lockedTarget;
        private static FieldInfo stillPressing;
        private static FieldInfo interactMoveLock;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Keyboard mouse stale target cleanup",
                "org.magickacommunitypatch.keyboard-mouse-clear",
                FindClear,
                target => typeof(KeyboardMouseClearPatch).GetMethod("Prefix"));

        private static MethodInfo FindClear(Assembly targetAssembly)
        {
            Type controllerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.KeyboardMouseController",
                true);
            MethodInfo clear = controllerType.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clear == null || clear.ReturnType != typeof(void))
                throw new MissingMethodException(controllerType.FullName, "Clear");
            cursorPressedTarget = RequireField(
                controllerType,
                "mCursorPressedTarget",
                typeof(object));
            lockedTarget = RequireField(
                controllerType,
                "mLockedTarget",
                typeof(object));
            stillPressing = RequireField(
                controllerType,
                "mStillPressing",
                typeof(bool));
            interactMoveLock = RequireField(
                controllerType,
                "mInteractMoveLock",
                typeof(bool));
            return clear;
        }

        public static void Prefix(object __instance)
        {
            cursorPressedTarget.SetValue(__instance, null);
            lockedTarget.SetValue(__instance, null);
            stillPressing.SetValue(__instance, false);
            interactMoveLock.SetValue(__instance, false);
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
