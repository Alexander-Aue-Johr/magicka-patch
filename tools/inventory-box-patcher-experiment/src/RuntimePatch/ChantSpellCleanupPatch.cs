using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ChantSpellCleanupPatch
    {
        private static FieldInfo initializedField;
        private static FieldInfo chantSpellsField;
        private static FieldInfo activeField;
        private static MethodInfo stopMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Active chant-spell level cleanup",
                "org.magickacommunitypatch.chant-spell-cleanup",
                FindTarget,
                target => typeof(ChantSpellCleanupPatch).GetMethod("Prefix"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type managerType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ChantSpellManager",
                true);
            Type chantSpellType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ChantSpells",
                true);

            initializedField = playStateType.GetField(
                "mInitialized",
                BindingFlags.Instance | BindingFlags.NonPublic);
            chantSpellsField = managerType.GetField(
                "sChantSpells",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            activeField = chantSpellType.GetField(
                "Active",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            stopMethod = chantSpellType.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);

            if (initializedField == null || initializedField.FieldType != typeof(bool))
                throw new MissingFieldException(playStateType.FullName, "mInitialized");
            if (chantSpellsField == null ||
                !chantSpellsField.FieldType.IsArray ||
                chantSpellsField.FieldType.GetElementType() != chantSpellType)
                throw new MissingFieldException(managerType.FullName, "sChantSpells");
            if (activeField == null || activeField.FieldType != typeof(bool))
                throw new MissingFieldException(chantSpellType.FullName, "Active");
            if (stopMethod == null || stopMethod.ReturnType != typeof(void))
                throw new MissingMethodException(chantSpellType.FullName, "Stop");

            MethodInfo dispose = playStateType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(playStateType.FullName, "Dispose");
            return dispose;
        }

        public static void Prefix(object __instance)
        {
            if (!(bool)initializedField.GetValue(__instance))
                return;

            Array spells = (Array)chantSpellsField.GetValue(null);
            if (spells == null)
                return;
            for (int index = 0; index < spells.Length; index++)
            {
                object spell = spells.GetValue(index);
                if ((bool)activeField.GetValue(spell))
                    Stop(spell);
            }
        }

        private static void Stop(object spell)
        {
            try
            {
                stopMethod.Invoke(spell, null);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }
    }
}
