using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ItemWeaponCachePatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            RuntimePatchSession.Apply(assembly,
                ItemWeaponCachePatch.ReadDefinition);
            RuntimePatchSession.Apply(assembly,
                ItemWeaponCachePatch.CacheDefinition);
            RuntimePatchSession.Apply(assembly,
                ItemWeaponCachePatch.GetDefinition);
            if (assembly.GetName().Version.Minor >= 10)
                RuntimePatchSession.Apply(assembly,
                    ItemWeaponCachePatch.HasDefinition);
            else
                RuntimePatchAudit.WriteNotApplicable(
                    ItemWeaponCachePatch.HasDefinition,
                    "The legacy Item type has no HasCachedWeapon method.");
            RuntimePatchSession.Apply(assembly,
                ItemWeaponCachePatch.CopyDefinition);
            RuntimePatchSession.Apply(assembly,
                ItemWeaponCachePatch.ReleaseDefinition);
        }
    }
}
