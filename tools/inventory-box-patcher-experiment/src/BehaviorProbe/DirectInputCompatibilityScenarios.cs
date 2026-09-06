using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;
using Magicka.CommunityPatch.Runtime;

internal static class DirectInputCompatibilityScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        DirectInputCompatibilityHarness harness =
            new DirectInputCompatibilityHarness(magicka);
        report.Add("direct_input.options_load_failure", harness.OpenOptions("missing"));
        report.Add("direct_input.discovery_load_failure", harness.FindControllers("load"));
        report.Add("direct_input.unrelated_failure", harness.OpenOptions("unrelated"));
        report.Add("direct_input.available", harness.FindControllers("none"));
        if (harness.WarningAvailable)
            report.Add("direct_input.warning_once", harness.ShowDeferredWarning());
        else
            report.AddNotApplicable(
                "direct_input.warning_once",
                "The in-game Paradox popup system is not present in this Magicka version");
    }
}

internal sealed class DirectInputCompatibilityHarness
{
    private readonly Assembly magicka;
    private readonly Type menuStateType;
    private readonly Type optionsType;
    private readonly Type managerType;
    private readonly Type helpType;
    private readonly Type messageBoxType;
    private readonly Type paradoxAccountType;
    private readonly Type widgetPopupSystemType;
    private readonly MethodInfo onEnter;
    private readonly MethodInfo findNewControllers;
    private readonly MethodInfo findNewGamePads;
    private readonly MethodInfo updateControllers;
    private readonly FieldInfo timerField;
    private readonly object menuState;
    private readonly object options;
    private readonly object manager;

    internal bool WarningAvailable
    {
        get
        {
            return paradoxAccountType != null && widgetPopupSystemType != null &&
                magicka.GetType("Magicka.WebTools.Paradox.ParadoxPopupUtils", false) != null;
        }
    }

    internal DirectInputCompatibilityHarness(Assembly magicka)
    {
        this.magicka = magicka;
        menuStateType = magicka.GetType("Magicka.GameLogic.GameStates.MenuState", true);
        optionsType = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.Main.Options.SubMenuOptionsControls",
            true);
        managerType = magicka.GetType("Magicka.GameLogic.Controls.ControlManager", true);
        helpType = magicka.GetType("Magicka.Graphics.GamePadMenuHelp", true);
        messageBoxType = magicka.GetType(
            "Magicka.GameLogic.UI.GamePadConfigMessageBox",
            true);
        paradoxAccountType = magicka.GetType("Magicka.WebTools.ParadoxAccount", false);
        widgetPopupSystemType = magicka.GetType(
            "Magicka.GameLogic.UI.UISystem.Popup.WidgetPopupSystem",
            false);

        onEnter = optionsType.GetMethod(
            "OnEnter",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        findNewControllers = menuStateType.GetMethod(
            "FindNewControllers",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        findNewGamePads = managerType.GetMethod(
            "FindNewGamePads",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        updateControllers = optionsType.GetMethod(
            "UpdateControllers",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        timerField = RuntimeReflection.RequireField(menuStateType, "mFindGamepadsTimer");
        if (onEnter == null || findNewControllers == null ||
            findNewGamePads == null || updateControllers == null)
            throw new MissingMethodException(
                "DirectInput compatibility test dependencies are incomplete.");

        menuState = NewUninitialized(menuStateType);
        options = NewUninitialized(optionsType);
        manager = NewUninitialized(managerType);
        InstallRecorders();
    }

    internal ScenarioResult OpenOptions(string failure)
    {
        ResetPatchState();
        DirectInputCompatibilityProbe.Failure = failure;
        DirectInputCompatibilityProbe.FindCalls = 0;
        DirectInputCompatibilityProbe.OptionsCalls = 0;

        string exception = Invoke(onEnter, options);
        string actual = "options:" + DirectInputCompatibilityProbe.OptionsCalls +
            ",failure:" + exception;
        string expected = failure == "unrelated"
            ? "options:1,failure:System.InvalidOperationException"
            : "options:1,failure:none";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult FindControllers(string failure)
    {
        ResetPatchState();
        DirectInputCompatibilityProbe.Failure = failure;
        DirectInputCompatibilityProbe.FindCalls = 0;
        DirectInputCompatibilityProbe.OptionsCalls = 0;
        timerField.SetValue(menuState, 0f);

        string exception = Invoke(findNewControllers, menuState);
        float timer = Convert.ToSingle(timerField.GetValue(menuState));
        string actual = "find:" + DirectInputCompatibilityProbe.FindCalls +
            ",options:" + DirectInputCompatibilityProbe.OptionsCalls +
            ",timer:" + timer + ",failure:" + exception;
        string expected = failure == "none"
            ? "find:1,options:1,timer:5,failure:none"
            : "find:1,options:0,timer:5,failure:none";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult ShowDeferredWarning()
    {
        ResetPatchState();
        DirectInputCompatibilityProbe.Failure = "missing";
        DirectInputCompatibilityProbe.PopupCalls = 0;
        DirectInputCompatibilityProbe.PopupTitle = null;
        DirectInputCompatibilityProbe.PopupMessage = null;

        Invoke(onEnter, options);
        MethodInfo warning = FindWarningMethod();
        if (warning != null)
        {
            warning.Invoke(null, null);
            warning.Invoke(null, null);
        }

        string actual = "popups:" + DirectInputCompatibilityProbe.PopupCalls +
            ",title:" + (DirectInputCompatibilityProbe.PopupTitle ?? "<null>") +
            ",mentions-installer:" +
            ContainsInstallerAdvice(DirectInputCompatibilityProbe.PopupMessage);
        const string expected =
            "popups:1,title:Controller support unavailable,mentions-installer:True";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private void InstallRecorders()
    {
        DirectInputCompatibilityProbe.Manager = manager;
        DirectInputCompatibilityProbe.Options = options;
        DirectInputCompatibilityProbe.Help = NewUninitialized(helpType);
        DirectInputCompatibilityProbe.MessageBox = NewUninitialized(messageBoxType);
        if (paradoxAccountType != null)
        {
            DirectInputCompatibilityProbe.ParadoxAccount =
                NewUninitialized(paradoxAccountType);
            SetSingletonInstance(
                paradoxAccountType,
                DirectInputCompatibilityProbe.ParadoxAccount);
        }
        if (widgetPopupSystemType != null)
        {
            DirectInputCompatibilityProbe.WidgetPopup =
                NewUninitialized(widgetPopupSystemType);
            SetSingletonInstance(
                widgetPopupSystemType,
                DirectInputCompatibilityProbe.WidgetPopup);
        }

        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-direct-input");
        harmony.Patch(
            findNewGamePads,
            new HarmonyMethod(
                typeof(DirectInputCompatibilityProbe).GetMethod("FindPrefix")),
            null,
            null);
        harmony.Patch(
            updateControllers,
            new HarmonyMethod(
                typeof(DirectInputCompatibilityProbe).GetMethod("OptionsPrefix")),
            null,
            null);
        DirectInputCompatibilityProbe.OptionsMethod = updateControllers;
        harmony.Patch(
            onEnter,
            null,
            null,
            new HarmonyMethod(
                typeof(DirectInputCompatibilityProbe)
                    .GetMethod("OnEnterTranspiler")));

        PatchSingletonGetter(harmony, managerType, "ManagerPrefix");
        PatchSingletonGetter(harmony, optionsType, "OptionsInstancePrefix");
        PatchSingletonGetter(harmony, helpType, "HelpPrefix");
        PatchSingletonGetter(harmony, messageBoxType, "MessageBoxPrefix");

        PropertyInfo dInputPads = managerType.GetProperty(
            "DInputPads",
            BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo dead = messageBoxType.GetProperty(
            "Dead",
            BindingFlags.Instance | BindingFlags.Public);
        if (dInputPads == null || dead == null)
            throw new MissingMemberException(
                "DirectInput collection test dependencies are incomplete.");
        DirectInputCompatibilityProbe.DInputPads =
            Activator.CreateInstance(dInputPads.PropertyType);
        harmony.Patch(
            dInputPads.GetGetMethod(),
            new HarmonyMethod(
                typeof(DirectInputCompatibilityProbe)
                    .GetMethod("DInputPadsPrefix")
                    .MakeGenericMethod(new Type[] { dInputPads.PropertyType })),
            null,
            null);
        harmony.Patch(
            dead.GetGetMethod(),
            new HarmonyMethod(
                typeof(DirectInputCompatibilityProbe).GetMethod("DeadPrefix")),
            null,
            null);

        PatchHelpMethods(harmony, "ActivateButton");
        PatchHelpMethods(harmony, "DeactivateButton");
        if (WarningAvailable)
            PatchWarningDependencies(harmony);
    }

    private static void PatchSingletonGetter(
        HarmonyInstance harmony,
        Type concreteType,
        string prefixName)
    {
        PropertyInfo instance = null;
        for (Type current = concreteType; current != null && instance == null;
            current = current.BaseType)
        {
            instance = current.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
        }
        if (instance == null)
            throw new MissingMemberException(concreteType.FullName, "Instance");
        MethodInfo prefix = typeof(DirectInputCompatibilityProbe)
            .GetMethod(prefixName)
            .MakeGenericMethod(new Type[] { concreteType });
        harmony.Patch(
            instance.GetGetMethod(),
            new HarmonyMethod(prefix),
            null,
            null);
    }

    private void PatchHelpMethods(HarmonyInstance harmony, string name)
    {
        MethodInfo[] methods = helpType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        int patched = 0;
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != name)
                continue;
            harmony.Patch(
                methods[index],
                new HarmonyMethod(
                    typeof(DirectInputCompatibilityProbe).GetMethod("HelpMethodPrefix")),
                null,
                null);
            patched++;
        }
        if (patched == 0)
            throw new MissingMethodException(helpType.FullName, name);
    }

    private void PatchWarningDependencies(HarmonyInstance harmony)
    {
        PropertyInfo pendingError = paradoxAccountType.GetProperty(
            "PendingErrorCode",
            BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo active = widgetPopupSystemType.GetProperty(
            "Active",
            BindingFlags.Instance | BindingFlags.Public);
        if (pendingError == null || active == null)
            throw new MissingMemberException(
                "DirectInput warning state dependencies are incomplete.");
        harmony.Patch(
            pendingError.GetGetMethod(),
            new HarmonyMethod(
                typeof(DirectInputCompatibilityProbe)
                    .GetMethod("PendingErrorPrefix")
                    .MakeGenericMethod(new Type[] { pendingError.PropertyType })),
            null,
            null);
        harmony.Patch(
            active.GetGetMethod(),
            new HarmonyMethod(
                typeof(DirectInputCompatibilityProbe).GetMethod("ActivePrefix")),
            null,
            null);

        Type popupUtils = magicka.GetType(
            "Magicka.WebTools.Paradox.ParadoxPopupUtils",
            true);
        MethodInfo show = popupUtils.GetMethod(
            "ShowErrorPopup",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new Type[] { typeof(string), typeof(string) },
            null);
        if (show == null)
            throw new MissingMethodException(popupUtils.FullName, "ShowErrorPopup");
        DirectInputCompatibilityProbe.PopupMethod = show;

        Type manual = magicka.GetType(
            "Magicka.CommunityPatch.RuntimeCompatibilityGuards",
            false);
        if (manual != null)
        {
            MethodInfo warning = manual.GetMethod(
                "ShowPendingDirectInputWarning",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            harmony.Patch(
                warning,
                null,
                null,
                new HarmonyMethod(
                    typeof(DirectInputCompatibilityProbe)
                        .GetMethod("WarningTranspiler")));
        }

        Type runtime = typeof(Bootstrap).Assembly.GetType(
            "Magicka.CommunityPatch.Runtime.DirectInputCompatibilityPatch",
            false);
        if (runtime != null)
        {
            FieldInfo showWarning = runtime.GetField(
                "showErrorPopup",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (showWarning != null)
            {
                Delegate recorder = Delegate.CreateDelegate(
                    showWarning.FieldType,
                    typeof(DirectInputCompatibilityProbe).GetMethod("RecordPopup"));
                showWarning.SetValue(null, recorder);
            }
        }
    }

    private static void SetSingletonInstance(Type concreteType, object value)
    {
        for (Type current = concreteType.BaseType; current != null;
            current = current.BaseType)
        {
            FieldInfo instance = current.GetField(
                "sInstance",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (instance == null)
                continue;
            instance.SetValue(null, value);
            return;
        }
        throw new MissingFieldException(concreteType.FullName, "sInstance");
    }

    private MethodInfo FindWarningMethod()
    {
        Type manual = magicka.GetType(
            "Magicka.CommunityPatch.RuntimeCompatibilityGuards",
            false);
        if (manual != null)
            return manual.GetMethod(
                "ShowPendingDirectInputWarning",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Type runtime = typeof(Bootstrap).Assembly.GetType(
            "Magicka.CommunityPatch.Runtime.DirectInputCompatibilityPatch",
            false);
        return runtime == null
            ? null
            : runtime.GetMethod(
                "ShowPendingDirectInputWarning",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private void ResetPatchState()
    {
        ResetStateFields(magicka.GetType(
            "Magicka.CommunityPatch.RuntimeCompatibilityGuards",
            false));
        ResetStateFields(typeof(Bootstrap).Assembly.GetType(
            "Magicka.CommunityPatch.Runtime.DirectInputCompatibilityPatch",
            false));
    }

    private static void ResetStateFields(Type helper)
    {
        if (helper == null)
            return;
        FieldInfo unavailable = helper.GetField(
            "sDirectInputUnavailable",
            BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo pending = helper.GetField(
            "sDirectInputWarningPending",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (unavailable != null)
            unavailable.SetValue(null, 0);
        if (pending != null)
            pending.SetValue(null, 0);
    }

    private static string Invoke(MethodInfo method, object instance)
    {
        try
        {
            method.Invoke(instance, null);
            return "none";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return inner.GetType().FullName;
        }
    }

    private static bool ContainsInstallerAdvice(string message)
    {
        return message != null &&
            message.IndexOf("DXSETUP.exe", StringComparison.Ordinal) >= 0 &&
            message.IndexOf("Start Game", StringComparison.Ordinal) >= 0;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class DirectInputCompatibilityProbe
{
    public static string Failure;
    public static int FindCalls;
    public static int OptionsCalls;
    public static int PopupCalls;
    public static string PopupTitle;
    public static string PopupMessage;
    public static object DInputPads;
    public static object Manager;
    public static object Options;
    public static object Help;
    public static object MessageBox;
    public static object ParadoxAccount;
    public static object WidgetPopup;

    public static bool FindPrefix()
    {
        FindCalls++;
        ThrowConfiguredFailure();
        return false;
    }

    public static bool OptionsPrefix()
    {
        OptionsCalls++;
        ThrowConfiguredFailure();
        return false;
    }

    public static bool ManagerPrefix<T>(ref T __result)
    {
        __result = (T)Manager;
        return false;
    }

    public static bool OptionsInstancePrefix<T>(ref T __result)
    {
        __result = (T)Options;
        return false;
    }

    public static bool HelpPrefix<T>(ref T __result)
    {
        __result = (T)Help;
        return false;
    }

    public static bool MessageBoxPrefix<T>(ref T __result)
    {
        __result = (T)MessageBox;
        return false;
    }

    public static bool DInputPadsPrefix<T>(ref T __result)
    {
        __result = (T)DInputPads;
        return false;
    }

    public static bool DeadPrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static bool HelpMethodPrefix()
    {
        return false;
    }

    public static bool PendingErrorPrefix<T>(ref T __result)
    {
        __result = default(T);
        return false;
    }

    public static bool ActivePrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static MethodInfo PopupMethod;
    public static MethodInfo OptionsMethod;

    public static IEnumerable<CodeInstruction> OnEnterTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = new List<CodeInstruction>();
        foreach (CodeInstruction instruction in instructions)
        {
            result.Add(instruction);
            MethodBase called = instruction.operand as MethodBase;
            if (Object.Equals(called, OptionsMethod) ||
                (called != null &&
                    (called.Name == "UpdateControllerOptions" ||
                        called.Name == "DirectInput_UpdateControllerOptions")))
            {
                result.Add(new CodeInstruction(OpCodes.Ret));
                return result;
            }
        }
        throw new InvalidOperationException(
            "SubMenuOptionsControls.OnEnter controller refresh call was not found.");
    }

    public static IEnumerable<CodeInstruction> WarningTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call ||
                    instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, PopupMethod))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = typeof(DirectInputCompatibilityProbe)
                    .GetMethod("RecordPopup");
            }
            yield return instruction;
        }
    }

    public static void RecordPopup(string iLocTitle, string iLocMessage)
    {
        PopupCalls++;
        PopupTitle = iLocTitle;
        PopupMessage = iLocMessage;
    }

    private static void ThrowConfiguredFailure()
    {
        if (Failure == "missing")
            throw new System.IO.FileNotFoundException("Managed DirectInput missing");
        if (Failure == "load")
            throw new System.IO.FileLoadException("Managed DirectInput failed to load");
        if (Failure == "unrelated")
            throw new InvalidOperationException("Unrelated controller failure");
    }
}
