using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class AvatarFindInteractableScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        AvatarFindInteractableHarness harness = new AvatarFindInteractableHarness(magicka);
        report.Add("avatar_interactable.missing_play_state", harness.MissingPlayState());
        report.Add("avatar_interactable.missing_level", harness.MissingLevel());
        report.Add("avatar_interactable.missing_scene", harness.MissingScene());
        report.Add("avatar_interactable.missing_triggers", harness.MissingTriggers());
        report.Add("avatar_interactable.empty_scene", harness.EmptyScene());
    }
}

internal sealed class AvatarFindInteractableHarness
{
    private readonly Type avatarType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type sceneType;
    private readonly Type triggerType;
    private readonly MethodInfo findInteractable;

    internal AvatarFindInteractableHarness(Assembly magicka)
    {
        avatarType = magicka.GetType("Magicka.GameLogic.Entities.Avatar", true);
        playStateType = magicka.GetType("Magicka.GameLogic.GameStates.PlayState", true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        triggerType = magicka.GetType("Magicka.Levels.Triggers.Trigger", true);
        findInteractable = avatarType.GetMethod(
            "FindInteractable",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { typeof(bool) },
            null);
    }

    internal ScenarioResult MissingPlayState()
    {
        return Invoke(NewAvatar(null));
    }

    internal ScenarioResult MissingScene()
    {
        return Invoke(NewAvatar(NewPlayState(NewLevel(null))));
    }

    internal ScenarioResult MissingLevel()
    {
        return Invoke(NewAvatar(NewPlayState(null)));
    }

    internal ScenarioResult MissingTriggers()
    {
        object scene = NewScene();
        return Invoke(NewAvatar(NewPlayState(NewLevel(scene))));
    }

    internal ScenarioResult EmptyScene()
    {
        object scene = NewScene();
        Type sortedListType = typeof(System.Collections.Generic.SortedList<,>)
            .MakeGenericType(typeof(int), triggerType);
        RuntimeReflection.WriteField(scene, "mTriggers", Activator.CreateInstance(sortedListType));
        return Invoke(NewAvatar(NewPlayState(NewLevel(scene))));
    }

    private object NewPlayState(object level)
    {
        object playState = FormatterServices.GetUninitializedObject(playStateType);
        RuntimeReflection.WriteField(playState, "mLevel", level);
        return playState;
    }

    private object NewAvatar(object playState)
    {
        object avatar = FormatterServices.GetUninitializedObject(avatarType);
        RuntimeReflection.WriteField(avatar, "mPlayState", playState);
        return avatar;
    }

    private object NewLevel(object scene)
    {
        object level = FormatterServices.GetUninitializedObject(levelType);
        RuntimeReflection.WriteField(level, "mCurrentScene", scene);
        return level;
    }

    private object NewScene()
    {
        object scene = FormatterServices.GetUninitializedObject(sceneType);
        GC.SuppressFinalize(scene);
        return scene;
    }

    private ScenarioResult Invoke(object avatar)
    {
        try
        {
            object result = findInteractable.Invoke(avatar, new object[] { false });
            string actual = result == null ? "null" : result.GetType().FullName;
            return new ScenarioResult(result == null, actual, "null");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, "null");
        }
    }
}
