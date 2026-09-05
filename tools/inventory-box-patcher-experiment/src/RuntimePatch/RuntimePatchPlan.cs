using System.Reflection;

namespace Magicka.InventoryBoxRuntimePatch
{
    internal static class RuntimePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            RuntimePatchAudit.BeginRun(targetAssembly);
            RuntimePatchSession.Apply(
                targetAssembly,
                InventoryBoxDrawTranspiler.Definition);
            PlayStatePatchPlan.ApplyTo(targetAssembly);
        }
    }
}
