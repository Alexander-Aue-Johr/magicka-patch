using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class CharacterSelectOfflineLeavePatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version.Minor < 10)
                return;
            RuntimePatchSession.Apply(
                assembly, CharacterSelectOfflineLeavePatch.Definition);
        }
    }
}
