using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PropBossTeardownPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!PropBossTeardownPatch.HasTarget(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    PropBossTeardownPatch.Definition,
                    "PropBoss is not present in this Magicka version.");
                return;
            }
            RuntimePatchSession.Apply(
                targetAssembly,
                PropBossTeardownPatch.Definition);
        }
    }

    public static class PropBossTeardownPatch
    {
        private static Type propBossType;
        private static FieldInfo typeField;
        private static FieldInfo instancesField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "PropBoss level teardown",
                "org.magickacommunitypatch.prop-boss-level-teardown",
                FindClearHandles,
                target => typeof(PropBossTeardownPatch).GetMethod("Prefix"));

        internal static bool HasTarget(Assembly assembly)
        {
            return assembly.GetType(
                "Magicka.GameLogic.Entities.Bosses.PropBoss",
                false) != null;
        }

        private static MethodInfo FindClearHandles(Assembly assembly)
        {
            Type entityType = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            propBossType = assembly.GetType(
                "Magicka.GameLogic.Entities.Bosses.PropBoss",
                true);
            typeField = propBossType.GetField(
                "mType",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            instancesField = entityType.GetField(
                "mInstances",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            MethodInfo method = entityType.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (typeField == null || typeField.FieldType != typeof(string))
                throw new MissingFieldException(propBossType.FullName, "mType");
            if (instancesField == null ||
                !typeof(IList).IsAssignableFrom(instancesField.FieldType))
                throw new MissingFieldException(entityType.FullName, "mInstances");
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(entityType.FullName, "ClearHandles");
            return method;
        }

        public static void Prefix()
        {
            IList instances = instancesField.GetValue(null) as IList;
            if (instances == null)
                return;
            for (int index = 0; index < instances.Count; index++)
            {
                object entity = instances[index];
                if (entity != null && propBossType.IsInstanceOfType(entity))
                    typeField.SetValue(entity, null);
            }
        }
    }
}
