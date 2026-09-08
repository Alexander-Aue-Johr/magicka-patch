using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class InGameMenuPlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        InGameMenuPlayStateHarness.InstallProbe(magicka);
        InGameMenuPlayStateHarness harness =
            new InGameMenuPlayStateHarness(magicka);
        try
        {
            report.Add(
                "in_game_menu.initialize_release",
                harness.InitializeReleasesPlayState());
            report.Add(
                "in_game_menu.current_scene",
                harness.UpdateUsesCurrentScene());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class InGameMenuPlayStateHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-in-game-menu";

    private readonly Type inGameMenuType;
    private readonly Type inGameMenuMainType;
    private readonly Type playStateType;
    private readonly Type sceneType;
    private readonly Type dataChannelType;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo mainSingletonField;
    private readonly MethodInfo initialize;
    private readonly MethodInfo update;
    private readonly object originalLegacyPlayState;
    private readonly object originalRecentPlayState;
    private readonly object originalMainSingleton;
    private readonly HarmonyInstance harmony;

    internal InGameMenuPlayStateHarness(Assembly magicka)
    {
        inGameMenuType = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu",
            true);
        inGameMenuMainType = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuMain",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        sceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        legacyPlayStateField = RequireField(
            inGameMenuType,
            "sPlayState",
            playStateType,
            BindingFlags.Static | BindingFlags.NonPublic);
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        mainSingletonField = RequireField(
            inGameMenuMainType,
            "sSingelton",
            inGameMenuMainType,
            BindingFlags.Static | BindingFlags.NonPublic);
        initialize = inGameMenuType.GetMethod(
            "Initialize",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { playStateType },
            null);
        update = inGameMenuMainType.GetMethod(
            "IUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (initialize == null || update == null)
            throw new MissingMemberException(
                "InGameMenu behavior contract is incomplete.");

        originalLegacyPlayState = legacyPlayStateField.GetValue(null);
        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalMainSingleton = mainSingletonField.GetValue(null);
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        InGameMenuPlayStateProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
        legacyPlayStateField.SetValue(null, originalLegacyPlayState);
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        mainSingletonField.SetValue(null, originalMainSingleton);
    }

    internal ScenarioResult InitializeReleasesPlayState()
    {
        object supplied = NewUninitialized(playStateType);
        object menu = NewUninitialized(inGameMenuMainType);
        legacyPlayStateField.SetValue(null, null);
        mainSingletonField.SetValue(null, menu);

        Invoke(initialize, null, new object[] { supplied });

        bool released = legacyPlayStateField.GetValue(null) == null;
        return new ScenarioResult(
            released,
            released ? "released" : "retained",
            "released");
    }

    internal ScenarioResult UpdateUsesCurrentScene()
    {
        object staleScene = NewUninitialized(sceneType);
        object currentScene = NewUninitialized(sceneType);
        object stale = NewPlayState(staleScene);
        object current = NewPlayState(currentScene);
        object menu = NewUninitialized(inGameMenuMainType);
        legacyPlayStateField.SetValue(null, stale);
        recentPlayStateField.SetValue(null, current);

        InGameMenuPlayStateProbe.Reset();
        InGameMenuPlayStateProbe.Enabled = true;
        try
        {
            Invoke(
                update,
                menu,
                new object[] {
                    Enum.ToObject(dataChannelType, 0),
                    0.1f
                });
        }
        catch (NullReferenceException)
        {
            // The uninitialized menu has no item list. Rendering is observed
            // before the override reaches that unrelated field.
        }
        finally
        {
            InGameMenuPlayStateProbe.Enabled = false;
        }

        bool currentUsed = ReferenceEquals(
            InGameMenuPlayStateProbe.ObservedScene,
            currentScene);
        bool passed = InGameMenuPlayStateProbe.Calls == 1 && currentUsed;
        return new ScenarioResult(
            passed,
            "calls:" + InGameMenuPlayStateProbe.Calls +
                ",scene:" + (currentUsed ? "current" : "stale"),
            "calls:1,scene:current");
    }

    internal static void InstallProbe(Assembly magicka)
    {
        Type menu = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu",
            true);
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
                !parameters[1].ParameterType.IsAssignableFrom(menu))
                continue;
            if (target != null)
                throw new InvalidOperationException(
                    "Multiple InGameMenu render targets matched.");
            target = methods[index];
        }
        if (target == null)
            throw new MissingMethodException(
                scene.FullName,
                "AddRenderableGUIObject");
        HarmonyInstance.Create(HarmonyOwner).Patch(
            target,
            new HarmonyMethod(
                typeof(InGameMenuPlayStateProbe).GetMethod("Prefix")),
            null,
            null);
    }

    private object NewPlayState(object scene)
    {
        object state = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(state, "mScene", scene);
        return state;
    }

    private static FieldInfo RequireField(
        Type type,
        string name,
        Type expectedType,
        BindingFlags flags)
    {
        FieldInfo field = type.GetField(name, flags);
        if (field == null || field.FieldType != expectedType)
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

public static class InGameMenuPlayStateProbe
{
    public static bool Enabled;
    public static int Calls;
    public static object ObservedScene;

    public static void Reset()
    {
        Calls = 0;
        ObservedScene = null;
    }

    public static bool Prefix(object __instance)
    {
        if (!Enabled)
            return true;
        Calls++;
        ObservedScene = __instance;
        return false;
    }
}
