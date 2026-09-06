using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class JormungandrScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        JormungandrHarness harness = new JormungandrHarness(magicka);
        report.Add("jormungandr.missing_target", harness.MissingTarget());
        report.Add("jormungandr.before_warning", harness.BeforeWarning());
    }
}

internal sealed class JormungandrHarness
{
    private readonly Type gameType;
    private readonly Type networkManagerType;
    private readonly Type playerType;
    private readonly Type jormungandrType;
    private readonly FieldInfo gameSingleton;
    private readonly FieldInfo networkSingleton;
    private readonly FieldInfo warningTime;
    private readonly object state;
    private readonly MethodInfo onUpdate;

    internal JormungandrHarness(Assembly magicka)
    {
        gameType = magicka.GetType("Magicka.Game", true);
        networkManagerType = magicka.GetType("Magicka.Network.NetworkManager", true);
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        jormungandrType = magicka.GetType(
            "Magicka.GameLogic.Entities.Bosses.Jormungandr",
            true);
        Type stateType = magicka.GetType(
            "Magicka.GameLogic.Entities.Bosses.Jormungandr+UndergroundState",
            true);
        gameSingleton = RuntimeReflection.RequireField(gameType, "mSingelton");
        networkSingleton = RuntimeReflection.RequireField(networkManagerType, "sSingelton");
        warningTime = RuntimeReflection.RequireField(jormungandrType, "WARNINGTIME");
        state = stateType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
        onUpdate = Array.Find(
            stateType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == "OnUpdate" && method.GetParameters().Length == 2);
    }

    internal ScenarioResult MissingTarget()
    {
        return Invoke(3f);
    }

    internal ScenarioResult BeforeWarning()
    {
        return Invoke(0f);
    }

    private ScenarioResult Invoke(float idleTime)
    {
        object previousGame = gameSingleton.GetValue(null);
        object previousNetwork = networkSingleton.GetValue(null);
        object previousWarning = warningTime.GetValue(null);
        try
        {
            gameSingleton.SetValue(null, NewGameWithoutAvatars());
            networkSingleton.SetValue(
                null,
                FormatterServices.GetUninitializedObject(networkManagerType));
            warningTime.SetValue(null, 2f);

            object owner = FormatterServices.GetUninitializedObject(jormungandrType);
            GC.SuppressFinalize(owner);
            RuntimeReflection.WriteField(owner, "mHitPoints", 10000f);
            RuntimeReflection.WriteField(owner, "mIdleTimer", idleTime);
            try
            {
                onUpdate.Invoke(state, new object[] { 0f, owner });
                object target = RuntimeReflection.ReadField(owner, "mTarget");
                float actualIdle = Convert.ToSingle(
                    RuntimeReflection.ReadField(owner, "mIdleTimer"));
                string actual = "completed,target:" +
                    (target == null ? "null" : "set") +
                    ",idle:" + actualIdle;
                string expected = "completed,target:null,idle:" + idleTime;
                return new ScenarioResult(
                    target == null && actualIdle == idleTime,
                    actual,
                    expected);
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                return new ScenarioResult(
                    false,
                    inner.GetType().FullName,
                    "completed,target:null,idle:" + idleTime);
            }
        }
        finally
        {
            warningTime.SetValue(null, previousWarning);
            networkSingleton.SetValue(null, previousNetwork);
            gameSingleton.SetValue(null, previousGame);
        }
    }

    private object NewGameWithoutAvatars()
    {
        object game = FormatterServices.GetUninitializedObject(gameType);
        GC.SuppressFinalize(game);
        Array players = Array.CreateInstance(playerType, 4);
        for (int index = 0; index < players.Length; index++)
        {
            object player = FormatterServices.GetUninitializedObject(playerType);
            RuntimeReflection.WriteField(player, "mAvatar", new WeakReference(null));
            players.SetValue(player, index);
        }
        RuntimeReflection.WriteField(game, "mPlayers", players);
        return game;
    }
}
