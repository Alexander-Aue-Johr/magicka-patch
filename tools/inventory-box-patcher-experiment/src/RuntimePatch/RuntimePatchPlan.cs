using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class RuntimePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            RuntimePatchAudit.BeginRun(targetAssembly);
            RuntimePatchSession.Apply(
                targetAssembly,
                InventoryBoxDrawPatch.Definition);
            HUDManagerPatchPlan.ApplyTo(targetAssembly);
            PlayStatePatchPlan.ApplyTo(targetAssembly);
        }
    }
}
