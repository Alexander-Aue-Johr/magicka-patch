using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class GenericHealthBarScenarios
{
    internal static void Prepare(Assembly magicka)
    {
        GenericHealthBarHarness.InstallProbeEarly(magicka);
    }

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        GenericHealthBarHarness harness = new GenericHealthBarHarness(magicka);
        try
        {
            report.Add(
                "generic_health_bar.current_scene",
                harness.CurrentScene());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class GenericHealthBarHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-generic-health-bar";

    private readonly Type dataChannelType;
    private readonly Type healthBarType;
    private readonly Type playStateType;
    private readonly Type renderDataType;
    private readonly Type sceneType;
    private readonly MethodInfo update;
    private readonly FieldInfo recentPlayStateField;
    private readonly HarmonyInstance harmony;

    internal GenericHealthBarHarness(Assembly magicka)
    {
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        sceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        healthBarType = magicka.GetType(
            "Magicka.GameLogic.UI.GenericHealthBar",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        renderDataType = healthBarType.GetNestedType(
            "RenderData",
            BindingFlags.NonPublic);
        update = healthBarType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        if (renderDataType == null || update == null)
            throw new MissingMemberException(
                "GenericHealthBar behavior contract is incomplete.");
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        GenericHealthBarProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult CurrentScene()
    {
        object healthBar = NewUninitialized(healthBarType);
        object staleScene = NewUninitialized(sceneType);
        object currentScene = NewUninitialized(sceneType);
        object playState = NewUninitialized(playStateType);
        Array renderData = Array.CreateInstance(renderDataType, 1);
        object item = NewUninitialized(renderDataType);
        renderData.SetValue(item, 0);

        RuntimeReflection.WriteField(healthBar, "mScene", staleScene);
        RuntimeReflection.WriteField(healthBar, "mActive", true);
        RuntimeReflection.WriteField(healthBar, "mRenderData", renderData);
        RuntimeReflection.WriteField(healthBar, "mCounterStart", 100f);
        RuntimeReflection.WriteField(healthBar, "mCounterCurrent", 50f);
        RuntimeReflection.WriteField(healthBar, "mCounterEnd", 0f);
        RuntimeReflection.WriteField(healthBar, "mFadeTime", 1f);
        RuntimeReflection.WriteField(healthBar, "mCurrentFadeTime", 0.5f);
        RuntimeReflection.WriteField(playState, "mScene", currentScene);
        recentPlayStateField.SetValue(null, playState);

        GenericHealthBarProbe.Reset();
        GenericHealthBarProbe.Enabled = true;
        Invoke(
            update,
            healthBar,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.1f });
        GenericHealthBarProbe.Enabled = false;

        bool current = ReferenceEquals(
            GenericHealthBarProbe.ObservedScene,
            currentScene);
        bool sameRenderData = ReferenceEquals(
            GenericHealthBarProbe.RenderData,
            item);
        bool passed = GenericHealthBarProbe.Calls == 1 && current &&
            sameRenderData;
        string actual = "calls:" + GenericHealthBarProbe.Calls +
            ",scene:" + (current ? "current" : "stale") +
            ",render_data:" + (sameRenderData ? "same" : "changed");
        return new ScenarioResult(
            passed,
            actual,
            "calls:1,scene:current,render_data:same");
    }

    internal static void InstallProbeEarly(Assembly magicka)
    {
        Type healthBarType = magicka.GetType(
            "Magicka.GameLogic.UI.GenericHealthBar",
            true);
        Type renderDataType = healthBarType.GetNestedType(
            "RenderData",
            BindingFlags.NonPublic);
        Type sceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        Type dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        MethodInfo target = null;
        MethodInfo[] methods = sceneType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name != "AddRenderableGUIObject" ||
                methods[index].ReturnType != typeof(void) ||
                parameters.Length != 2 ||
                parameters[0].ParameterType != dataChannelType ||
                !parameters[1].ParameterType.IsAssignableFrom(renderDataType))
                continue;
            if (target != null)
                throw new InvalidOperationException(
                    "Multiple GenericHealthBar render targets matched.");
            target = methods[index];
        }
        if (target == null)
            throw new MissingMethodException(
                sceneType.FullName,
                "AddRenderableGUIObject");

        HarmonyInstance.Create(HarmonyOwner).Patch(
            target,
            new HarmonyMethod(
                typeof(GenericHealthBarProbe).GetMethod("Prefix")),
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

public static class GenericHealthBarProbe
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
