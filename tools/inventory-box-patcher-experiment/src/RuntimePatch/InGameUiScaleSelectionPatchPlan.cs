using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class InGameUiScaleSelectionPatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version.Minor < 10)
                return;
            RuntimePatchDefinition[] definitions =
                InGameUiScaleSelectionPatch.Definitions;
            for (int index = 0; index < definitions.Length; index++)
                RuntimePatchSession.Apply(assembly, definitions[index]);
        }
    }
}
