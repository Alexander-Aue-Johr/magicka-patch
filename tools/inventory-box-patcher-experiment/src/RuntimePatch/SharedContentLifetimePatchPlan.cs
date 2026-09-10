using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SharedContentLifetimePatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            System.Collections.Generic.IList<RuntimePatchDefinition> loads =
                SharedContentLifetimePatch.CreateLoadDefinitions(assembly);
            for (int index = 0; index < loads.Count; index++)
                RuntimePatchSession.Apply(assembly, loads[index]);
            RuntimePatchSession.Apply(assembly,
                SharedContentLifetimePatch.ReleaseDefinition);
            RuntimePatchSession.Apply(assembly,
                SharedContentLifetimePatch.FinalDefinition);
        }
    }
}
