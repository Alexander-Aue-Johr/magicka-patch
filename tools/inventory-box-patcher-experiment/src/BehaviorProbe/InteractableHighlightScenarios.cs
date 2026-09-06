using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class InteractableHighlightScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        InteractableHighlightHarness harness = new InteractableHighlightHarness(magicka);
        report.Add("interactable_highlight.missing_scene", harness.MissingScene());
        report.Add(
            "interactable_highlight.missing_level_model",
            harness.MissingLevelModel());
        report.Add("interactable_highlight.empty", harness.Empty());
    }
}

internal sealed class InteractableHighlightHarness
{
    private readonly Type interactableType;
    private readonly Type gameSceneType;
    private readonly FieldInfo gameSceneField;
    private readonly FieldInfo animatedIdsField;
    private readonly FieldInfo physicsIdsField;
    private readonly MethodInfo highlight;

    internal InteractableHighlightHarness(Assembly magicka)
    {
        interactableType = magicka.GetType(
            "Magicka.Levels.Triggers.Interactable",
            true);
        gameSceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        gameSceneField = RuntimeReflection.RequireField(interactableType, "mGameScene");
        animatedIdsField = RuntimeReflection.RequireField(
            interactableType,
            "mAnimHighlightIDs");
        physicsIdsField = RuntimeReflection.RequireField(
            interactableType,
            "mPhysHighlightIDs");
        highlight = interactableType.GetMethod(
            "Highlight",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        if (highlight == null || highlight.ReturnType != typeof(void))
            throw new MissingMethodException(interactableType.FullName, "Highlight");
    }

    internal ScenarioResult MissingScene()
    {
        return Invoke(null, new int[][] { new int[] { 123 } });
    }

    internal ScenarioResult MissingLevelModel()
    {
        object scene = NewUninitialized(gameSceneType);
        return Invoke(scene, new int[][] { new int[] { 123 } });
    }

    internal ScenarioResult Empty()
    {
        return Invoke(null, new int[0][]);
    }

    private ScenarioResult Invoke(object scene, int[][] animatedIds)
    {
        object interactable = NewUninitialized(interactableType);
        gameSceneField.SetValue(interactable, scene);
        animatedIdsField.SetValue(interactable, animatedIds);
        physicsIdsField.SetValue(interactable, new int[0]);

        string actual = "none";
        try
        {
            highlight.Invoke(interactable, null);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            actual = inner.GetType().FullName;
        }
        const string expected = "none";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
