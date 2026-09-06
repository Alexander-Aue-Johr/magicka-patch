using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class InteractableHighlightPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Interactable detached scene highlight guard",
                "org.magickacommunitypatch.interactable-highlight-scene",
                FindTarget,
                target => typeof(InteractableHighlightPatch).GetMethod("Prefix"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type type = targetAssembly.GetType(
                "Magicka.Levels.Triggers.Interactable",
                true);
            MethodInfo method = type.GetMethod(
                "Highlight",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, "Highlight");
            return method;
        }

        public static bool Prefix(object __instance)
        {
            object scene = RuntimeMember.ReadField(__instance, "mGameScene");
            return scene != null &&
                RuntimeMember.ReadProperty(scene, "LevelModel") != null;
        }
    }
}
