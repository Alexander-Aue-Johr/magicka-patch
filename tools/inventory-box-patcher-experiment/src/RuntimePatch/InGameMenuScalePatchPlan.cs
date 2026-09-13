using System.Threading;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class InGameMenuScalePatchPlan
    {
        private static int applied;

        internal static void ApplyDeferred()
        {
            if (Interlocked.CompareExchange(ref applied, 1, 0) != 0)
                return;
            ApplyTo(Assembly.GetEntryAssembly());
        }

        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version.Minor < 10)
                return;
            RuntimePatchSession.Apply(assembly,
                InGameMenuScalePatch.ShowDefinition);
            RuntimePatchSession.Apply(assembly,
                InGameMenuScalePatch.PositionsDefinition);
            System.Collections.Generic.IList<RuntimePatchDefinition> mouse =
                InGameMenuScalePatch.CreateMouseDefinitions(assembly);
            for (int index = 0; index < mouse.Count; index++)
                RuntimePatchSession.Apply(assembly, mouse[index]);
        }
    }
}
