using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class KeyboardMouseInteractableScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        KeyboardMouseInteractableHarness harness =
            new KeyboardMouseInteractableHarness(magicka);
        report.Add(
            "keyboard_mouse_interactable.missing_avatar",
            harness.MissingAvatar());
        report.Add(
            "keyboard_mouse_interactable.missing_play_state",
            harness.MissingPlayState());
        report.Add(
            "keyboard_mouse_interactable.missing_level",
            harness.MissingLevel());
        report.Add(
            "keyboard_mouse_interactable.missing_scene",
            harness.MissingScene());
        report.Add(
            "keyboard_mouse_interactable.missing_triggers",
            harness.MissingTriggers());
        report.Add(
            "keyboard_mouse_interactable.empty_scene",
            harness.EmptyScene());
    }
}

internal sealed class KeyboardMouseInteractableHarness
{
    private readonly Type controllerType;
    private readonly Type avatarType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type sceneType;
    private readonly Type triggerType;
    private readonly Type segmentType;
    private readonly MethodInfo findInteractable;

    internal KeyboardMouseInteractableHarness(Assembly magicka)
    {
        controllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.KeyboardMouseController",
            true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        triggerType = magicka.GetType(
            "Magicka.Levels.Triggers.Trigger",
            true);
        segmentType = FindLoadedType("JigLibX.Geometry.Segment");
        findInteractable = controllerType.GetMethod(
            "FindInteractable",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { segmentType.MakeByRefType() },
            null);
        if (findInteractable == null)
            throw new MissingMethodException(
                controllerType.FullName,
                "FindInteractable");
    }

    internal ScenarioResult MissingAvatar()
    {
        return Invoke(NewController(null));
    }

    internal ScenarioResult MissingPlayState()
    {
        return Invoke(NewController(NewAvatar(null)));
    }

    internal ScenarioResult MissingLevel()
    {
        return Invoke(NewController(NewAvatar(NewPlayState(null))));
    }

    internal ScenarioResult MissingScene()
    {
        return Invoke(NewController(
            NewAvatar(NewPlayState(NewLevel(null)))));
    }

    internal ScenarioResult MissingTriggers()
    {
        object scene = NewScene();
        return Invoke(NewController(
            NewAvatar(NewPlayState(NewLevel(scene)))));
    }

    internal ScenarioResult EmptyScene()
    {
        object scene = NewScene();
        Type sortedListType = typeof(System.Collections.Generic.SortedList<,>)
            .MakeGenericType(typeof(int), triggerType);
        RuntimeReflection.WriteField(
            scene,
            "mTriggers",
            Activator.CreateInstance(sortedListType));
        return Invoke(NewController(
            NewAvatar(NewPlayState(NewLevel(scene)))));
    }

    private object NewController(object avatar)
    {
        object controller = FormatterServices.GetUninitializedObject(
            controllerType);
        RuntimeReflection.WriteField(controller, "mAvatar", avatar);
        return controller;
    }

    private object NewAvatar(object playState)
    {
        object avatar = FormatterServices.GetUninitializedObject(avatarType);
        RuntimeReflection.WriteField(avatar, "mPlayState", playState);
        return avatar;
    }

    private object NewPlayState(object level)
    {
        object playState = FormatterServices.GetUninitializedObject(
            playStateType);
        RuntimeReflection.WriteField(playState, "mLevel", level);
        return playState;
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

    private ScenarioResult Invoke(object controller)
    {
        try
        {
            object result = findInteractable.Invoke(
                controller,
                new object[] { Activator.CreateInstance(segmentType) });
            string actual = result == null ? "null" : result.GetType().FullName;
            return new ScenarioResult(result == null, actual, "null");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, "null");
        }
    }

    private static Type FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < assemblies.Length; index++)
        {
            Type type = assemblies[index].GetType(fullName, false);
            if (type != null)
                return type;
        }
        throw new TypeLoadException(fullName);
    }
}
