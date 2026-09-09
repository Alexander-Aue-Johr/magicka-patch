using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class CharacterTemplateCachePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!CharacterTemplateCachePatch.IsAvailableIn(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    CharacterTemplateCachePatch.Definition,
                    "The paired character and avatar template caches are not declared in this Magicka version.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                CharacterTemplateCachePatch.Definition);
        }
    }
}
