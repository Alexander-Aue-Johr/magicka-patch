using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ControlManagerScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ControlManagerHarness harness = new ControlManagerHarness(magicka);
        report.Add("control_manager.null_controller", harness.InvalidController(true));
        report.Add(
            "control_manager.playerless_controller",
            harness.InvalidController(false));
        report.Add("control_manager.valid_controller", harness.ValidController());
    }
}

internal sealed class ControlManagerHarness
{
    private readonly Type managerType;
    private readonly Type controllerType;
    private readonly Type concreteControllerType;
    private readonly Type playerType;
    private readonly FieldInfo lockStateField;
    private readonly FieldInfo playerField;
    private readonly FieldInfo playerIdField;
    private readonly MethodInfo lockPlayerInput;
    private readonly MethodInfo isPlayerInputLocked;
    private readonly MethodInfo unlockPlayerInput;

    internal ControlManagerHarness(Assembly magicka)
    {
        managerType = magicka.GetType(
            "Magicka.GameLogic.Controls.ControlManager",
            true);
        controllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.Controller",
            true);
        concreteControllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.XInputController",
            true);
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        lockStateField = RuntimeReflection.RequireField(managerType, "mPlayerInputLocked");
        playerField = RuntimeReflection.RequireField(controllerType, "mPlayer");
        playerIdField = RuntimeReflection.RequireField(playerType, "mID");
        lockPlayerInput = FindControllerOverload("LockPlayerInput");
        isPlayerInputLocked = FindControllerOverload("IsPlayerInputLocked");
        unlockPlayerInput = FindControllerOverload("UnlockPlayerInput");
    }

    internal ScenarioResult InvalidController(bool missingController)
    {
        object manager = CreateManager();
        object controller = missingController ? null : NewUninitialized(concreteControllerType);
        string lockFailure = InvokeVoid(lockPlayerInput, manager, controller);
        bool locked;
        string queryFailure = InvokeBool(
            isPlayerInputLocked,
            manager,
            controller,
            out locked);
        string unlockFailure = InvokeVoid(unlockPlayerInput, manager, controller);
        string actual = "lock:" + lockFailure + ",query:" + queryFailure +
            ",value:" + locked + ",unlock:" + unlockFailure;
        const string expected = "lock:none,query:none,value:False,unlock:none";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult ValidController()
    {
        object manager = CreateManager();
        object controller = NewUninitialized(concreteControllerType);
        object player = NewUninitialized(playerType);
        playerIdField.SetValue(player, 2);
        playerField.SetValue(controller, player);

        string lockFailure = InvokeVoid(lockPlayerInput, manager, controller);
        bool locked;
        string lockedFailure = InvokeBool(
            isPlayerInputLocked,
            manager,
            controller,
            out locked);
        string unlockFailure = InvokeVoid(unlockPlayerInput, manager, controller);
        bool unlockedValue;
        string unlockedFailure = InvokeBool(
            isPlayerInputLocked,
            manager,
            controller,
            out unlockedValue);
        string actual = "lock:" + lockFailure + ",locked:" + lockedFailure +
            "/" + locked + ",unlock:" + unlockFailure + ",unlocked:" +
            unlockedFailure + "/" + unlockedValue;
        const string expected =
            "lock:none,locked:none/True,unlock:none,unlocked:none/False";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private object CreateManager()
    {
        object manager = NewUninitialized(managerType);
        lockStateField.SetValue(manager, new bool[4]);
        return manager;
    }

    private MethodInfo FindControllerOverload(string name)
    {
        MethodInfo method = managerType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { controllerType },
            null);
        if (method == null)
            throw new MissingMethodException(managerType.FullName, name);
        return method;
    }

    private static string InvokeVoid(MethodInfo method, object target, object argument)
    {
        try
        {
            method.Invoke(target, new object[] { argument });
            return "none";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return inner.GetType().FullName;
        }
    }

    private static string InvokeBool(
        MethodInfo method,
        object target,
        object argument,
        out bool value)
    {
        value = false;
        try
        {
            value = (bool)method.Invoke(target, new object[] { argument });
            return "none";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return inner.GetType().FullName;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
