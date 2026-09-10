using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ElementSelectionTelemetryPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            Version version = targetAssembly.GetName().Version;
            if (version == null || version < new Version(1, 10, 0, 0))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    ElementSelectionTelemetryPatch.KeyboardDefinition,
                    "Element-selection telemetry was introduced for the current 1.10 patch reference.");
                RuntimePatchAudit.WriteNotApplicable(
                    ElementSelectionTelemetryPatch.DirectInputDefinition,
                    "Element-selection telemetry was introduced for the current 1.10 patch reference.");
                RuntimePatchAudit.WriteNotApplicable(
                    ElementSelectionTelemetryPatch.XInputDefinition,
                    "Element-selection telemetry was introduced for the current 1.10 patch reference.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                ElementSelectionTelemetryPatch.KeyboardDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                ElementSelectionTelemetryPatch.DirectInputDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                ElementSelectionTelemetryPatch.XInputDefinition);
        }
    }
}
