using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class CharacterSelectWidgetTexturePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!CharacterSelectWidgetTexturePatch.IsAvailableIn(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    CharacterSelectWidgetTexturePatch.Definition,
                    "UISystem image widgets are not present in this Magicka version.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                CharacterSelectWidgetTexturePatch.Definition);
        }
    }
}
