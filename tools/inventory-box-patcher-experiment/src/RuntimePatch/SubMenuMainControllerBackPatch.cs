using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class SubMenuMainControllerBackPatch
    {
        private static Type keyboardMouseControllerType;
        private static MethodInfo showExitConfirmation;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "SubMenuMain controller exit confirmation",
                "org.magickacommunitypatch.sub-menu-main-controller-back",
                FindTarget,
                target => typeof(SubMenuMainControllerBackPatch).GetMethod("Prefix"));

        internal static bool IsAvailableIn(Assembly targetAssembly)
        {
            Type subMenuType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuMain",
                false);
            Type controllerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.Controller",
                false);
            if (subMenuType == null || controllerType == null)
                return false;
            return subMenuType.GetMethod(
                "ControllerB",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { controllerType },
                null) != null;
        }

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type subMenuType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuMain",
                true);
            Type controllerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.Controller",
                true);
            keyboardMouseControllerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.KeyboardMouseController",
                true);
            if (!controllerType.IsAssignableFrom(keyboardMouseControllerType))
                throw new InvalidOperationException(
                    "KeyboardMouseController does not inherit Controller.");

            showExitConfirmation = subMenuType.GetMethod(
                "ShowRUSure",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            MethodInfo target = subMenuType.GetMethod(
                "ControllerB",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { controllerType },
                null);
            if (showExitConfirmation == null ||
                showExitConfirmation.ReturnType != typeof(void))
                throw new MissingMethodException(subMenuType.FullName, "ShowRUSure");
            if (target == null || target.ReturnType != typeof(void))
                throw new MissingMethodException(subMenuType.FullName, "ControllerB");
            return target;
        }

        public static bool Prefix(object __instance, object iSender)
        {
            if (keyboardMouseControllerType.IsInstanceOfType(iSender))
                return true;

            showExitConfirmation.Invoke(__instance, null);
            return false;
        }
    }
}
