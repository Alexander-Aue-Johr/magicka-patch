using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class EntityManagerClearAndStorePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Postfix(
                "EntityManager scene-transition grid cleanup",
                "org.magickacommunitypatch.entity-manager-clear-and-store",
                EntityManagerTargetMethods.FindClearAndStoreIn,
                target => typeof(EntityManagerClearAndStorePatch).GetMethod("Postfix"));

        public static void Postfix(object __instance)
        {
            MethodInfo updateQuadGrid = __instance.GetType().GetMethod(
                "UpdateQuadGrid",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (updateQuadGrid == null)
                throw new MissingMethodException(__instance.GetType().FullName, "UpdateQuadGrid");
            updateQuadGrid.Invoke(__instance, null);
        }
    }
}
