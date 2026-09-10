using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class TomeSupporterPatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetType(
                "Magicka.WebTools.Paradox.ParadoxPopupUtils", false) == null)
            {
                RuntimePatchAudit.WriteNotApplicable(
                    TomeSupporterPatch.Definition,
                    "The legacy executable predates the Paradox popup system.");
                return;
            }
            RuntimePatchSession.Apply(assembly, TomeSupporterPatch.Definition);
        }
    }
}
