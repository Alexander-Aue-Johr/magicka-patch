using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class HUDManagerInitialisePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Postfix(
                "HUDManager original HUD enable",
                "org.magickacommunitypatch.hud-manager-original-hud-enable",
                HUDManagerTargetMethods.FindInitialiseIn,
                target => typeof(HUDManagerInitialisePatch).GetMethod("Postfix"));

        public static void Postfix(object __instance)
        {
            RuntimeMember.WriteProperty(__instance, "UIEnabled", true);
        }
    }
}
