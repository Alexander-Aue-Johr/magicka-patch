using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class SubMenuMainScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        Type subMenuType = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuMain",
            false);
        Type controllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.Controller",
            false);
        if (subMenuType == null || controllerType == null ||
            subMenuType.GetMethod(
                "ControllerB",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { controllerType },
                null) == null)
        {
            const string reason =
                "SubMenuMain.ControllerB is not declared in this Magicka version";
            report.AddNotApplicable("sub_menu_main.gamepad_back", reason);
            report.AddNotApplicable("sub_menu_main.keyboard_back", reason);
            return;
        }

        SubMenuMainHarness harness = new SubMenuMainHarness(magicka);
        report.Add("sub_menu_main.gamepad_back", harness.PressBack(false));
        report.Add("sub_menu_main.keyboard_back", harness.PressBack(true));
    }
}

internal sealed class SubMenuMainHarness
{
    private readonly Type keyboardControllerType;
    private readonly Type gamepadControllerType;
    private readonly Type subMenuType;
    private readonly FieldInfo cursorField;
    private readonly FieldInfo menuField;
    private readonly MethodInfo controllerB;

    internal SubMenuMainHarness(Assembly magicka)
    {
        keyboardControllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.KeyboardMouseController",
            true);
        gamepadControllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.XInputController",
            true);
        subMenuType = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuMain",
            true);
        Type controllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.Controller",
            true);
        cursorField = RuntimeReflection.RequireField(subMenuType, "mCursor");
        menuField = RuntimeReflection.RequireField(subMenuType, "mMenu");
        controllerB = subMenuType.GetMethod(
            "ControllerB",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { controllerType },
            null);
        MethodInfo showDialog = subMenuType.GetMethod(
            "ShowRUSure",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        MethodInfo attached = cursorField.FieldType.GetProperty(
            "Attached",
            BindingFlags.Instance | BindingFlags.Public).GetGetMethod();
        MethodInfo attach = FindAttach(cursorField.FieldType);
        if (controllerB == null || showDialog == null || attached == null || attach == null)
            throw new MissingMethodException("SubMenuMain test dependencies are incomplete.");

        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-sub-menu-main");
        if (keyboardControllerType.TypeInitializer != null)
        {
            harmony.Patch(
                keyboardControllerType.TypeInitializer,
                new HarmonyMethod(
                    typeof(SubMenuMainProbe).GetMethod("StaticConstructorPrefix")),
                null,
                null);
        }
        harmony.Patch(
            showDialog,
            new HarmonyMethod(typeof(SubMenuMainProbe).GetMethod("ShowDialogPrefix")),
            null,
            null);
        harmony.Patch(
            attached,
            new HarmonyMethod(typeof(SubMenuMainProbe).GetMethod("AttachedPrefix")),
            null,
            null);
        harmony.Patch(
            attach,
            new HarmonyMethod(typeof(SubMenuMainProbe).GetMethod("AttachPrefix")),
            null,
            null);
    }

    internal ScenarioResult PressBack(bool keyboard)
    {
        object menu = NewUninitialized(subMenuType);
        object cursor = NewUninitialized(cursorField.FieldType);
        object controller = NewUninitialized(
            keyboard ? keyboardControllerType : gamepadControllerType);
        cursorField.SetValue(menu, cursor);
        menuField.SetValue(menu, null);
        SubMenuMainProbe.DialogCalls = 0;
        SubMenuMainProbe.AttachCalls = 0;

        string failure = "none";
        try
        {
            controllerB.Invoke(menu, new object[] { controller });
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            failure = inner.GetType().FullName;
        }

        string actual = "dialog:" + SubMenuMainProbe.DialogCalls +
            ",attach:" + SubMenuMainProbe.AttachCalls + ",failure:" + failure;
        string expected = keyboard
            ? "dialog:0,attach:1,failure:none"
            : "dialog:1,attach:0,failure:none";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static MethodInfo FindAttach(Type cursorType)
    {
        MethodInfo match = null;
        MethodInfo[] methods = cursorType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "Attach" ||
                methods[index].GetParameters().Length != 2)
                continue;
            if (match != null)
                throw new AmbiguousMatchException(cursorType.FullName + ".Attach");
            match = methods[index];
        }
        return match;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class SubMenuMainProbe
{
    public static int DialogCalls;
    public static int AttachCalls;

    public static bool StaticConstructorPrefix()
    {
        return false;
    }

    public static bool ShowDialogPrefix()
    {
        DialogCalls++;
        return false;
    }

    public static bool AttachedPrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static bool AttachPrefix()
    {
        AttachCalls++;
        return false;
    }
}
