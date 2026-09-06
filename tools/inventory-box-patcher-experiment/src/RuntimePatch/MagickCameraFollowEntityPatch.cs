using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MagickCameraFollowEntityPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "MagickCamera detached follow target guard",
                "org.magickacommunitypatch.magick-camera-follow-entity",
                MagickCameraTargetMethods.FindUpdateIn,
                target => typeof(MagickCameraFollowEntityPatch).GetMethod("Prefix"));

        public static void Prefix(object __instance)
        {
            object behavior = RuntimeMember.ReadField(__instance, "mCurrentBehaviour");
            if (behavior.ToString() != "FollowEntity")
                return;

            object following = RuntimeMember.ReadField(__instance, "mFollowing");
            if (following != null && RuntimeMember.ReadProperty(following, "Body") == null)
                RuntimeMember.WriteField(__instance, "mFollowing", null);
        }
    }
}
