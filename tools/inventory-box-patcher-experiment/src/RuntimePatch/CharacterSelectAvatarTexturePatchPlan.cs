using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class CharacterSelectAvatarTexturePatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version.Minor < 10)
                return;
            RuntimePatchSession.Apply(
                assembly,
                CharacterSelectAvatarTexturePatch.Definition);
        }
    }
}
