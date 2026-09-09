using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameSceneMenuControllerResetPatch
    {
        private static MethodInfo controlManagerInstanceGetter;
        private static FieldInfo controlManagerSingletonField;
        private static FieldInfo menuControllerField;
        private static MethodInfo clearMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "GameScene menu-controller reset",
                "org.magickacommunitypatch.game-scene-menu-controller-reset",
                FindDestroy,
                target => typeof(GameSceneMenuControllerResetPatch).GetMethod(
                    "Prefix"));

        private static MethodInfo FindDestroy(Assembly assembly)
        {
            Type gameScene = assembly.GetType(
                "Magicka.Levels.GameScene",
                true);
            Type controlManager = assembly.GetType(
                "Magicka.GameLogic.Controls.ControlManager",
                true);
            Type keyboardMouse = assembly.GetType(
                "Magicka.GameLogic.Controls.KeyboardMouseController",
                true);

            controlManagerInstanceGetter = RequireGetter(
                controlManager,
                "Instance",
                controlManager,
                true);
            controlManagerSingletonField = RequireField(
                controlManager,
                "mSingelton",
                true,
                controlManager);
            menuControllerField = RequireField(
                controlManager,
                "mMenuController",
                false,
                keyboardMouse);
            clearMethod = keyboardMouse.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (clearMethod == null || clearMethod.ReturnType != typeof(void))
                throw new MissingMethodException(keyboardMouse.FullName, "Clear");

            MethodInfo destroy = gameScene.GetMethod(
                "Destroy",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(bool) },
                null);
            if (destroy == null || destroy.ReturnType != typeof(void))
                throw new MissingMethodException(gameScene.FullName, "Destroy");
            return destroy;
        }

        public static void Prefix()
        {
            object manager = controlManagerSingletonField.GetValue(null);
            if (manager == null)
                manager = controlManagerInstanceGetter.Invoke(null, null);
            if (manager == null)
                throw new InvalidOperationException(
                    "ControlManager.Instance returned null during scene teardown.");
            object controller = menuControllerField.GetValue(manager);
            if (controller == null)
                throw new InvalidOperationException(
                    "ControlManager.MenuController returned null during scene teardown.");
            try
            {
                clearMethod.Invoke(controller, null);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Could not clear the menu controller during scene teardown.",
                    exception);
            }
        }

        private static MethodInfo RequireGetter(
            Type type,
            string name,
            Type returnType,
            bool isStatic)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            PropertyInfo property = type.GetProperty(name, flags);
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod(true);
            if (getter == null || getter.ReturnType != returnType ||
                getter.IsStatic != isStatic)
                throw new MissingMethodException(type.FullName, "get_" + name);
            return getter;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            bool isStatic,
            Type fieldType)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.IsStatic != isStatic ||
                field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
