using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class ArcaneBladeScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ArcaneBladeHarness harness = new ArcaneBladeHarness(magicka);
        try
        {
            report.Add(
                "arcane_blade.initialize_current_scene",
                harness.InitializeCurrentScene());
            report.Add(
                "arcane_blade.update_current_scene",
                harness.UpdateCurrentScene());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class ArcaneBladeHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-arcane-blade";

    private readonly Type bladeType;
    private readonly Type playStateType;
    private readonly Type sceneType;
    private readonly Type elementsType;
    private readonly Type dataChannelType;
    private readonly MethodInfo initialize;
    private readonly MethodInfo update;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo verticesField;
    private readonly FieldInfo renderDataField;
    private readonly FieldInfo lightField;
    private readonly FieldInfo effectsField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo deadField;
    private readonly FieldInfo alphaField;
    private readonly FieldInfo rangeField;
    private readonly FieldInfo maxRangeField;
    private readonly FieldInfo spellManagerSingletonField;
    private readonly object originalRecentPlayState;
    private readonly object originalSpellManager;
    private readonly HarmonyInstance harmony;

    internal ArcaneBladeHarness(Assembly magicka)
    {
        bladeType = magicka.GetType(
            "Magicka.GameLogic.Spells.ArcaneBlade",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        PropertyInfo sceneProperty = playStateType.GetProperty(
            "Scene",
            BindingFlags.Instance | BindingFlags.Public);
        if (sceneProperty == null)
            throw new MissingMemberException(playStateType.FullName, "Scene");
        sceneType = sceneProperty.PropertyType;
        elementsType = magicka.GetType("Magicka.Elements", true);
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        Type itemType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.Item",
            true);
        initialize = bladeType.GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[]
            {
                playStateType,
                itemType,
                elementsType,
                typeof(float)
            },
            null);
        update = bladeType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (initialize == null || update == null)
            throw new MissingMethodException(
                "ArcaneBlade behavior targets are incomplete.");

        playStateField = bladeType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        recentPlayStateField = RequireField(
            playStateType,
            "sRecentPlayState");
        verticesField = RequireField(bladeType, "mVertices");
        renderDataField = RequireField(bladeType, "mRenderData");
        lightField = RequireField(bladeType, "mLight");
        effectsField = RequireField(bladeType, "mEffects");
        ownerField = RequireField(bladeType, "mOwner");
        deadField = RequireField(bladeType, "mDead");
        alphaField = RequireField(bladeType, "mAlpha");
        rangeField = RequireField(bladeType, "mRange");
        maxRangeField = RequireField(bladeType, "mMaxRange");

        Type spellManager = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        spellManagerSingletonField = RequireField(
            spellManager,
            "mSingelton");
        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalSpellManager = spellManagerSingletonField.GetValue(null);
        spellManagerSingletonField.SetValue(
            null,
            NewUninitialized(spellManager));
        harmony = InstallStubs(spellManager);
    }

    internal void Dispose()
    {
        ArcaneBladeProbe.Reset();
        harmony.UnpatchAll(HarmonyOwner);
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        spellManagerSingletonField.SetValue(null, originalSpellManager);
    }

    internal ScenarioResult InitializeCurrentScene()
    {
        object currentScene = NewUninitialized(sceneType);
        object suppliedScene = NewUninitialized(sceneType);
        object current = NewPlayState(currentScene);
        object supplied = NewPlayState(suppliedScene);
        object blade = NewBlade(false);
        recentPlayStateField.SetValue(null, current);
        ArcaneBladeProbe.Reset();
        ArcaneBladeProbe.Enabled = true;

        Exception failure = null;
        try
        {
            Invoke(
                initialize,
                blade,
                new object[]
                {
                    supplied,
                    null,
                    Enum.ToObject(elementsType, 0),
                    2f
                });
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        ArcaneBladeProbe.Enabled = false;

        bool currentUsed = ReferenceEquals(
            ArcaneBladeProbe.EnabledScene,
            currentScene);
        bool released = playStateField == null ||
            playStateField.GetValue(blade) == null;
        bool passed = failure == null && currentUsed && released &&
            ArcaneBladeProbe.EnableCalls == 1 &&
            ArcaneBladeProbe.AddEffectCalls == 1;
        return new ScenarioResult(
            passed,
            "exception:" +
                (failure == null ? "none" : failure.GetType().FullName) +
                ",scene:" + (currentUsed ? "current" : "supplied") +
                ",play_state:" + (released ? "released" : "retained") +
                ",effects:" + ArcaneBladeProbe.AddEffectCalls,
            "exception:none,scene:current,play_state:released,effects:1");
    }

    internal ScenarioResult UpdateCurrentScene()
    {
        object currentScene = NewUninitialized(sceneType);
        object staleScene = NewUninitialized(sceneType);
        object current = NewPlayState(currentScene);
        object stale = NewPlayState(staleScene);
        object blade = NewBlade(true);
        recentPlayStateField.SetValue(null, current);
        if (playStateField != null)
            playStateField.SetValue(blade, stale);
        ArcaneBladeProbe.Reset();
        ArcaneBladeProbe.Enabled = true;

        Exception failure = null;
        try
        {
            Invoke(
                update,
                blade,
                new object[]
                {
                    Enum.ToObject(dataChannelType, 0),
                    0.01f
                });
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        ArcaneBladeProbe.Enabled = false;

        bool currentUsed = ReferenceEquals(
            ArcaneBladeProbe.RenderScene,
            currentScene);
        bool passed = failure == null && currentUsed &&
            ArcaneBladeProbe.RenderCalls == 1;
        return new ScenarioResult(
            passed,
            "exception:" +
                (failure == null ? "none" : failure.GetType().FullName) +
                ",scene:" + (currentUsed ? "current" : "stale") +
                ",renders:" + ArcaneBladeProbe.RenderCalls,
            "exception:none,scene:current,renders:1");
    }

    private object NewPlayState(object scene)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mScene", scene);
        return playState;
    }

    private object NewBlade(bool withRenderData)
    {
        object blade = NewUninitialized(bladeType);
        Array vertices = Array.CreateInstance(
            verticesField.FieldType.GetElementType(),
            32);
        verticesField.SetValue(blade, vertices);
        lightField.SetValue(blade, NewUninitialized(lightField.FieldType));
        effectsField.SetValue(
            blade,
            Array.CreateInstance(
                effectsField.FieldType.GetElementType(),
                4));
        ownerField.SetValue(blade, null);
        deadField.SetValue(blade, false);
        alphaField.SetValue(blade, 1f);
        rangeField.SetValue(blade, 2f);
        maxRangeField.SetValue(blade, 2f);
        if (withRenderData)
            renderDataField.SetValue(blade, NewRenderDataArray());
        return blade;
    }

    private Array NewRenderDataArray()
    {
        Type renderData = renderDataField.FieldType.GetElementType();
        ConstructorInfo constructor = renderData.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            new Type[] { typeof(int) },
            null);
        if (constructor == null)
            throw new MissingMethodException(renderData.FullName, ".ctor");
        Array result = Array.CreateInstance(renderData, 3);
        for (int index = 0; index < result.Length; index++)
            result.SetValue(constructor.Invoke(new object[] { 32 }), index);
        return result;
    }

    private HarmonyInstance InstallStubs(Type spellManager)
    {
        MethodInfo enable = lightField.FieldType.GetMethod(
            "Enable",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { sceneType },
            null);
        MethodInfo addEffect = null;
        MethodInfo[] spellMethods = spellManager.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        for (int index = 0; index < spellMethods.Length; index++)
        {
            if (spellMethods[index].Name == "AddSpellEffect" &&
                spellMethods[index].GetParameters().Length == 1)
                addEffect = spellMethods[index];
        }
        MethodInfo addRenderable = null;
        MethodInfo[] sceneMethods = sceneType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        for (int index = 0; index < sceneMethods.Length; index++)
        {
            if (sceneMethods[index].Name ==
                    "AddRenderableAdditiveObject" &&
                sceneMethods[index].GetParameters().Length == 2)
                addRenderable = sceneMethods[index];
        }
        if (enable == null || addEffect == null || addRenderable == null)
            throw new MissingMethodException(
                "ArcaneBlade dependency probes are incomplete.");

        HarmonyInstance instance = HarmonyInstance.Create(HarmonyOwner);
        instance.Patch(
            enable,
            new HarmonyMethod(
                typeof(ArcaneBladeProbe).GetMethod("EnablePrefix")),
            null,
            null);
        instance.Patch(
            addEffect,
            new HarmonyMethod(
                typeof(ArcaneBladeProbe).GetMethod("AddEffectPrefix")),
            null,
            null);
        instance.Patch(
            addRenderable,
            new HarmonyMethod(
                typeof(ArcaneBladeProbe).GetMethod("RenderPrefix")),
            null,
            null);
        return instance;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static object Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
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

public static class ArcaneBladeProbe
{
    public static bool Enabled;
    public static object EnabledScene;
    public static object RenderScene;
    public static int EnableCalls;
    public static int AddEffectCalls;
    public static int RenderCalls;

    public static void Reset()
    {
        Enabled = false;
        EnabledScene = null;
        RenderScene = null;
        EnableCalls = 0;
        AddEffectCalls = 0;
        RenderCalls = 0;
    }

    public static bool EnablePrefix(object iScene)
    {
        if (!Enabled)
            return true;
        EnableCalls++;
        EnabledScene = iScene;
        return false;
    }

    public static bool AddEffectPrefix()
    {
        if (!Enabled)
            return true;
        AddEffectCalls++;
        return false;
    }

    public static bool RenderPrefix(object __instance)
    {
        if (!Enabled)
            return true;
        RenderCalls++;
        RenderScene = __instance;
        return false;
    }
}
