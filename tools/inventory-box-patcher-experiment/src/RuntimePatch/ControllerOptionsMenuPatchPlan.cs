using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ControllerOptionsMenuPatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version < new Version(1, 10, 0, 0))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    ControllerOptionsMenuPatch.ConstructorDefinition,
                    "The 1.4/1.5 options menu has a different virtual contract.");
                RuntimePatchAudit.WriteNotApplicable(
                    ControllerOptionsMenuPatch.SelectDefinition,
                    "The 1.4/1.5 options menu has a different virtual contract.");
                RuntimePatchAudit.WriteNotApplicable(
                    ControllerOptionsMenuPatch.HighlightDefinition,
                    "The 1.4/1.5 options menu has a different virtual contract.");
                return;
            }
            RuntimePatchSession.Apply(assembly,
                ControllerOptionsMenuPatch.ConstructorDefinition);
            RuntimePatchSession.Apply(assembly,
                ControllerOptionsMenuPatch.SelectDefinition);
            RuntimePatchSession.Apply(assembly,
                ControllerOptionsMenuPatch.HighlightDefinition);
        }
    }
}
