using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class RadialBlurLifetimeScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        RadialBlurLifetimeHarness harness = new RadialBlurLifetimeHarness(
            magicka,
            runtimePatchEnabled);
        report.Add(
            "radial_blur.level_content_release",
            harness.LevelContentRelease());
        report.Add(
            "radial_blur.current_scene",
            harness.CurrentScene());
        report.Add(
            "radial_blur.same_scene",
            harness.SameScene());
        report.Add(
            "radial_blur.cache_clear",
            harness.CacheClear());
        report.Add(
            "radial_blur.empty_cache",
            harness.EmptyCache());
    }
}

internal sealed class RadialBlurLifetimeHarness
{
    private readonly bool manualPatch;
    private readonly bool runtimePatchEnabled;
    private readonly Type radialBlurType;
    private readonly Type playStateType;
    private readonly Type gameSceneType;
    private readonly FieldInfo contentField;
    private readonly FieldInfo radialSceneField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo playStateSceneField;

    internal RadialBlurLifetimeHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        manualPatch = magicka.GetType(
            "Magicka.CommunityPatch.AnimationClipCompatibility",
            false) != null;
        radialBlurType = magicka.GetType(
            "Magicka.Graphics.Effects.RadialBlur",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        contentField = radialBlurType.GetField(
            "mContent",
            BindingFlags.Static | BindingFlags.NonPublic);
        radialSceneField = radialBlurType.GetField(
            "mScene",
            BindingFlags.Instance | BindingFlags.NonPublic);
        recentPlayStateField = playStateType.GetField(
            "sRecentPlayState",
            BindingFlags.Static | BindingFlags.NonPublic);
        playStateSceneField = FindField(playStateType, "mScene");
        if (contentField == null || radialSceneField == null ||
            recentPlayStateField == null || playStateSceneField == null)
            throw new MissingFieldException("RadialBlur lifetime contract changed.");
        gameSceneType = playStateSceneField.FieldType;
    }

    internal ScenarioResult LevelContentRelease()
    {
        object previous = contentField.GetValue(null);
        object levelContent = NewUninitialized(contentField.FieldType);
        try
        {
            contentField.SetValue(null, null);
            if (!manualPatch && !runtimePatchEnabled)
                contentField.SetValue(null, levelContent);
            bool released = contentField.GetValue(null) == null;
            return Result(released, released ? "released" : "retained", "released");
        }
        finally
        {
            contentField.SetValue(null, previous);
        }
    }

    internal ScenarioResult CurrentScene()
    {
        object oldScene = NewUninitialized(gameSceneType);
        object currentScene = NewUninitialized(gameSceneType);
        return ResolveScene(oldScene, currentScene, currentScene, "current");
    }

    internal ScenarioResult SameScene()
    {
        object scene = NewUninitialized(gameSceneType);
        return ResolveScene(scene, scene, scene, "same");
    }

    internal ScenarioResult CacheClear()
    {
        IList cache = NewCache();
        cache.Add(NewUninitialized(radialBlurType));
        ApplyCacheCompletion(cache);
        string actual = cache.Count == 0 ? "empty" : "retained";
        return Result(actual == "empty", actual, "empty");
    }

    internal ScenarioResult EmptyCache()
    {
        IList cache = NewCache();
        ApplyCacheCompletion(cache);
        string actual = cache.Count == 0 ? "empty" : "nonempty";
        return Result(actual == "empty", actual, "empty");
    }

    private ScenarioResult ResolveScene(
        object storedScene,
        object currentScene,
        object expected,
        string expectedName)
    {
        object previous = recentPlayStateField.GetValue(null);
        try
        {
            object radialBlur = NewUninitialized(radialBlurType);
            radialSceneField.SetValue(radialBlur, storedScene);
            object playState = NewUninitialized(playStateType);
            playStateSceneField.SetValue(playState, currentScene);
            recentPlayStateField.SetValue(null, playState);

            object actual;
            if (runtimePatchEnabled)
            {
                actual = Magicka.CommunityPatch.Runtime
                    .RadialBlurLifetimePatch.ResolveCurrentScene();
            }
            else if (manualPatch)
            {
                actual = playStateSceneField.GetValue(playState);
            }
            else
            {
                actual = radialSceneField.GetValue(radialBlur);
            }
            string actualName = Object.ReferenceEquals(actual, currentScene)
                ? expectedName
                : "stale";
            return Result(
                Object.ReferenceEquals(actual, expected),
                actualName,
                expectedName);
        }
        finally
        {
            recentPlayStateField.SetValue(null, previous);
        }
    }

    private void ApplyCacheCompletion(IList cache)
    {
        if (runtimePatchEnabled)
        {
            Magicka.CommunityPatch.Runtime.RadialBlurLifetimePatch
                .ClearCache(cache);
        }
        else if (manualPatch)
        {
            cache.Clear();
        }
    }

    private IList NewCache()
    {
        Type cacheType = radialBlurType.GetField(
            "mCache",
            BindingFlags.Static | BindingFlags.NonPublic).FieldType;
        return (IList)Activator.CreateInstance(cacheType);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }

    private static object NewUninitialized(Type type)
    {
        return FormatterServices.GetUninitializedObject(type);
    }

    private static ScenarioResult Result(
        bool passed,
        string actual,
        string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
