using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ParadoxStorePriceUpdatePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (targetAssembly.GetType(
                "Magicka.CoreFramework.GameSystem.Store.StoreItemDatabase",
                false) == null)
            {
                RuntimePatchAudit.WriteNotApplicable(
                    ParadoxStorePriceUpdatePatch.Definition,
                    "The legacy Paradox store database is not present in this Magicka version.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                ParadoxStorePriceUpdatePatch.Definition);
        }
    }
}
