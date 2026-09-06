using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ParadoxPopupPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!ParadoxPopupPatch.HasSupportedPopup(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    ParadoxPopupPatch.Definition,
                    "This Magicka version does not use the 1.10 Paradox popup API.");
                return;
            }
            RuntimePatchSession.Apply(targetAssembly, ParadoxPopupPatch.Definition);
        }
    }
}
