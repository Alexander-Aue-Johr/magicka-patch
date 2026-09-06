using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PlayerNotifierCleanupScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        PlayerNotifierCleanupHarness harness =
            new PlayerNotifierCleanupHarness(magicka);
        report.Add(
            "player_notifier.active_release",
            harness.ActiveRelease());
        report.Add(
            "player_notifier.empty_release",
            harness.EmptyRelease());
        report.Add(
            "player_notifier.missing_notifier",
            harness.MissingNotifier());
    }
}

internal sealed class PlayerNotifierCleanupHarness
{
    private readonly Type playerType;
    private readonly Type notifierType;
    private readonly Type avatarType;
    private readonly FieldInfo notifierField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo dialogAttachField;
    private readonly FieldInfo alphaField;
    private readonly FieldInfo targetAlphaField;
    private readonly MethodInfo deinitializeGame;

    internal PlayerNotifierCleanupHarness(Assembly magicka)
    {
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        notifierType = magicka.GetType(
            "Magicka.Graphics.NotifierButton",
            true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        Type entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        Type textBoxType = magicka.GetType("Magicka.Graphics.TextBox", true);

        notifierField = RequireField(
            playerType,
            "mNotifierButton",
            notifierType);
        ownerField = RequireField(notifierType, "mOwner", entityType);
        dialogAttachField = RequireField(
            notifierType,
            "mDialogAttach",
            textBoxType);
        alphaField = RequireField(notifierType, "mAlpha", typeof(float));
        targetAlphaField = RequireField(
            notifierType,
            "mTargetAlpha",
            typeof(float));

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
        object notifier = NewUninitialized(notifierType);
        ownerField.SetValue(notifier, NewUninitialized(avatarType));
        dialogAttachField.SetValue(
            notifier,
            NewUninitialized(dialogAttachField.FieldType));
        alphaField.SetValue(notifier, 0.75f);
        targetAlphaField.SetValue(notifier, 1f);
        notifierField.SetValue(player, notifier);

        deinitializeGame.Invoke(player, null);

        bool referencesReleased = ownerField.GetValue(notifier) == null &&
            dialogAttachField.GetValue(notifier) == null;
        bool stateReset = (float)alphaField.GetValue(notifier) == 0f &&
            (float)targetAlphaField.GetValue(notifier) == 0f;
        bool notifierRetained = Object.ReferenceEquals(
            notifierField.GetValue(player),
            notifier);
        return new ScenarioResult(
            referencesReleased && stateReset && notifierRetained,
            "references_released:" + referencesReleased +
                ",state_reset:" + stateReset +
                ",notifier_retained:" + notifierRetained,
            "references_released:True,state_reset:True,notifier_retained:True");
    }

    internal ScenarioResult EmptyRelease()
    {
        object player = NewUninitialized(playerType);
        object notifier = NewUninitialized(notifierType);
        notifierField.SetValue(player, notifier);

        deinitializeGame.Invoke(player, null);

        bool unchanged = ownerField.GetValue(notifier) == null &&
            dialogAttachField.GetValue(notifier) == null &&
            (float)alphaField.GetValue(notifier) == 0f &&
            (float)targetAlphaField.GetValue(notifier) == 0f;
        return new ScenarioResult(
            unchanged,
            "empty_unchanged:" + unchanged,
            "empty_unchanged:True");
    }

    internal ScenarioResult MissingNotifier()
    {
        object player = NewUninitialized(playerType);
        deinitializeGame.Invoke(player, null);
        bool stillMissing = notifierField.GetValue(player) == null;
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
        if (field == null || field.FieldType != expectedType)
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
