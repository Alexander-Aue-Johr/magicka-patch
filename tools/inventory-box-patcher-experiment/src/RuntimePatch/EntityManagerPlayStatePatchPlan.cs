using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class EntityManagerPlayStatePatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version.Minor < 10)
            {
                RuntimePatchAudit.WriteNotApplicable(
                    EntityManagerPlayStatePatch.Definition,
                    "The legacy EntityManager cache-lifetime contract differs.");
                return;
            }
            RuntimePatchSession.Apply(
                assembly,
                EntityManagerPlayStatePatch.Definition);
        }
    }
}
