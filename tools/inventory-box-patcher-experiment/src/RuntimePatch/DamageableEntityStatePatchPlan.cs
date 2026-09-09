using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class DamageableEntityStatePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!DamageableEntityStatePatch.IsAvailableIn(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    DamageableEntityStatePatch.Definition,
                    "This Magicka version has no DamageablePhysicsEntity reuse pool.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                DamageableEntityStatePatch.Definition);
        }
    }
}
