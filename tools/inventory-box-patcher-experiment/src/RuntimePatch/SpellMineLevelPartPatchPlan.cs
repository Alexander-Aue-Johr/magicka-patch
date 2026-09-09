using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SpellMineLevelPartPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!SpellMineLevelPartPatch.IsAvailableIn(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    SpellMineLevelPartPatch.Definition,
                    "SpellMine has no animated-level-part field in this Magicka version.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                SpellMineLevelPartPatch.Definition);
        }
    }
}
