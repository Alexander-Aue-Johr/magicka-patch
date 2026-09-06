using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class CompanyStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        CompanyStateHarness harness = new CompanyStateHarness(magicka);
        report.Add("company_state.exit_cleanup_order", harness.Exit());
    }
}

internal sealed class CompanyStateHarness
{
    private readonly Type companyStateType;
    private readonly Type contentManagerType;
    private readonly Type controlManagerType;
    private readonly Type tomeType;
    private readonly FieldInfo contentManagerField;
    private readonly MethodInfo onExit;

    internal CompanyStateHarness(Assembly magicka)
    {
        companyStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.CompanyState",
            true);
        controlManagerType = magicka.GetType(
            "Magicka.GameLogic.Controls.ControlManager",
            true);
        tomeType = magicka.GetType("Magicka.GameLogic.UI.Tome", true);
        contentManagerField = RuntimeReflection.RequireField(
            companyStateType,
            "mContentManager");
        contentManagerType = contentManagerField.FieldType;
        onExit = companyStateType.GetMethod(
            "OnExit",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (onExit == null)
            throw new MissingMethodException(companyStateType.FullName, "OnExit");
        InstallRecorders();
    }

    internal ScenarioResult Exit()
    {
        object state = NewUninitialized(companyStateType);
        object contentManager = NewUninitialized(contentManagerType);
        CompanyStateProbe.ControlManager = NewUninitialized(controlManagerType);
        CompanyStateProbe.Tome = NewUninitialized(tomeType);
        CompanyStateProbe.Operations.Clear();
        contentManagerField.SetValue(state, contentManager);

        string failure = "none";
        try
        {
            onExit.Invoke(state, null);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            failure = inner.GetType().FullName;
        }

        string actual = string.Join(",", CompanyStateProbe.Operations.ToArray()) +
            ";failure:" + failure;
        const string expected = "controllers,camera,light,content;failure:none";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private void InstallRecorders()
    {
        PropertyInfo controlInstance = controlManagerType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo clearControllers = controlManagerType.GetMethod(
            "ClearControllers",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        PropertyInfo tomeInstance = tomeType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public);
        Type cameraAnimationType = tomeType.GetNestedType(
            "CameraAnimation",
            BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo setCamera = tomeType.GetMethod(
            "SetCameraAnimation",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { cameraAnimationType },
            null);
        PropertyInfo lightIntensity = tomeType.GetProperty(
            "LightIntensity",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo dispose = contentManagerType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        if (controlInstance == null || clearControllers == null ||
            tomeInstance == null || cameraAnimationType == null ||
            setCamera == null || lightIntensity == null ||
            lightIntensity.GetSetMethod() == null || dispose == null)
            throw new MissingMethodException("CompanyState test dependencies are incomplete.");

        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-company-state");
        harmony.Patch(
            controlInstance.GetGetMethod(),
            new HarmonyMethod(
                typeof(CompanyStateProbe).GetMethod("ControlManagerPrefix")
                    .MakeGenericMethod(new Type[] { controlManagerType })),
            null,
            null);
        harmony.Patch(
            clearControllers,
            new HarmonyMethod(typeof(CompanyStateProbe).GetMethod("ControllersPrefix")),
            null,
            null);
        harmony.Patch(
            tomeInstance.GetGetMethod(),
            new HarmonyMethod(
                typeof(CompanyStateProbe).GetMethod("TomePrefix")
                    .MakeGenericMethod(new Type[] { tomeType })),
            null,
            null);
        harmony.Patch(
            setCamera,
            new HarmonyMethod(typeof(CompanyStateProbe).GetMethod("CameraPrefix")),
            null,
            null);
        harmony.Patch(
            lightIntensity.GetSetMethod(),
            new HarmonyMethod(typeof(CompanyStateProbe).GetMethod("LightPrefix")),
            null,
            null);
        harmony.Patch(
            dispose,
            new HarmonyMethod(typeof(CompanyStateProbe).GetMethod("ContentPrefix")),
            null,
            null);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class CompanyStateProbe
{
    public static object ControlManager;
    public static object Tome;
    public static readonly List<string> Operations = new List<string>();

    public static bool ControlManagerPrefix<T>(ref T __result)
    {
        __result = (T)ControlManager;
        return false;
    }

    public static bool TomePrefix<T>(ref T __result)
    {
        __result = (T)Tome;
        return false;
    }

    public static bool ControllersPrefix()
    {
        Operations.Add("controllers");
        return false;
    }

    public static bool CameraPrefix()
    {
        Operations.Add("camera");
        return false;
    }

    public static bool LightPrefix()
    {
        Operations.Add("light");
        return false;
    }

    public static bool ContentPrefix()
    {
        Operations.Add("content");
        return false;
    }
}
