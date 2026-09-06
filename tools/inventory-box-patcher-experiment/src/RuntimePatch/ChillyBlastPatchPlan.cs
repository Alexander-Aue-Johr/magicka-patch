using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ChillyBlastPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!ChillyBlastPlayStatePatch.IsAvailableIn(targetAssembly))
            {
                const string reason =
                    "ChillyBlast is not declared in this Magicka version.";
                RuntimePatchAudit.WriteNotApplicable(
                    ChillyBlastPlayStatePatch.ExecuteDefinition,
                    reason);
                RuntimePatchAudit.WriteNotApplicable(
                    ChillyBlastPlayStatePatch.UpdateDefinition,
                    reason);
                return;
            }

            RuntimePatchSession.Apply(
                targetAssembly,
                ChillyBlastPlayStatePatch.ExecuteDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                ChillyBlastPlayStatePatch.UpdateDefinition);
        }
    }
}
