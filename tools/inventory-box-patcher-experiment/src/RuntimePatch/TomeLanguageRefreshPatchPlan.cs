using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class TomeLanguageRefreshPatchPlan
    {
        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version.Minor < 10)
            {
                RuntimePatchAudit.WriteNotApplicable(
                    TomeLanguageRefreshPatch.Definition,
                    "The legacy executable predates the account widgets.");
                return;
            }
            RuntimePatchSession.Apply(
                assembly,
                TomeLanguageRefreshPatch.Definition);
        }
    }
}
