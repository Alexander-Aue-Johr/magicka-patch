using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SubMenuMainPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!SubMenuMainControllerBackPatch.IsAvailableIn(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    SubMenuMainControllerBackPatch.Definition,
                    "SubMenuMain.ControllerB is not declared in this Magicka version.");
                return;
            }

            RuntimePatchSession.Apply(
                targetAssembly,
                SubMenuMainControllerBackPatch.Definition);
        }
    }
}
