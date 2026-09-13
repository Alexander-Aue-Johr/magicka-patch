using System.Reflection;
using System.Threading;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ItemWeaponCachePatchPlan
    {
        private static int deferredApplied;

        internal static void ApplyTo(Assembly assembly)
        {
            RuntimePatchSession.Apply(assembly,
                ItemWeaponCachePatch.ReadDefinition);
            RuntimePatchSession.Apply(assembly,
                ItemWeaponCachePatch.CacheDefinition);
            RuntimePatchSession.Apply(assembly,
                ItemWeaponCachePatch.GetDefinition);
        }

        internal static void ApplyDeferred()
        {
            if (Interlocked.CompareExchange(ref deferredApplied, 1, 0) != 0)
                return;
            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly == null)
                throw new InvalidOperationException(
                    "The Magicka entry assembly is unavailable.");
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
