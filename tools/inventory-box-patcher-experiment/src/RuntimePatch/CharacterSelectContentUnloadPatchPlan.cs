using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class CharacterSelectContentUnloadPatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            if (!CharacterSelectContentUnloadPatch.IsAvailableIn(assembly) ||
                assembly.GetName().Version.Minor < 10)
            {
                RuntimePatchAudit.WriteNotApplicable(
                    CharacterSelectContentUnloadPatch.Definition,
                    "The legacy executable predates the current Tome render pipeline.");
                return;
            }
            RuntimePatchSession.Apply(
                assembly,
                CharacterSelectContentUnloadPatch.Definition);
        }
    }
}
