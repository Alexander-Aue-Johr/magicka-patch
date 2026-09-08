using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class DialogManagerLevelCleanupScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        DialogManagerLevelCleanupHarness harness =
            new DialogManagerLevelCleanupHarness(
                magicka,
                runtimePatchEnabled);
        try
        {
            report.Add(
                "dialog_manager.level_reference_release",
                harness.LevelReferenceRelease());
            report.Add(
                "dialog_manager.empty_additional_list",
                harness.EmptyAdditionalList());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class DialogManagerLevelCleanupHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-dialog-manager-cleanup";

    private readonly bool runtimePatchEnabled;
    private readonly Type managerType;
    private readonly Type textBoxType;
    private readonly Type cutsceneTextType;
    private readonly Type avatarType;
    private readonly FieldInfo textBoxesField;
    private readonly FieldInfo cutsceneTextField;
    private readonly FieldInfo additionalTextBoxesField;
    private readonly FieldInfo dialogsField;
    private readonly FieldInfo drawCutsceneTextField;
    private readonly FieldInfo cutsceneInteractField;
    private readonly FieldInfo drawSubtitlesField;
    private readonly FieldInfo subtitleHeightField;
    private readonly FieldInfo awaitingInputField;
    private readonly FieldInfo holdoffInputTimerField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo sceneField;
    private readonly FieldInfo automaticAdvanceField;
    private readonly FieldInfo timeToLiveField;
    private readonly FieldInfo growField;
    private readonly FieldInfo scaleField;
    private readonly MethodInfo setDialogs;
    private readonly MethodInfo manualReset;
    private readonly MethodInfo runtimeReset;
    private readonly HarmonyInstance harmony;

    internal DialogManagerLevelCleanupHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        managerType = magicka.GetType(
            "Magicka.GameLogic.UI.DialogManager",
            true);
        textBoxType = magicka.GetType("Magicka.Graphics.TextBox", true);
        cutsceneTextType = magicka.GetType(
            "Magicka.Graphics.CutsceneText",
            true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);

        textBoxesField = RequireField(managerType, "mTextBoxes");
        cutsceneTextField = RequireField(managerType, "mCutsceneText");
        additionalTextBoxesField = RequireField(
            managerType,
            "mAdditionalTextBoxes");
        dialogsField = RequireField(managerType, "mDialogs");
        drawCutsceneTextField = RequireField(
            managerType,
            "mDrawCutsceneText");
        cutsceneInteractField = RequireField(
            managerType,
            "mCutsceneInteract");
        drawSubtitlesField = RequireField(managerType, "mDrawSubtitles");
        subtitleHeightField = RequireField(managerType, "mSubtitleHeight");
        awaitingInputField = RequireField(managerType, "mAwaitingInput");
        holdoffInputTimerField = RequireField(
            managerType,
            "mHoldoffInputTimer");

        ownerField = RequireField(textBoxType, "mOwner");
        sceneField = RequireField(textBoxType, "mScene");
        automaticAdvanceField = RequireField(
            textBoxType,
            "mAutomaticAdvance");
        timeToLiveField = RequireField(textBoxType, "mTTL");
        growField = RequireField(textBoxType, "mGrow");
        scaleField = RequireField(textBoxType, "mScale");

        setDialogs = FindSetDialogs();
        manualReset = managerType.GetMethod(
            "ResetForLevelUnload",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        runtimeReset = typeof(
            Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.DialogManagerLevelCleanupPatch",
                false).GetMethod(
                    "ResetForLevelUnload",
                    BindingFlags.Static | BindingFlags.Public);

        MethodInfo endAll = managerType.GetMethod(
            "EndAll",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (endAll == null)
            throw new MissingMethodException(managerType.FullName, "EndAll");
        harmony = HarmonyInstance.Create(HarmonyOwner);
        harmony.Patch(
            endAll,
            new HarmonyMethod(
                typeof(DialogManagerLevelCleanupProbe).GetMethod(
                    "EndAllPrefix")),
            null,
            null);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult LevelReferenceRelease()
    {
        object manager = CreateManager(1);
        object additionalTextBox =
            ((IList)additionalTextBoxesField.GetValue(manager))[0];
        Exception failure = InvokeReset(manager);
        int released = CountReleased(manager) +
            (IsReleased(additionalTextBox) ? 1 : 0);
        IList additional = (IList)additionalTextBoxesField.GetValue(manager);
        bool managerReset = dialogsField.GetValue(manager) == null &&
            !(bool)drawCutsceneTextField.GetValue(manager) &&
            (int)cutsceneInteractField.GetValue(manager) == 0 &&
            !(bool)drawSubtitlesField.GetValue(manager) &&
            (float)subtitleHeightField.GetValue(manager) == 0f &&
            !(bool)awaitingInputField.GetValue(manager) &&
            (float)holdoffInputTimerField.GetValue(manager) == 0f;
        bool passed = failure == null && released == 10 &&
            additional.Count == 0 && managerReset &&
            DialogManagerLevelCleanupProbe.EndAllCalls == 1;
        string actual = "exception:" + ExceptionName(failure) +
            ",released:" + released + "/10,additional:" +
            additional.Count + ",manager_reset:" + managerReset +
            ",end_all:" + DialogManagerLevelCleanupProbe.EndAllCalls;
        return new ScenarioResult(
            passed,
            actual,
            "exception:none,released:10/10,additional:0," +
                "manager_reset:True,end_all:1");
    }

    internal ScenarioResult EmptyAdditionalList()
    {
        object manager = CreateManager(0);
        Exception failure = InvokeReset(manager);
        IList additional = (IList)additionalTextBoxesField.GetValue(manager);
        bool passed = failure == null && additional.Count == 0 &&
            CountReleased(manager) == 9 &&
            DialogManagerLevelCleanupProbe.EndAllCalls == 1;
        return new ScenarioResult(
            passed,
            "exception:" + ExceptionName(failure) +
                ",released:" + CountReleased(manager) +
                "/9,additional:" + additional.Count +
                ",end_all:" + DialogManagerLevelCleanupProbe.EndAllCalls,
            "exception:none,released:9/9,additional:0,end_all:1");
    }

    private object CreateManager(int additionalCount)
    {
        object manager = NewUninitialized(managerType);
        Array textBoxes = Array.CreateInstance(textBoxType, 8);
        for (int index = 0; index < textBoxes.Length; index++)
            textBoxes.SetValue(CreateActiveTextBox(textBoxType), index);
        textBoxesField.SetValue(manager, textBoxes);
        cutsceneTextField.SetValue(
            manager,
            CreateActiveTextBox(cutsceneTextType));

        IList additional = (IList)Activator.CreateInstance(
            additionalTextBoxesField.FieldType);
        for (int index = 0; index < additionalCount; index++)
            additional.Add(CreateActiveTextBox(textBoxType));
        additional.Add(null);
        if (additionalCount == 0)
            additional.Clear();
        additionalTextBoxesField.SetValue(manager, additional);

        dialogsField.SetValue(
            manager,
            NewUninitialized(dialogsField.FieldType));
        drawCutsceneTextField.SetValue(manager, true);
        cutsceneInteractField.SetValue(manager, 7);
        drawSubtitlesField.SetValue(manager, true);
        subtitleHeightField.SetValue(manager, 42f);
        awaitingInputField.SetValue(manager, true);
        holdoffInputTimerField.SetValue(manager, 3f);
        return manager;
    }

    private object CreateActiveTextBox(Type type)
    {
        object textBox = NewUninitialized(type);
        ownerField.SetValue(textBox, NewUninitialized(avatarType));
        sceneField.SetValue(
            textBox,
            NewUninitialized(sceneField.FieldType));
        automaticAdvanceField.SetValue(textBox, true);
        timeToLiveField.SetValue(textBox, 8f);
        growField.SetValue(textBox, true);
        scaleField.SetValue(textBox, 1f);
        return textBox;
    }

    private int CountReleased(object manager)
    {
        int result = 0;
        Array textBoxes = (Array)textBoxesField.GetValue(manager);
        for (int index = 0; index < textBoxes.Length; index++)
        {
            if (IsReleased(textBoxes.GetValue(index)))
                result++;
        }
        if (IsReleased(cutsceneTextField.GetValue(manager)))
            result++;
        IList additional = (IList)additionalTextBoxesField.GetValue(manager);
        for (int index = 0; index < additional.Count; index++)
        {
            if (additional[index] != null && IsReleased(additional[index]))
                result++;
        }
        return result;
    }

    private bool IsReleased(object textBox)
    {
        return ownerField.GetValue(textBox) == null &&
            sceneField.GetValue(textBox) == null &&
            !(bool)automaticAdvanceField.GetValue(textBox) &&
            (float)timeToLiveField.GetValue(textBox) == 0f &&
            !(bool)growField.GetValue(textBox) &&
            (float)scaleField.GetValue(textBox) == 0f;
    }

    private Exception InvokeReset(object manager)
    {
        DialogManagerLevelCleanupProbe.EndAllCalls = 0;
        if (manualReset != null)
            return Invoke(manualReset, manager, new object[0]);
        if (runtimePatchEnabled)
            return Invoke(runtimeReset, null, new object[] { manager, null });
        return Invoke(setDialogs, manager, new object[] { null });
    }

    private MethodInfo FindSetDialogs()
    {
        MethodInfo[] methods = managerType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "SetDialogs" &&
                methods[index].GetParameters().Length == 1)
                return methods[index];
        }
        throw new MissingMethodException(managerType.FullName, "SetDialogs");
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static Exception Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException ?? exception;
        }
    }

    private static string ExceptionName(Exception exception)
    {
        return exception == null ? "none" : exception.GetType().FullName;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class DialogManagerLevelCleanupProbe
{
    public static int EndAllCalls;

    public static bool EndAllPrefix()
    {
        EndAllCalls++;
        return false;
    }
}
