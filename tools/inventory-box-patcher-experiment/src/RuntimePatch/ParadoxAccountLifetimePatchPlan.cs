using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ParadoxAccountLifetimePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (targetAssembly.GetType(
                "Magicka.Storage.ParadoxAccountSaveData",
                false) == null ||
                targetAssembly.GetType(
                    "Magicka.Misc.ScopedSingleton`1",
                    false) == null)
            {
                const string reason =
                    "The scoped ParadoxAccountSaveData lifetime is not present in this Magicka version.";
                RuntimePatchAudit.WriteNotApplicable(
                    ParadoxAccountLifetimePatch.MenuExitDefinition,
                    reason);
                RuntimePatchAudit.WriteNotApplicable(
                    ParadoxAccountLifetimePatch.EndRunDefinition,
                    reason);
                return;
            }

            RuntimePatchSession.Apply(
                targetAssembly,
                ParadoxAccountLifetimePatch.MenuExitDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                ParadoxAccountLifetimePatch.EndRunDefinition);
        }
    }
}
