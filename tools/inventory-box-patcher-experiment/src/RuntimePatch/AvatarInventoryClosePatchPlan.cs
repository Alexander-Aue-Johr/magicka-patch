using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class AvatarInventoryClosePatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            RuntimePatchSession.Apply(
                assembly, AvatarInventoryClosePatch.Definition);
        }
    }
}
