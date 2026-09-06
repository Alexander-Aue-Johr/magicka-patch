using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class HUDManagerScenarios
{
    private const string DisabledScenario = "hud_manager.disabled_original_hud";
    private const string EnabledScenario = "hud_manager.enabled_original_hud";

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        Type managerType = magicka.GetType(
            "Magicka.CoreFramework.GameSystem.HUDCustomisation.HUDManager",
            false);
        if (managerType == null)
        {
            const string reason = "HUDManager is not available in this Magicka version";
            report.AddNotApplicable(DisabledScenario, reason);
            report.AddNotApplicable(EnabledScenario, reason);
            return;
        }

        HUDManagerHarness harness = new HUDManagerHarness(magicka, managerType);
        report.Add(DisabledScenario, harness.Initialise(false, true));
        report.Add(EnabledScenario, harness.Initialise(true, false));
    }
}

internal sealed class HUDManagerHarness
{
    private readonly Type managerType;
    private readonly Type canvasType;
    private readonly MethodInfo initialise;

    internal HUDManagerHarness(Assembly magicka, Type managerType)
    {
        this.managerType = managerType;
        canvasType = magicka.GetType("Magicka.GameLogic.UI.UISystem.Canvas", true);
        initialise = managerType.GetMethod(
            "Initialise",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        if (initialise == null)
            throw new MissingMethodException(managerType.FullName, "Initialise");
    }

    internal ScenarioResult Initialise(bool initiallyEnabled, bool canvasInitiallyEnabled)
    {
        object manager = FormatterServices.GetUninitializedObject(managerType);
        object canvas = FormatterServices.GetUninitializedObject(canvasType);
        RuntimeReflection.WriteField(manager, "mUIEnabled", initiallyEnabled);
        RuntimeReflection.WriteField(manager, "mCanvas", canvas);
        RuntimeReflection.WriteField(canvas, "mEnabled", canvasInitiallyEnabled);

        initialise.Invoke(manager, null);

        bool actualUi = (bool)RuntimeReflection.ReadField(manager, "mUIEnabled");
        bool actualCanvas = (bool)RuntimeReflection.ReadField(canvas, "mEnabled");
        string actual = "ui=" + actualUi + ",canvas=" + actualCanvas;
        const string expected = "ui=True,canvas=False";
        return new ScenarioResult(actual == expected, actual, expected);
    }
}
