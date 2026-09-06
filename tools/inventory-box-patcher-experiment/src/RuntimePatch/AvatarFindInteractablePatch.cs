using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AvatarFindInteractablePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Avatar detached interaction guard",
                "org.magickacommunitypatch.avatar-find-interactable",
                AvatarTargetMethods.FindFindInteractableIn,
                ExactPrefixAdapter.CreateNullResult);

        public static bool Prefix(object __instance)
        {
            if (__instance == null)
                return true;

            object playState = RuntimeMember.ReadField(__instance, "mPlayState");
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
}
