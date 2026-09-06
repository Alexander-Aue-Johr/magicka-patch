using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class FlashScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        FlashHarness harness = new FlashHarness(magicka);
        report.Add("flash.scene_release", harness.SceneRelease());
        report.Add("flash.current_scene", harness.CurrentScene());
    }
}

internal sealed class FlashHarness
{
    private readonly Type dataChannelType;
    private readonly Type flashType;
    private readonly Type playStateType;
    private readonly Type sceneType;
    private readonly MethodInfo execute;
    private readonly MethodInfo update;
    private readonly FieldInfo intensityField;
    private readonly FieldInfo intensitiesField;
    private readonly FieldInfo legacySceneField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo spellEffectsField;
    private readonly FieldInfo spellManagerSingleton;
    private readonly FieldInfo ttlField;

    internal FlashHarness(Assembly magicka)
    {
        dataChannelType = RuntimeReflection.FindLoadedType("PolygonHead.DataChannel");
        sceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        flashType = magicka.GetType("Magicka.Graphics.Flash", true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);

        execute = flashType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { sceneType, typeof(float) },
            null);
        update = flashType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (execute == null || update == null)
            throw new MissingMethodException("Flash behavior targets are incomplete.");

        intensityField = RuntimeReflection.RequireField(flashType, "mIntensity");
        intensitiesField = RuntimeReflection.RequireField(flashType, "mIntensities");
        legacySceneField = RuntimeReflection.RequireField(flashType, "mScene");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        ttlField = RuntimeReflection.RequireField(flashType, "mTTL");
        spellEffectsField = RuntimeReflection.RequireField(
            spellManagerType,
            "mEffects");
        spellManagerSingleton = RuntimeReflection.RequireField(
            spellManagerType,
            "mSingelton");

        InstallSceneProbe();
    }

    internal ScenarioResult SceneRelease()
    {
        object flash = NewUninitialized(flashType);
        object scene = NewUninitialized(sceneType);
        IList effects = ConfigureSpellManager();
        Invoke(execute, flash, new object[] { scene, 2f });

        bool retained = ReferenceEquals(legacySceneField.GetValue(flash), scene);
        float intensity = Convert.ToSingle(intensityField.GetValue(flash));
        float ttl = Convert.ToSingle(ttlField.GetValue(flash));
        bool registered = effects.Count == 1 && ReferenceEquals(effects[0], flash);
        bool passed = !retained && Math.Abs(intensity - 2f) < 0.0001f &&
            Math.Abs(ttl - 2f) < 0.0001f && registered;
        string actual = "scene:" + (retained ? "retained" : "released") +
            ",intensity:" + intensity + ",ttl:" + ttl +
            ",registered:" + registered;
        return new ScenarioResult(
            passed,
            actual,
            "scene:released,intensity:2,ttl:2,registered:True");
    }

    internal ScenarioResult CurrentScene()
    {
        object flash = NewUninitialized(flashType);
        object staleScene = NewUninitialized(sceneType);
        object currentScene = NewUninitialized(sceneType);
        object playState = NewUninitialized(playStateType);
        object channel = Enum.ToObject(dataChannelType, 0);

        legacySceneField.SetValue(flash, staleScene);
        intensityField.SetValue(flash, 2f);
        ttlField.SetValue(flash, 4f);
        intensitiesField.SetValue(flash, new float[3]);
        RuntimeReflection.WriteField(playState, "mScene", currentScene);
        recentPlayStateField.SetValue(null, playState);
        FlashProbe.Reset();

        Invoke(update, flash, new object[] { channel, 0.5f });

        float intensity = Convert.ToSingle(intensityField.GetValue(flash));
        float renderedIntensity = ((float[])intensitiesField.GetValue(flash))[0];
        bool current = ReferenceEquals(FlashProbe.ObservedScene, currentScene);
        bool passed = FlashProbe.Calls == 1 && current &&
            Math.Abs(intensity - 1.5f) < 0.0001f &&
            Math.Abs(renderedIntensity - 0.28125f) < 0.0001f;
        string actual = "calls:" + FlashProbe.Calls +
            ",scene:" + (current ? "current" : "stale") +
            ",intensity:" + intensity +
            ",rendered_intensity:" + renderedIntensity;
        return new ScenarioResult(
            passed,
            actual,
            "calls:1,scene:current,intensity:1.5,rendered_intensity:0.28125");
    }

    private IList ConfigureSpellManager()
    {
        object manager = NewUninitialized(spellManagerSingleton.FieldType);
        IList effects = (IList)Activator.CreateInstance(spellEffectsField.FieldType);
        spellEffectsField.SetValue(manager, effects);
        spellManagerSingleton.SetValue(null, manager);
        return effects;
    }

    private void InstallSceneProbe()
    {
        MethodInfo addRenderable = sceneType.GetMethod(
            "AddRenderableAdditiveObject",
            BindingFlags.Instance | BindingFlags.Public);
        if (addRenderable == null || addRenderable.GetParameters().Length != 2)
            throw new MissingMethodException(
                sceneType.FullName,
                "AddRenderableAdditiveObject");
        HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-flash").Patch(
                addRenderable,
                new HarmonyMethod(typeof(FlashProbe).GetMethod("ScenePrefix")),
                null,
                null);
    }

    private static void Invoke(MethodInfo method, object target, object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
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

public static class FlashProbe
{
    public static int Calls;
    public static object ObservedScene;

    public static void Reset()
    {
        Calls = 0;
        ObservedScene = null;
    }

    public static bool ScenePrefix(object __instance)
    {
        Calls++;
        ObservedScene = __instance;
        return false;
    }
}
