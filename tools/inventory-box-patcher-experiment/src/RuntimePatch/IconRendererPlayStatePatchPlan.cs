using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class IconRendererPlayStatePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!IconRendererPlayStatePatch.IsAvailableIn(targetAssembly))
            {
                const string reason =
                    "IconRenderer play-state contract is not present in this Magicka version.";
                RuntimePatchAudit.WriteNotApplicable(
                    IconRendererPlayStatePatch.ConstructorDefinition,
                    reason);
                RuntimePatchAudit.WriteNotApplicable(
                    IconRendererPlayStatePatch.InitializeDefinition,
                    reason);
                RuntimePatchAudit.WriteNotApplicable(
                    IconRendererPlayStatePatch.SetterDefinition,
                    reason);
                return;
            }

            RuntimePatchSession.Apply(
                targetAssembly,
                IconRendererPlayStatePatch.ConstructorDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                IconRendererPlayStatePatch.InitializeDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                IconRendererPlayStatePatch.SetterDefinition);
        }
    }
}
