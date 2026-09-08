using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class SpellWheelPlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        SpellWheelPlayStateHarness.InstallProbeEarly(magicka);
        SpellWheelPlayStateHarness harness =
            new SpellWheelPlayStateHarness(magicka);
        try
        {
            report.Add(
                "spell_wheel.initialize_release",
                harness.InitializeRelease());
            report.Add(
                "spell_wheel.current_scene",
                harness.CurrentScene());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class SpellWheelPlayStateHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-spell-wheel";

    private readonly Type spellWheelType;
    private readonly Type playStateType;
    private readonly Type sceneType;
    private readonly Type dataChannelType;
    private readonly Type iconType;
    private readonly Type renderDataType;
    private readonly Type playerType;
    private readonly Type avatarType;
    private readonly Type bodyType;
    private readonly Type globalSettingsType;
    private readonly MethodInfo initialize;
    private readonly MethodInfo update;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo globalSettingsSingletonField;
    private readonly FieldInfo legacyPlayStateField;
    private readonly object originalRecentPlayState;
    private readonly object originalGlobalSettings;
    private readonly HarmonyInstance harmony;

    internal SpellWheelPlayStateHarness(Assembly magicka)
    {
        spellWheelType = magicka.GetType(
            "Magicka.GameLogic.UI.SpellWheel",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        sceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        iconType = spellWheelType.GetNestedType(
            "Icon",
            BindingFlags.Public | BindingFlags.NonPublic);
        renderDataType = spellWheelType.GetNestedType(
            "RenderData",
            BindingFlags.Public | BindingFlags.NonPublic);
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        globalSettingsType = magicka.GetType("Magicka.GlobalSettings", true);
        initialize = spellWheelType.GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playStateType },
            null);
        update = spellWheelType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        globalSettingsSingletonField = RuntimeReflection.RequireField(
            globalSettingsType,
            "mSingelton");
        legacyPlayStateField = spellWheelType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (iconType == null || renderDataType == null || initialize == null ||
            update == null)
            throw new MissingMemberException(
                "SpellWheel behavior contract is incomplete.");
        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalGlobalSettings = globalSettingsSingletonField.GetValue(null);
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        SpellWheelPlayStateProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        globalSettingsSingletonField.SetValue(null, originalGlobalSettings);
    }

    internal ScenarioResult InitializeRelease()
    {
        object spellWheel = NewUninitialized(spellWheelType);
        object supplied = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(
            spellWheel,
            "mIcons",
            Array.CreateInstance(iconType, 0));
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(spellWheel, null);

        Invoke(initialize, spellWheel, new object[] { supplied });
        bool released = legacyPlayStateField == null ||
            legacyPlayStateField.GetValue(spellWheel) == null;
        return new ScenarioResult(
            released,
            released ? "released" : "retained",
            "released");
    }

    internal ScenarioResult CurrentScene()
    {
        object staleScene = NewUninitialized(sceneType);
        object currentScene = NewUninitialized(sceneType);
        object stale = NewPlayState(staleScene);
        object current = NewPlayState(currentScene);
        recentPlayStateField.SetValue(null, current);

        object spellWheel = NewUninitialized(spellWheelType);
        object player = NewUninitialized(playerType);
        object avatar = NewUninitialized(avatarType);
        RuntimeReflection.WriteField(
            avatar,
            "mBody",
            NewUninitialized(bodyType));
        RuntimeReflection.WriteField(
            player,
            "mAvatar",
            new WeakReference(avatar));
        RuntimeReflection.WriteField(
            spellWheel,
            "mPlayer",
            new WeakReference(player));
        RuntimeReflection.WriteField(
            spellWheel,
            "mIcons",
            Array.CreateInstance(iconType, 0));
        object renderData = Activator.CreateInstance(renderDataType, true);
        Array renderDataItems = Array.CreateInstance(renderDataType, 1);
        renderDataItems.SetValue(renderData, 0);
        RuntimeReflection.WriteField(
            spellWheel,
            "mRenderData",
            renderDataItems);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(spellWheel, stale);

        object settings = NewUninitialized(globalSettingsType);
        PropertyInfo spellWheelSetting = globalSettingsType.GetProperty(
            "SpellWheel",
            BindingFlags.Instance | BindingFlags.Public);
        spellWheelSetting.SetValue(
            settings,
            Enum.Parse(spellWheelSetting.PropertyType, "On", false),
            null);
        globalSettingsSingletonField.SetValue(null, settings);

        SpellWheelPlayStateProbe.Reset();
        SpellWheelPlayStateProbe.Enabled = true;
        Invoke(
            update,
            spellWheel,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.1f });
        GC.KeepAlive(player);
        GC.KeepAlive(avatar);
        SpellWheelPlayStateProbe.Enabled = false;

        bool currentUsed = ReferenceEquals(
            SpellWheelPlayStateProbe.ObservedScene,
            currentScene);
        bool sameRenderData = ReferenceEquals(
            SpellWheelPlayStateProbe.RenderData,
            renderData);
        bool passed = SpellWheelPlayStateProbe.Calls == 1 && currentUsed &&
            sameRenderData;
        string actual = "calls:" + SpellWheelPlayStateProbe.Calls +
            ",scene:" + (currentUsed ? "current" : "stale") +
            ",render_data:" + (sameRenderData ? "same" : "changed");
        return new ScenarioResult(
            passed,
            actual,
            "calls:1,scene:current,render_data:same");
    }

    private object NewPlayState(object scene)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mScene", scene);
        return playState;
    }

    internal static void InstallProbeEarly(Assembly magicka)
    {
        Type spellWheel = magicka.GetType(
            "Magicka.GameLogic.UI.SpellWheel",
            true);
        Type renderData = spellWheel.GetNestedType(
            "RenderData",
            BindingFlags.Public | BindingFlags.NonPublic);
        Type scene = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        Type dataChannel = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        MethodInfo target = null;
        MethodInfo[] methods = scene.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name != "AddRenderableGUIObject" ||
                methods[index].ReturnType != typeof(void) ||
                parameters.Length != 2 ||
                parameters[0].ParameterType != dataChannel ||
                !parameters[1].ParameterType.IsAssignableFrom(renderData))
                continue;
            if (target != null)
                throw new InvalidOperationException(
                    "Multiple SpellWheel render targets matched.");
            target = methods[index];
        }
        if (target == null)
            throw new MissingMethodException(
                scene.FullName,
                "AddRenderableGUIObject");
        HarmonyInstance.Create(HarmonyOwner).Patch(
            target,
            new HarmonyMethod(
                typeof(SpellWheelPlayStateProbe).GetMethod("Prefix")),
            null,
            null);
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

public static class SpellWheelPlayStateProbe
{
    public static bool Enabled;
    public static int Calls;
    public static object ObservedScene;
    public static object RenderData;

    public static void Reset()
    {
        Calls = 0;
        ObservedScene = null;
        RenderData = null;
    }

    public static bool Prefix(object __instance, object __1)
    {
        if (!Enabled)
            return true;
        Calls++;
        ObservedScene = __instance;
        RenderData = __1;
        return false;
    }
}
