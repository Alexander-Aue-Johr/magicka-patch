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
                AvatarFindInteractablePatch.Definition);
            RuntimePatchSession.Apply(
                targetAssembly,
                AIStateAttackOnExecutePatch.Definition);
            RuntimePatchSession.Apply(
                targetAssembly,
                EntityManagerClosestDamageablePatch.Definition);
            RuntimePatchSession.Apply(
                targetAssembly,
                HelperArrayEqualsPatch.Definition);
            RuntimePatchSession.Apply(
                targetAssembly,
                InventoryBoxDrawPatch.Definition);
            HUDManagerPatchPlan.ApplyTo(targetAssembly);
            RuntimePatchSession.Apply(
                targetAssembly,
                MachineNetworkInitializePatch.Definition);
            PlayStatePatchPlan.ApplyTo(targetAssembly);
        }
    }
}
