using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;
using Harmony.ILCopying;

internal static class GameSceneMenuControllerResetScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        GameSceneMenuControllerResetHarness harness =
            new GameSceneMenuControllerResetHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "game_scene.menu_controller_reset",
            harness.ClearsTransientState());
        report.Add(
            "game_scene.menu_controller_already_clear",
            harness.PreservesClearState());
    }
}

internal sealed class GameSceneMenuControllerResetHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.GameSceneMenuControllerResetPatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly Type controlManagerType;
    private readonly Type keyboardMouseType;
    private readonly FieldInfo singletonField;
    private readonly FieldInfo menuControllerField;
    private readonly FieldInfo cursorPressedTargetField;
    private readonly FieldInfo lockedTargetField;
    private readonly FieldInfo stillPressingField;
    private readonly FieldInfo interactMoveLockField;
    private readonly MethodInfo clearMethod;
    private readonly MethodInfo runtimePrefix;
    private readonly bool manualDestroyReset;

    internal GameSceneMenuControllerResetHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        Type gameScene = magicka.GetType("Magicka.Levels.GameScene", true);
        controlManagerType = magicka.GetType(
            "Magicka.GameLogic.Controls.ControlManager",
            true);
        keyboardMouseType = magicka.GetType(
            "Magicka.GameLogic.Controls.KeyboardMouseController",
            true);
        singletonField = RuntimeReflection.RequireField(
            controlManagerType,
            "mSingelton");
        menuControllerField = RuntimeReflection.RequireField(
            controlManagerType,
            "mMenuController");
        cursorPressedTargetField = RuntimeReflection.RequireField(
            keyboardMouseType,
            "mCursorPressedTarget");
        lockedTargetField = RuntimeReflection.RequireField(
            keyboardMouseType,
            "mLockedTarget");
        stillPressingField = RuntimeReflection.RequireField(
            keyboardMouseType,
            "mStillPressing");
        interactMoveLockField = RuntimeReflection.RequireField(
            keyboardMouseType,
            "mInteractMoveLock");
        clearMethod = keyboardMouseType.GetMethod(
            "Clear",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        MethodInfo destroy = gameScene.GetMethod(
            "Destroy",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(bool) },
            null);
        if (clearMethod == null || clearMethod.ReturnType != typeof(void))
            throw new MissingMethodException(keyboardMouseType.FullName, "Clear");
        if (destroy == null || destroy.ReturnType != typeof(void))
            throw new MissingMethodException(gameScene.FullName, "Destroy");
        manualDestroyReset = CallsMethod(destroy, clearMethod);

        runtimePrefix = null;
        if (runtimePatchEnabled)
        {
            Type patch = Type.GetType(PatchTypeName, false);
            runtimePrefix = patch == null
                ? null
                : patch.GetMethod(
                    "Prefix",
                    BindingFlags.Static | BindingFlags.Public);
        }
    }

    internal ScenarioResult ClearsTransientState()
    {
        return RunScenario(true);
    }

    internal ScenarioResult PreservesClearState()
    {
        return RunScenario(false);
    }

    private ScenarioResult RunScenario(bool seedState)
    {
        object previous = singletonField.GetValue(null);
        try
        {
            object manager = NewUninitialized(controlManagerType);
            object controller = NewUninitialized(keyboardMouseType);
            menuControllerField.SetValue(manager, controller);
            singletonField.SetValue(null, manager);
            if (seedState)
            {
                cursorPressedTargetField.SetValue(controller, new object());
                lockedTargetField.SetValue(controller, new object());
                stillPressingField.SetValue(controller, true);
                interactMoveLockField.SetValue(controller, true);
            }

            if (runtimePrefix != null)
                Invoke(runtimePrefix, null);
            else if (manualDestroyReset)
                Invoke(clearMethod, controller);

            bool clear = cursorPressedTargetField.GetValue(controller) == null &&
                lockedTargetField.GetValue(controller) == null &&
                !(bool)stillPressingField.GetValue(controller) &&
                !(bool)interactMoveLockField.GetValue(controller);
            string actual = clear ? "clear" : "retained";
            const string expected = "clear";
            return new ScenarioResult(actual == expected, actual, expected);
        }
        finally
        {
            singletonField.SetValue(null, previous);
        }
    }

    private static bool CallsMethod(MethodInfo source, MethodInfo target)
    {
        DynamicMethod reader = new DynamicMethod(
            "ReadGameSceneDestroyBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(GameSceneMenuControllerResetScenarios),
            true);
        List<ILInstruction> instructions = MethodBodyReader.GetInstructions(
            reader.GetILGenerator(),
            source);
        for (int index = 0; index < instructions.Count; index++)
        {
            CodeInstruction instruction = instructions[index].GetCodeInstruction();
            if ((instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, target))
                return true;
        }
        return false;
    }

    private static void Invoke(MethodInfo method, object target)
    {
        try
        {
            method.Invoke(target, null);
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
