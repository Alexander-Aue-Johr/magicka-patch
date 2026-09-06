using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class BossHealthBarScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        BossHealthBarHarness harness = new BossHealthBarHarness(magicka);
        if (magicka.GetName().Version < new Version(1, 10))
        {
            report.AddNotApplicable(
                "boss_health_bar.constructor_release",
                "The legacy constructor requires a live graphics device");
        }
        else
        {
            report.Add("boss_health_bar.constructor_release", harness.ConstructorDoesNotRetainScene());
        }
        report.Add("boss_health_bar.current_scene", harness.ReturnsCurrentScene());
        report.Add("boss_health_bar.setter_release", harness.SetterDoesNotRetainScene());
    }
}

internal sealed class BossHealthBarHarness
{
    private readonly Type healthBarType;
    private readonly Type gameType;
    private readonly Type playStateType;
    private readonly Type sceneType;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo gameSingletonField;
    private readonly FieldInfo healthBarSceneField;
    private readonly MethodInfo sceneGetter;
    private readonly MethodInfo sceneSetter;
    private readonly ConstructorInfo constructor;

    internal BossHealthBarHarness(Assembly magicka)
    {
        healthBarType = magicka.GetType("Magicka.GameLogic.UI.BossHealthBar", true);
        gameType = magicka.GetType("Magicka.Game", true);
        playStateType = magicka.GetType("Magicka.GameLogic.GameStates.PlayState", true);
        PropertyInfo sceneProperty = healthBarType.GetProperty(
            "Scene",
            BindingFlags.Instance | BindingFlags.Public);
        sceneType = sceneProperty.PropertyType;
        sceneGetter = sceneProperty.GetGetMethod();
        sceneSetter = sceneProperty.GetSetMethod();
        recentPlayStateField = RuntimeReflection.RequireField(playStateType, "sRecentPlayState");
        gameSingletonField = RuntimeReflection.RequireField(gameType, "mSingelton");
        healthBarSceneField = healthBarType.GetField(
            "mScene",
            BindingFlags.Instance | BindingFlags.NonPublic);
        constructor = healthBarType.GetConstructor(new Type[] { sceneType });
    }

    internal ScenarioResult ConstructorDoesNotRetainScene()
    {
        object previousGame = gameSingletonField.GetValue(null);
        try
        {
            object game = NewUninitialized(gameType);
            Type graphicsManagerType = RuntimeReflection.FindLoadedType(
                "Microsoft.Xna.Framework.GraphicsDeviceManager");
            RuntimeReflection.WriteField(
                game,
                "graphicsDeviceService",
                NewUninitialized(graphicsManagerType));
            gameSingletonField.SetValue(null, game);
            object suppliedScene = NewUninitialized(sceneType);
            object healthBar = constructor.Invoke(new object[] { suppliedScene });
            bool retained = healthBarSceneField != null &&
                healthBarSceneField.GetValue(healthBar) != null;
            return new ScenarioResult(
                !retained,
                retained ? "retained" : "released",
                "released");
        }
        finally
        {
            gameSingletonField.SetValue(null, previousGame);
        }
    }

    internal ScenarioResult ReturnsCurrentScene()
    {
        object previousPlayState = recentPlayStateField.GetValue(null);
        try
        {
            object currentScene = NewUninitialized(sceneType);
            object staleScene = NewUninitialized(sceneType);
            recentPlayStateField.SetValue(null, NewPlayState(currentScene));
            object healthBar = NewUninitialized(healthBarType);
            if (healthBarSceneField != null)
                healthBarSceneField.SetValue(healthBar, staleScene);

            object actualScene = sceneGetter.Invoke(healthBar, null);
            return new ScenarioResult(
                ReferenceEquals(actualScene, currentScene),
                ReferenceEquals(actualScene, currentScene) ? "current" : "stale",
                "current");
        }
        finally
        {
            recentPlayStateField.SetValue(null, previousPlayState);
        }
    }

    internal ScenarioResult SetterDoesNotRetainScene()
    {
        object healthBar = NewUninitialized(healthBarType);
        object suppliedScene = NewUninitialized(sceneType);
        sceneSetter.Invoke(healthBar, new object[] { suppliedScene });
        bool retained = healthBarSceneField != null &&
            healthBarSceneField.GetValue(healthBar) != null;
        return new ScenarioResult(
            !retained,
            retained ? "retained" : "released",
            "released");
    }

    private object NewPlayState(object scene)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mScene", scene);
        return playState;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
