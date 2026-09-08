using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class KeyboardMouseClearScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        KeyboardMouseClearHarness harness =
            new KeyboardMouseClearHarness(magicka);
        report.Add("keyboard_mouse_clear.seeded", harness.Seeded());
        report.Add("keyboard_mouse_clear.empty", harness.Empty());
    }
}

internal sealed class KeyboardMouseClearHarness
{
    private readonly Type controllerType;
    private readonly MethodInfo clear;
    private readonly FieldInfo cursorPressedTarget;
    private readonly FieldInfo lockedTarget;
    private readonly FieldInfo stillPressing;
    private readonly FieldInfo interactMoveLock;

    internal KeyboardMouseClearHarness(Assembly magicka)
    {
        controllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.KeyboardMouseController",
            true);
        clear = controllerType.GetMethod(
            "Clear",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        cursorPressedTarget = RequireField("mCursorPressedTarget");
        lockedTarget = RequireField("mLockedTarget");
        stillPressing = RequireField("mStillPressing");
        interactMoveLock = RequireField("mInteractMoveLock");
        if (clear == null || clear.ReturnType != typeof(void))
            throw new MissingMethodException(controllerType.FullName, "Clear");
    }

    internal ScenarioResult Seeded()
    {
        object controller = NewController();
        cursorPressedTarget.SetValue(controller, new object());
        lockedTarget.SetValue(controller, new object());
        stillPressing.SetValue(controller, true);
        interactMoveLock.SetValue(controller, true);
        clear.Invoke(controller, null);
        return Result(IsClear(controller), true);
    }

    internal ScenarioResult Empty()
    {
        object controller = NewController();
        clear.Invoke(controller, null);
        return Result(IsClear(controller), true);
    }

    private object NewController()
    {
        return FormatterServices.GetUninitializedObject(controllerType);
    }

    private bool IsClear(object controller)
    {
        return cursorPressedTarget.GetValue(controller) == null &&
            lockedTarget.GetValue(controller) == null &&
            !(bool)stillPressing.GetValue(controller) &&
            !(bool)interactMoveLock.GetValue(controller);
    }

    private FieldInfo RequireField(string name)
    {
        FieldInfo field = controllerType.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(controllerType.FullName, name);
        return field;
    }

    private static ScenarioResult Result(bool actual, bool expected)
    {
        return new ScenarioResult(
            actual == expected,
            actual.ToString(),
            expected.ToString());
    }
}
