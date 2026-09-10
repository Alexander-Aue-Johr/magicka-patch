using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GameSceneTeardownScenarios
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        GameSceneTeardownHarness harness = new GameSceneTeardownHarness(magicka);
        report.Add(
            "game_scene.dispose_complete",
            harness.DisposesCompleteGraph());
        report.Add(
            "game_scene.dispose_idempotent",
            harness.DisposesTwice());
        report.Add(
            "game_scene.dispose_releases_kill_plane_tag",
            harness.ReleasesKillPlaneTag());
    }
}

internal sealed class GameSceneTeardownHarness
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Type gameSceneType;
    private readonly Type triggerType;
    private readonly Type actionBaseType;
    private readonly Type actionType;
    private readonly Type conditionType;
    private readonly MethodInfo dispose;

    internal GameSceneTeardownHarness(Assembly magicka)
    {
        gameSceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        triggerType = magicka.GetType("Magicka.Levels.Triggers.Trigger", true);
        actionBaseType = magicka.GetType(
            "Magicka.Levels.Triggers.Actions.Action",
            true);
        actionType = magicka.GetType(
            "Magicka.Levels.Triggers.Actions.Spawn",
            true);
        conditionType = magicka.GetType(
            "Magicka.Levels.Triggers.Conditions.Timer",
            true);
        dispose = gameSceneType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (dispose == null || dispose.ReturnType != typeof(void))
            throw new MissingMethodException(gameSceneType.FullName, "Dispose");
    }

    internal ScenarioResult DisposesCompleteGraph()
    {
        object trigger;
        object action;
        object skin;
        object scene = CreateScene(out trigger, out action, out skin);
        string exception = Invoke(dispose, scene);
        int retained = CountRetained(scene);
        bool triggerOnlyReleased = TriggerReleased(scene, trigger, action);
        bool passed = exception == "none" && retained == 0 &&
            triggerOnlyReleased;
        return new ScenarioResult(
            passed,
            "completed:" + (exception == "none") +
                ",retained:" + retained +
                ",trigger_graph:" +
                    (triggerOnlyReleased ? "released" : "retained") +
                ",exception:" + exception,
            "completed:True,retained:0,trigger_graph:released," +
                "exception:none");
    }

    internal ScenarioResult DisposesTwice()
    {
        object trigger;
        object action;
        object skin;
        object scene = CreateScene(out trigger, out action, out skin);
        string first = Invoke(dispose, scene);
        string second = Invoke(dispose, scene);
        bool passed = first == "none" && second == "none" &&
            CountRetained(scene) == 0;
        return new ScenarioResult(
            passed,
            "first:" + first + ",second:" + second +
                ",retained:" + CountRetained(scene),
            "first:none,second:none,retained:0");
    }

    internal ScenarioResult ReleasesKillPlaneTag()
    {
        object trigger;
        object action;
        object skin;
        object scene = CreateScene(out trigger, out action, out skin);
        string exception = Invoke(dispose, scene);
        bool released = SkinReleased(skin);
        return new ScenarioResult(
            exception == "none" && released,
            "tag:" + (released ? "released" : "retained") +
                ",exception:" + exception,
            "tag:released,exception:none");
    }

    private object CreateScene(
        out object trigger,
        out object action,
        out object skin)
    {
        object scene = FormatterServices.GetUninitializedObject(gameSceneType);
        GC.SuppressFinalize(scene);
        SetCollection(scene, "mGlobalSounds");
        SetCollection(scene, "mSounds");
        SetCollection(scene, "mWorldSyncTriggeredActions");
        SetCollection(scene, "mTriggeredActions");
        SetCollection(scene, "mStartupActions");
        SetCollection(scene, "mSavedEntities");
        SetCollection(scene, "mSavedAnimations");
        SetCollection(scene, "mEffects");
        SetReference(scene, "mModelName", "model");
        SetReference(scene, "mSkyMapFileName", "sky");
        SetReference(scene, "mName", "scene");
        SetReference(scene, "mShaHash", new byte[] { 1 });
        SetReference(scene, "mLiquids", null);
        SetReference(scene, "mModel", null);
        SetReference(scene, "mContent", null);
        SetReference(scene, "mRuleset", null);
        SetReference(scene, "mSwayTarget", null);
        SetReference(scene, "mSwayEffect", null);
        SetReference(scene, "mSwayVertexDeclaration", null);
        SetReference(scene, "mSwayVertices", null);
        SetReference(scene, "mCloudTexture", null);
        SetReference(scene, "mSkyMap", null);
        SetReference(scene, "mSwayTexture", null);
        SetReference(scene, "mCharacterDisplacementTexture", null);
        SetReference(scene, "mDirectionalLightSettings", null);
        SetReference(scene, "mLevel", null);
        FieldInfo skinField = RequireField(gameSceneType, "mKillPlane");
        skin = Activator.CreateInstance(skinField.FieldType);
        PropertyInfo tag = skinField.FieldType.GetProperty(
            "Tag",
            BindingFlags.Instance | BindingFlags.Public);
        if (tag == null || !tag.CanWrite)
            throw new MissingMemberException(skinField.FieldType.FullName, "Tag");
        tag.SetValue(skin, scene, null);
        skinField.SetValue(scene, skin);
        PopulateTrigger(scene, out trigger, out action);
        return scene;
    }

    private void PopulateTrigger(
        object scene,
        out object trigger,
        out object action)
    {
        FieldInfo triggersField = RequireField(gameSceneType, "mTriggers");
        IDictionary triggers = (IDictionary)Activator.CreateInstance(
            triggersField.FieldType);
        trigger = FormatterServices.GetUninitializedObject(triggerType);
        action = FormatterServices.GetUninitializedObject(actionType);
        object condition = FormatterServices.GetUninitializedObject(conditionType);
        SetTriggerField(trigger, "mGameScene", scene);
        SetTriggerField(trigger, "mIDString", "trigger");
        SetTriggerArray(trigger, "mActions", actionType, action);
        SetTriggerArray(trigger, "mConditions", conditionType, condition);
        SetActionField(action, "mTrigger", trigger);
        SetActionField(action, "mScene", scene);
        SetActionField(action, "mQueue", 1);
        triggers.Add(1, trigger);
        triggersField.SetValue(scene, triggers);
    }

    private int CountRetained(object scene)
    {
        string[] names = new string[]
        {
            "mContent", "mLevel", "mModel", "mLiquids", "mRuleset",
            "mGlobalSounds", "mSounds", "mTriggers", "mSwayTarget",
            "mKillPlane",
            "mSwayEffect", "mSwayVertexDeclaration", "mSwayVertices",
            "mWorldSyncTriggeredActions", "mTriggeredActions", "mStartupActions",
            "mSavedEntities", "mSavedAnimations", "mEffects", "mShaHash",
            "mModelName", "mSkyMapFileName", "mName"
        };
        int retained = 0;
        for (int index = 0; index < names.Length; index++)
        {
            FieldInfo field = gameSceneType.GetField(names[index], InstanceFields);
            if (field != null && field.GetValue(scene) != null)
                retained++;
        }
        return retained;
    }

    private bool TriggerReleased(
        object scene,
        object trigger,
        object action)
    {
        FieldInfo triggers = RequireField(gameSceneType, "mTriggers");
        if (triggers.GetValue(scene) != null)
            return false;
        return RequireField(triggerType, "mActions").GetValue(trigger) == null &&
            RequireField(triggerType, "mConditions").GetValue(trigger) == null &&
            RequireField(triggerType, "mGameScene").GetValue(trigger) == null &&
            RequireField(triggerType, "mIDString").GetValue(trigger) == null &&
            GetActionField("mTrigger").GetValue(action) == null &&
            GetActionField("mScene").GetValue(action) == null &&
            (int)GetActionField("mQueue").GetValue(action) == 0;
    }

    private static bool SkinReleased(object skin)
    {
        PropertyInfo tag = skin.GetType().GetProperty(
            "Tag",
            BindingFlags.Instance | BindingFlags.Public);
        return tag != null && tag.GetValue(skin, null) == null;
    }

    private void SetCollection(object target, string name)
    {
        FieldInfo field = gameSceneType.GetField(name, InstanceFields);
        if (field != null)
            field.SetValue(target, Activator.CreateInstance(field.FieldType));
    }

    private void SetReference(object target, string name, object value)
    {
        FieldInfo field = gameSceneType.GetField(name, InstanceFields);
        if (field != null)
            field.SetValue(target, value);
    }

    private void SetTriggerField(object target, string name, object value)
    {
        RequireField(triggerType, name).SetValue(target, value);
    }

    private void SetActionField(object target, string name, object value)
    {
        GetActionField(name).SetValue(target, value);
    }

    private FieldInfo GetActionField(string name)
    {
        FieldInfo field = actionBaseType.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(actionBaseType.FullName, name);
        return field;
    }

    private void SetTriggerArray(
        object trigger,
        string name,
        Type element,
        object value)
    {
        Array values = Array.CreateInstance(element, 1);
        values.SetValue(value, 0);
        Array groups = Array.CreateInstance(values.GetType(), 1);
        groups.SetValue(values, 0);
        SetTriggerField(trigger, name, groups);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, InstanceFields);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static string Invoke(MethodInfo method, object target)
    {
        try
        {
            method.Invoke(target, null);
            return "none";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return inner.GetType().FullName;
        }
        catch (Exception exception)
        {
            return exception.GetType().FullName;
        }
    }
}
