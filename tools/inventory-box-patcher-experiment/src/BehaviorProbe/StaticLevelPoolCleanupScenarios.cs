using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class StaticLevelPoolCleanupScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        StaticLevelPoolCleanupHarness harness =
            new StaticLevelPoolCleanupHarness(magicka, runtimePatchEnabled);
        report.Add(
            "static_level_pools.level_dispose",
            harness.InitializedDispose());
        report.Add(
            "static_level_pools.uninitialized_dispose",
            harness.UninitializedDispose());
    }
}

internal sealed class StaticLevelPoolCleanupHarness
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

    private readonly bool runtimePatchEnabled;
    private readonly Type playStateType;
    private readonly MethodInfo playStateDispose;
    private readonly PoolFixture[] pools;
    private readonly MethodInfo[] manualCleanupMethods;

    internal StaticLevelPoolCleanupHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        playStateDispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (playStateDispose == null || playStateDispose.ReturnType != typeof(void))
            throw new MissingMethodException(playStateType.FullName, "Dispose");

        List<PoolFixture> foundPools = new List<PoolFixture>();
        List<MethodInfo> cleanupMethods = new List<MethodInfo>();
        for (int contractIndex = 0;
            contractIndex < PoolContracts.Length;
            contractIndex++)
        {
            string[] contract = PoolContracts[contractIndex];
            Type type = magicka.GetType(contract[0], true);
            for (int fieldIndex = 1;
                fieldIndex < contract.Length;
                fieldIndex++)
            {
                foundPools.Add(new PoolFixture(type, contract[fieldIndex]));
            }

            MethodInfo cleanup = type.GetMethod(
                "DisposeCache",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (cleanup != null)
                cleanupMethods.Add(cleanup);
        }
        if (cleanupMethods.Count != 0 &&
            cleanupMethods.Count != PoolContracts.Length)
            throw new InvalidOperationException(
                "Only part of the manual static-pool cleanup contract is present.");

        pools = foundPools.ToArray();
        manualCleanupMethods = cleanupMethods.ToArray();
    }

    internal ScenarioResult InitializedDispose()
    {
        PopulatePools();
        if (manualCleanupMethods.Length != 0)
        {
            for (int index = 0;
                index < manualCleanupMethods.Length;
                index++)
                Invoke(manualCleanupMethods[index], null);
        }
        else if (runtimePatchEnabled)
        {
            Type cleanupType =
                typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.StaticLevelPoolCleanupPatch",
                false);
            MethodInfo clearAll = cleanupType == null
                ? null
                : cleanupType.GetMethod(
                    "ClearAll",
                    BindingFlags.Static | BindingFlags.Public);
            if (clearAll != null)
                Invoke(clearAll, null);
        }

        return CountsResult(0);
    }

    internal ScenarioResult UninitializedDispose()
    {
        PopulatePools();
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mInitialized", false);
        Invoke(playStateDispose, playState);
        return CountsResult(1);
    }

    private void PopulatePools()
    {
        for (int index = 0; index < pools.Length; index++)
            pools[index].Populate();
    }

    private ScenarioResult CountsResult(int expectedCount)
    {
        int matching = 0;
        for (int index = 0; index < pools.Length; index++)
        {
            if (pools[index].Count == expectedCount)
                matching++;
        }
        string description = "matching:" + matching + "/" + pools.Length;
        return new ScenarioResult(
            matching == pools.Length,
            description,
            "matching:" + pools.Length + "/" + pools.Length);
    }

    private static string[] Pool(string typeName, params string[] fields)
    {
        string[] contract = new string[fields.Length + 1];
        contract[0] = typeName;
        Array.Copy(fields, 0, contract, 1, fields.Length);
        return contract;
    }

    private static object Invoke(MethodInfo method, object target)
    {
        try
        {
            return method.Invoke(target, new object[0]);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }

    private sealed class PoolFixture
    {
        private readonly FieldInfo field;
        private object collection;

        internal PoolFixture(Type type, string fieldName)
        {
            field = type.GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null ||
                !typeof(ICollection).IsAssignableFrom(field.FieldType))
                throw new MissingFieldException(type.FullName, fieldName);
            if (FindInsertMethod(field.FieldType) == null)
                throw new MissingMethodException(field.FieldType.FullName, "Add/Enqueue");
        }

        internal int Count
        {
            get { return ((ICollection)collection).Count; }
        }

        internal void Populate()
        {
            collection = Activator.CreateInstance(field.FieldType);
            MethodInfo insert = FindInsertMethod(field.FieldType);
            Type elementType = insert.GetParameters()[0].ParameterType;
            insert.Invoke(
                collection,
                new object[] { NewUninitialized(elementType) });
            field.SetValue(null, collection);
        }

        private static MethodInfo FindInsertMethod(Type collectionType)
        {
            MethodInfo[] methods = collectionType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                if ((method.Name == "Add" || method.Name == "Enqueue") &&
                    method.GetParameters().Length == 1)
                    return method;
            }
            return null;
        }
    }
}
