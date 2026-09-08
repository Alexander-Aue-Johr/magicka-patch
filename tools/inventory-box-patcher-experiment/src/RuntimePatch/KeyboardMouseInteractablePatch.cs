using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class KeyboardMouseInteractablePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Keyboard mouse detached interaction guard",
                "org.magickacommunitypatch.keyboard-mouse-interactable",
                FindTarget,
                CreatePrefix);

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type controllerType = targetAssembly.GetType(
                "Magicka.GameLogic.Controls.KeyboardMouseController",
                true);
            Type segmentType = RuntimeMember.FindLoadedType(
                "JigLibX.Geometry.Segment");
            MethodInfo target = controllerType.GetMethod(
                "FindInteractable",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { segmentType.MakeByRefType() },
                null);
            if (target == null || target.ReturnType.FullName !=
                "Magicka.Levels.Triggers.Interactable")
            {
                throw new MissingMethodException(
                    controllerType.FullName,
                    "FindInteractable");
            }
            return target;
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            Type adapter = typeof(KeyboardMouseInteractablePrefix<,>)
                .MakeGenericType(target.DeclaringType, target.ReturnType);
            return adapter.GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.Public);
        }

        public static bool Prefix(object __instance)
        {
            if (__instance == null)
                return true;

            object avatar = RuntimeMember.ReadField(__instance, "mAvatar");
            if (avatar == null)
                return false;

            object playState = RuntimeMember.ReadProperty(avatar, "PlayState");
            if (playState == null)
                return false;

            object level = RuntimeMember.ReadProperty(playState, "Level");
            if (level == null)
                return false;

            object scene = RuntimeMember.ReadProperty(level, "CurrentScene");
            if (scene == null)
                return false;

            return RuntimeMember.ReadProperty(scene, "Triggers") != null;
        }
    }

    public static class KeyboardMouseInteractablePrefix<TInstance, TResult>
    {
        public static bool Prefix(TInstance __instance, ref TResult __result)
        {
            bool runOriginal = KeyboardMouseInteractablePatch.Prefix(__instance);
            if (!runOriginal)
                __result = default(TResult);
            return runOriginal;
        }
    }
}
