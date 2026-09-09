using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PhysicsEntityTemplateCachePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!PhysicsEntityTemplateCachePatch.IsAvailableIn(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    PhysicsEntityTemplateCachePatch.Definition,
                    "PhysicsEntityTemplate.sCache is not declared in this Magicka version.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                PhysicsEntityTemplateCachePatch.Definition);
        }
    }
}
