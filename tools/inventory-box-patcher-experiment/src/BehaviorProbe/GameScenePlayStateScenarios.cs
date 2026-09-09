using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GameScenePlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        GameScenePlayStateHarness harness =
            new GameScenePlayStateHarness(magicka);
        report.Add(
            "game_scene.current_play_state",
            harness.ReturnsCurrentPlayState());
        report.Add(
            "game_scene.same_play_state",
            harness.PreservesMatchingPlayState());
    }
}

internal sealed class GameScenePlayStateHarness
{
    private readonly Type gameSceneType;
    private readonly Type playStateType;
    private readonly FieldInfo scenePlayStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly MethodInfo playStateGetter;

    internal GameScenePlayStateHarness(Assembly magicka)
    {
        gameSceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        scenePlayStateField = RuntimeReflection.RequireField(
            gameSceneType,
            "mPlayState");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        PropertyInfo property = gameSceneType.GetProperty(
            "PlayState",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        playStateGetter = property == null ? null : property.GetGetMethod(true);
        if (playStateGetter == null ||
            playStateGetter.ReturnType != playStateType)
            throw new MissingMethodException(
                gameSceneType.FullName,
                "get_PlayState");
    }

    internal ScenarioResult ReturnsCurrentPlayState()
    {
        object captured = NewUninitialized(playStateType);
        object current = NewUninitialized(playStateType);
        return InvokeScenario(captured, current, "current");
    }

    internal ScenarioResult PreservesMatchingPlayState()
    {
        object current = NewUninitialized(playStateType);
        return InvokeScenario(current, current, "current");
    }

    private ScenarioResult InvokeScenario(
        object captured,
        object current,
        string expected)
    {
        object previous = recentPlayStateField.GetValue(null);
        try
        {
            object scene = NewUninitialized(gameSceneType);
            scenePlayStateField.SetValue(scene, captured);
            recentPlayStateField.SetValue(null, current);
            object result = playStateGetter.Invoke(scene, null);
            string actual = Object.ReferenceEquals(result, current)
                ? "current"
                : (Object.ReferenceEquals(result, captured)
                    ? "captured"
                    : "other");
            return new ScenarioResult(actual == expected, actual, expected);
        }
        finally
        {
            recentPlayStateField.SetValue(null, previous);
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
