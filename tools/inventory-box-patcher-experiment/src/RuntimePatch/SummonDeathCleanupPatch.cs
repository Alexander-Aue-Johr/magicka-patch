using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class SummonDeathCleanupPatch
    {
        private const string SummonDeathTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
            "SummonDeath";

        private static FieldInfo singletonField;
        private static FieldInfo deathField;
        private static MethodInfo clearHandlesMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "SummonDeath singleton entity cleanup",
                "org.magickacommunitypatch.summon-death-entity-cleanup",
                FindPlayStateDispose,
                typeof(SummonDeathCleanupPatch).GetMethod("Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type summonDeath = targetAssembly.GetType(
                SummonDeathTypeName,
                true);
            Type death = summonDeath.GetNestedType(
                "MagickDeath",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (death == null)
                throw new TypeLoadException(SummonDeathTypeName + "+MagickDeath");
            singletonField = RequireField(
                summonDeath,
                "mSingelton",
                summonDeath,
                true);
            deathField = RequireField(
                summonDeath,
                "mDeath",
                death,
                false);

            Type entity = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            clearHandlesMethod = entity.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clearHandlesMethod == null ||
                clearHandlesMethod.ReturnType != typeof(void))
                throw new MissingMethodException(entity.FullName, "ClearHandles");

            Type playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            MethodInfo dispose = playState.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(playState.FullName, "Dispose");
            return dispose;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int anchor = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo method = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(method, clearHandlesMethod))
                {
                    anchor = index;
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one Entity.ClearHandles call in " +
                    "PlayState.Dispose, found " + matches + ".");
            result.Insert(
                anchor,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(SummonDeathCleanupPatch).GetMethod(
                        "ReleaseDeathEntity")));
            return result;
        }

        public static void ReleaseDeathEntity()
        {
            if (singletonField == null || deathField == null)
                throw new InvalidOperationException(
                    "SummonDeath cleanup contract has not been initialized.");
            object singleton = singletonField.GetValue(null);
            if (singleton != null)
                deathField.SetValue(singleton, null);
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type expectedType,
            bool isStatic)
        {
            BindingFlags flags = BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
