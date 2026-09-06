using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ControlManagerPlayerLockPatch
    {
        internal static readonly RuntimePatchDefinition LockDefinition =
            RuntimePatchDefinition.Prefix(
                "ControlManager lock detached controller guard",
                "org.magickacommunitypatch.control-manager-lock-player-input",
                assembly => FindTarget(assembly, "LockPlayerInput", typeof(void)),
                target => typeof(ControlManagerPlayerLockPatch).GetMethod("VoidPrefix"));

        internal static readonly RuntimePatchDefinition QueryDefinition =
            RuntimePatchDefinition.Prefix(
                "ControlManager query detached controller guard",
                "org.magickacommunitypatch.control-manager-is-player-input-locked",
                assembly => FindTarget(assembly, "IsPlayerInputLocked", typeof(bool)),
                target => typeof(ControlManagerPlayerLockPatch).GetMethod("QueryPrefix"));

        internal static readonly RuntimePatchDefinition UnlockDefinition =
            RuntimePatchDefinition.Prefix(
                "ControlManager unlock detached controller guard",
                "org.magickacommunitypatch.control-manager-unlock-player-input",
                assembly => FindTarget(assembly, "UnlockPlayerInput", typeof(void)),
                target => typeof(ControlManagerPlayerLockPatch).GetMethod("VoidPrefix"));

        private static MethodInfo FindTarget(
            Assembly targetAssembly,
            string name,
            Type returnType)
        {
            Type managerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.ControlManager",
                true);
            Type controllerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.Controller",
                true);
            MethodInfo method = managerType.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { controllerType },
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(managerType.FullName, name);
            return method;
        }

        public static bool VoidPrefix(object iSender)
        {
            return HasPlayer(iSender);
        }

        public static bool QueryPrefix(object iSender, ref bool __result)
        {
            if (HasPlayer(iSender))
                return true;
            __result = false;
            return false;
        }

        private static bool HasPlayer(object controller)
        {
            return controller != null &&
                RuntimeMember.ReadProperty(controller, "Player") != null;
        }
    }
}
