using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class HUDManagerPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!HUDManagerTargetMethods.IsAvailableIn(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    HUDManagerInitialisePatch.Definition,
                    "HUDManager.Initialise is not present in this Magicka version.");
                return;
            }

            RuntimePatchSession.Apply(
                targetAssembly,
                HUDManagerInitialisePatch.Definition);
        }
    }
}
