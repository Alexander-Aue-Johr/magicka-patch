using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PlayerTextBoxCleanupScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        PlayerTextBoxCleanupHarness harness =
            new PlayerTextBoxCleanupHarness(magicka);
        report.Add(
            "player_text_box.active_release",
            harness.ActiveRelease());
        report.Add(
            "player_text_box.empty_release",
            harness.EmptyRelease());
        report.Add(
            "player_text_box.missing_text_box",
            harness.MissingTextBox());
    }
}

internal sealed class PlayerTextBoxCleanupHarness
{
    private readonly Type playerType;
    private readonly Type textBoxType;
    private readonly Type avatarType;
    private readonly Type entityType;
    private readonly FieldInfo obtainedTextBoxField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo sceneField;
    private readonly FieldInfo automaticAdvanceField;
    private readonly FieldInfo timeToLiveField;
    private readonly FieldInfo growField;
    private readonly FieldInfo scaleField;
    private readonly MethodInfo deinitializeGame;

    internal PlayerTextBoxCleanupHarness(Assembly magicka)
    {
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        textBoxType = magicka.GetType("Magicka.Graphics.TextBox", true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);

        obtainedTextBoxField = RequireField(
            playerType,
            "mObtainedTextBox",
            textBoxType);
        ownerField = RequireField(textBoxType, "mOwner", entityType);
        sceneField = RequireField(textBoxType, "mScene", null);
        automaticAdvanceField = RequireField(
            textBoxType,
            "mAutomaticAdvance",
            typeof(bool));
        timeToLiveField = RequireField(textBoxType, "mTTL", typeof(float));
        growField = RequireField(textBoxType, "mGrow", typeof(bool));
        scaleField = RequireField(textBoxType, "mScale", typeof(float));

        deinitializeGame = playerType.GetMethod(
            "DeinitializeGame",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (deinitializeGame == null || deinitializeGame.ReturnType != typeof(void))
            throw new MissingMethodException(playerType.FullName, "DeinitializeGame");
    }

    internal ScenarioResult ActiveRelease()
    {
        object player = NewUninitialized(playerType);
        object textBox = NewUninitialized(textBoxType);
        ownerField.SetValue(textBox, NewUninitialized(avatarType));
        sceneField.SetValue(textBox, NewUninitialized(sceneField.FieldType));
        automaticAdvanceField.SetValue(textBox, true);
        timeToLiveField.SetValue(textBox, 17f);
        growField.SetValue(textBox, true);
        scaleField.SetValue(textBox, 0.75f);
        obtainedTextBoxField.SetValue(player, textBox);

        deinitializeGame.Invoke(player, null);

        bool referencesReleased = ownerField.GetValue(textBox) == null &&
            sceneField.GetValue(textBox) == null;
        bool stateReset = !(bool)automaticAdvanceField.GetValue(textBox) &&
            (float)timeToLiveField.GetValue(textBox) == 0f &&
            !(bool)growField.GetValue(textBox) &&
            (float)scaleField.GetValue(textBox) == 0f;
        bool textBoxRetained = Object.ReferenceEquals(
            obtainedTextBoxField.GetValue(player),
            textBox);
        return new ScenarioResult(
            referencesReleased && stateReset && textBoxRetained,
            "references_released:" + referencesReleased +
                ",state_reset:" + stateReset +
                ",text_box_retained:" + textBoxRetained,
            "references_released:True,state_reset:True,text_box_retained:True");
    }

    internal ScenarioResult EmptyRelease()
    {
        object player = NewUninitialized(playerType);
        object textBox = NewUninitialized(textBoxType);
        obtainedTextBoxField.SetValue(player, textBox);

        deinitializeGame.Invoke(player, null);

        bool unchanged = ownerField.GetValue(textBox) == null &&
            sceneField.GetValue(textBox) == null &&
            !(bool)automaticAdvanceField.GetValue(textBox) &&
            (float)timeToLiveField.GetValue(textBox) == 0f &&
            !(bool)growField.GetValue(textBox) &&
            (float)scaleField.GetValue(textBox) == 0f;
        return new ScenarioResult(
            unchanged,
            "empty_unchanged:" + unchanged,
            "empty_unchanged:True");
    }

    internal ScenarioResult MissingTextBox()
    {
        object player = NewUninitialized(playerType);
        deinitializeGame.Invoke(player, null);
        bool stillMissing = obtainedTextBoxField.GetValue(player) == null;
        return new ScenarioResult(
            stillMissing,
            "still_missing:" + stillMissing,
            "still_missing:True");
    }

    private static FieldInfo RequireField(
        Type type,
        string name,
        Type expectedType)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null ||
            (expectedType != null && field.FieldType != expectedType))
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
