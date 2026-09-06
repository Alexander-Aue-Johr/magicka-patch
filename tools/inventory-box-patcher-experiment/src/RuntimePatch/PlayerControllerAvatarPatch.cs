using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PlayerControllerAvatarPatch
    {
        private static FieldInfo playerAvatarField;
        private static MethodInfo controllerGetter;
        private static FieldInfo controllerAvatarField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Player controller avatar release",
                "org.magickacommunitypatch.player-controller-avatar-release",
                FindPlayerAvatarSetter,
                CreatePrefix);

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            return typeof(PlayerControllerAvatarPatch).GetMethod("Prefix");
        }

        private static MethodInfo FindPlayerAvatarSetter(Assembly targetAssembly)
        {
            Type avatarType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Avatar",
                true);
            Type controllerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.Controller",
                true);
            controllerAvatarField = RequireField(
                controllerType,
                "mAvatar",
                avatarType,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);

            Type playerType = targetAssembly.GetType("Magicka.GameLogic.Player", true);
            playerAvatarField = RequireField(
                playerType,
                "mAvatar",
                typeof(WeakReference),
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            PropertyInfo controllerProperty = playerType.GetProperty(
                "Controller",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            controllerGetter = controllerProperty == null
                ? null
                : controllerProperty.GetGetMethod(true);
            if (controllerGetter == null ||
                controllerGetter.ReturnType != controllerType ||
                controllerGetter.GetParameters().Length != 0)
                throw new MissingMethodException(playerType.FullName, "get_Controller");

            PropertyInfo avatarProperty = playerType.GetProperty(
                "Avatar",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            MethodInfo setter = avatarProperty == null
                ? null
                : avatarProperty.GetSetMethod();
            if (setter == null || setter.ReturnType != typeof(void))
                throw new MissingMethodException(playerType.FullName, "set_Avatar");
            ParameterInfo[] parameters = setter.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != avatarType)
                throw new MissingMethodException(playerType.FullName, "set_Avatar");
            return setter;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType,
            BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static void Prefix(object __instance, object value)
        {
            if (value != null)
                return;
            if (playerAvatarField == null || controllerGetter == null ||
                controllerAvatarField == null)
                throw new InvalidOperationException(
                    "Player controller-avatar contract has not been initialized.");

            WeakReference avatarReference =
                (WeakReference)playerAvatarField.GetValue(__instance);
            object expected = avatarReference.Target;
            object controller = controllerGetter.Invoke(__instance, null);
            if (controller != null && Object.ReferenceEquals(
                controllerAvatarField.GetValue(controller),
                expected))
                controllerAvatarField.SetValue(controller, null);
        }
    }
}
