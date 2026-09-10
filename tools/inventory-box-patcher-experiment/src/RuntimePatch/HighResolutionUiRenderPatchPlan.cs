using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class HighResolutionUiRenderPatchPlan
    {
        private static readonly RuntimePatchDefinition[] Definitions =
            new RuntimePatchDefinition[]
            {
                HighResolutionUiRenderPatch.GameDefinition,
                HighResolutionUiRenderPatch.TextBoxDefinition,
                HighResolutionUiRenderPatch.CutsceneDefinition,
                HighResolutionUiRenderPatch.IconDefinition,
                HighResolutionUiRenderPatch.SpellWheelDefinition,
                HighResolutionUiRenderPatch.NotifierPrefixDefinition,
                HighResolutionUiRenderPatch.NotifierPostfixDefinition
            };

        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version.Minor < 10)
            {
                for (int index = 0; index < Definitions.Length; index++)
                    RuntimePatchAudit.WriteNotApplicable(
                        Definitions[index],
                        "The legacy executable predates high-resolution UI scaling.");
                return;
            }
            for (int index = 0; index < Definitions.Length; index++)
                RuntimePatchSession.Apply(assembly, Definitions[index]);
        }
    }
}
