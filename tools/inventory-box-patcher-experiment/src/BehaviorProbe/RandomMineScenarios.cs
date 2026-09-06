using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class RandomMineScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        RandomMineHarness harness = new RandomMineHarness(magicka);
        report.Add("random_mine.play_state_release", harness.PlayStateRelease());
        report.Add("random_mine.offline_damage", harness.DamageState(false, true));
        report.Add("random_mine.client_no_damage", harness.DamageState(true, false));
    }
}

internal sealed class RandomMineHarness
{
    private readonly Type networkClientType;
    private readonly Type networkManagerType;
    private readonly Type playStateType;
    private readonly Type randomMineType;
    private readonly Type vectorType;
    private readonly MethodInfo execute;
    private readonly FieldInfo damageField;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo networkManagerSingleton;

    internal RandomMineHarness(Assembly magicka)
    {
        networkClientType = magicka.GetType("Magicka.Network.NetworkClient", true);
        networkManagerType = magicka.GetType("Magicka.Network.NetworkManager", true);
        playStateType = magicka.GetType("Magicka.GameLogic.GameStates.PlayState", true);
        randomMineType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.RandomMine",
            true);
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
        execute = randomMineType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { vectorType, playStateType },
            null);
        if (execute == null)
            throw new MissingMethodException(randomMineType.FullName, "Execute");

        damageField = RuntimeReflection.RequireField(randomMineType, "mDoDamage");
        playStateField = randomMineType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        networkManagerSingleton = RuntimeReflection.RequireField(
            networkManagerType,
            "sSingelton");
    }

    internal ScenarioResult PlayStateRelease()
    {
        object mine = NewUninitialized(randomMineType);
        object playState = NewUninitialized(playStateType);
        ConfigureNetwork(false);
        Invoke(mine, playState);
        bool retained = playStateField != null &&
            ReferenceEquals(playStateField.GetValue(mine), playState);
        return new ScenarioResult(
            !retained,
            retained ? "retained" : "released",
            "released");
    }

    internal ScenarioResult DamageState(bool client, bool expectedDamage)
    {
        object mine = NewUninitialized(randomMineType);
        object playState = NewUninitialized(playStateType);
        ConfigureNetwork(client);
        bool result = Invoke(mine, playState);
        bool damage = (bool)damageField.GetValue(mine);
        string actual = "result:" + result + ",damage:" + damage;
        string expected = "result:True,damage:" + expectedDamage;
        return new ScenarioResult(
            result && damage == expectedDamage,
            actual,
            expected);
    }

    private void ConfigureNetwork(bool client)
    {
        object manager = NewUninitialized(networkManagerType);
        if (client)
            RuntimeReflection.WriteField(
                manager,
                "mInterface",
                NewUninitialized(networkClientType));
        networkManagerSingleton.SetValue(null, manager);
    }

    private bool Invoke(object mine, object playState)
    {
        object position = Activator.CreateInstance(vectorType);
        try
        {
            return (bool)execute.Invoke(mine, new object[] { position, playState });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
