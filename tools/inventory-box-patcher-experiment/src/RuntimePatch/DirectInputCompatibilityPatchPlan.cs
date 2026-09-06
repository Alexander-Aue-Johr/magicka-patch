using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class DirectInputCompatibilityPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            RuntimePatchSession.Apply(
                targetAssembly,
                DirectInputCompatibilityPatch.OptionsConstructorDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                DirectInputCompatibilityPatch.OptionsOnEnterDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                DirectInputCompatibilityPatch.ControllerScanDefinition);

            if (!DirectInputCompatibilityPatch.HasWarningSupport(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    DirectInputCompatibilityPatch.WarningDefinition,
                    "The in-game Paradox popup system is not present in this Magicka version.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                DirectInputCompatibilityPatch.WarningDefinition);
        }
    }
}
