using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class InGameMenuPlayStatePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            RuntimePatchSession.Apply(
                targetAssembly,
                InGameMenuPlayStatePatch.InitializeDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                InGameMenuPlayStatePatch.InstallDefinition);
            InGameMenuPlayStatePatch.ValidateDeferredDefinitions(
                targetAssembly);
        }
    }
}
