using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class StaticLevelPoolCleanupPatch
    {
        private static readonly string[][] PoolContracts = new string[][]
        {
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Revive", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonZombie", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Conflagration", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Wave", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.PerformanceEnchantment", "sCache", "sActiveEnchantments"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.GreaseLump", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.BreakBarriers", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.GreaseTrail", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.FloorStomp", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.VortexEntity", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Confuse", "sCache", "sActiveCaches"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grow", "sCache", "sActiveCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Polymorph", "sCache", "sActiveCaches"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Zap", "sCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.VladZap", "sCache"),
            Pool("Magicka.GameLogic.Spells.Railgun", "sCache", "sActiveRails"),
            Pool("Magicka.GameLogic.Spells.ArcaneBlast", "sCache"),
            Pool("Magicka.GameLogic.Spells.ArcaneBlade", "sCache"),
            Pool("Magicka.GameLogic.Spells.IceBlade", "sCache"),
            Pool("Magicka.GameLogic.Spells.UnderGroundAttack", "sCache"),
            Pool("Magicka.GameLogic.Spells.IceSpikes", "sCache"),
            Pool("Magicka.GameLogic.Spells.SpellEffects.PushSpell", "mCache"),
            Pool("Magicka.GameLogic.Spells.SpellEffects.SpraySpell", "mCache"),
            Pool("Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell", "mCache", "sCachedConditions"),
            Pool("Magicka.GameLogic.Spells.SpellEffects.RailGunSpell", "mCache"),
            Pool("Magicka.GameLogic.Spells.SpellEffects.LightningSpell", "mCache"),
            Pool("Magicka.GameLogic.Spells.SpellEffects.ShieldSpell", "sCache"),
            Pool("Magicka.GameLogic.Entities.SpellMine", "sCache"),
            Pool("Magicka.GameLogic.Entities.TeslaField", "sCache"),
            Pool("Magicka.GameLogic.Entities.Shield", "mCache"),
            Pool("Magicka.GameLogic.Entities.Abilities.SpecialAbilities.WaveEntity", "mWaveCache"),
            Pool("Magicka.GameLogic.Entities.SprayEntity", "sCache"),
            Pool("Magicka.GameLogic.Entities.Dispenser", "mCache")
        };

        private static readonly object[] EmptyArguments = new object[0];
        private static PoolField[] pools;
        private static MethodInfo clearHandlesMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Static level pool cleanup",
                "org.magickacommunitypatch.static-level-pool-cleanup",
                FindPlayStateDispose,
                typeof(StaticLevelPoolCleanupPatch).GetMethod("Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            List<PoolField> foundPools = new List<PoolField>();
            for (int contractIndex = 0;
                contractIndex < PoolContracts.Length;
                contractIndex++)
            {
                string[] contract = PoolContracts[contractIndex];
                Type type = targetAssembly.GetType(contract[0], true);
                for (int fieldIndex = 1;
                    fieldIndex < contract.Length;
                    fieldIndex++)
                    foundPools.Add(RequirePool(type, contract[fieldIndex]));
            }
            pools = foundPools.ToArray();

            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            clearHandlesMethod = entityType.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clearHandlesMethod == null ||
                clearHandlesMethod.ReturnType != typeof(void))
                throw new MissingMethodException(entityType.FullName, "ClearHandles");

            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
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

        private static PoolField RequirePool(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null ||
                !typeof(ICollection).IsAssignableFrom(field.FieldType))
                throw new MissingFieldException(type.FullName, fieldName);

            MethodInfo clear = field.FieldType.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (clear == null || clear.ReturnType != typeof(void))
                throw new MissingMethodException(field.FieldType.FullName, "Clear");
            return new PoolField(field, clear);
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int anchor = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(called, clearHandlesMethod))
                {
                    anchor = index;
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one Entity.ClearHandles call in PlayState.Dispose, found " +
                    matches + ".");

            result.Insert(
                anchor + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(StaticLevelPoolCleanupPatch).GetMethod("ClearAll")));
            return result;
        }

        public static void ClearAll()
        {
            if (pools == null)
                throw new InvalidOperationException(
                    "Static level pool contracts have not been initialized.");

            for (int index = 0; index < pools.Length; index++)
            {
                PoolField pool = pools[index];
                object collection = pool.Field.GetValue(null);
                if (collection == null)
                    continue;

                IList list = collection as IList;
                if (list != null)
                    list.Clear();
                else
                    pool.Clear.Invoke(collection, EmptyArguments);
            }
        }

        private static string[] Pool(string typeName, params string[] fields)
        {
            string[] contract = new string[fields.Length + 1];
            contract[0] = typeName;
            Array.Copy(fields, 0, contract, 1, fields.Length);
            return contract;
        }

        private sealed class PoolField
        {
            internal FieldInfo Field { get; private set; }
            internal MethodInfo Clear { get; private set; }

            internal PoolField(FieldInfo field, MethodInfo clear)
            {
                Field = field;
                Clear = clear;
            }
        }
    }
}
