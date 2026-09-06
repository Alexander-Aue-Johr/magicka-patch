using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PlayerControllerAvatarScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        PlayerControllerAvatarHarness harness =
            new PlayerControllerAvatarHarness(magicka);
        report.Add(
            "player_controller_avatar.matching_release",
            harness.MatchingRelease());
        report.Add(
            "player_controller_avatar.replacement_retained",
            harness.ReplacementRetained());
        report.Add(
            "player_controller_avatar.non_null_assignment",
            harness.NonNullAssignment());
    }
}

internal sealed class PlayerControllerAvatarHarness
{
    private readonly Type playerType;
    private readonly Type avatarType;
    private readonly Type controllerType;
    private readonly FieldInfo playerAvatarField;
    private readonly FieldInfo playerControllerField;
    private readonly FieldInfo controllerAvatarField;
    private readonly MethodInfo avatarSetter;

    internal PlayerControllerAvatarHarness(Assembly magicka)
    {
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        controllerType = magicka.GetType(
            "Magicka.GameLogic.Controls.XInputController",
            true);
        Type controllerBaseType = magicka.GetType(
            "Magicka.GameLogic.Controls.Controller",
            true);

        playerAvatarField = RequireField(playerType, "mAvatar");
        playerControllerField = RequireField(
            playerType,
            "<Controller>k__BackingField");
        controllerAvatarField = RequireField(controllerBaseType, "mAvatar");
        PropertyInfo avatarProperty = playerType.GetProperty(
            "Avatar",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        avatarSetter = avatarProperty == null
            ? null
            : avatarProperty.GetSetMethod();
        if (avatarSetter == null || avatarSetter.ReturnType != typeof(void))
            throw new MissingMethodException(playerType.FullName, "set_Avatar");
    }

    internal ScenarioResult MatchingRelease()
    {
        Fixture fixture = CreateFixture();
        avatarSetter.Invoke(fixture.Player, new object[] { null });
        bool detached = controllerAvatarField.GetValue(fixture.Controller) == null;
        bool weakCleared = ((WeakReference)playerAvatarField.GetValue(
            fixture.Player)).Target == null;
        return new ScenarioResult(
            detached && weakCleared,
            "detached:" + detached + ",weak_cleared:" + weakCleared,
            "detached:True,weak_cleared:True");
    }

    internal ScenarioResult ReplacementRetained()
    {
        Fixture fixture = CreateFixture();
        object replacement = NewUninitialized(avatarType);
        controllerAvatarField.SetValue(fixture.Controller, replacement);
        avatarSetter.Invoke(fixture.Player, new object[] { null });
        bool retained = Object.ReferenceEquals(
            controllerAvatarField.GetValue(fixture.Controller),
            replacement);
        return new ScenarioResult(
            retained,
            "replacement_retained:" + retained,
            "replacement_retained:True");
    }

    internal ScenarioResult NonNullAssignment()
    {
        Fixture fixture = CreateFixture();
        object replacement = NewUninitialized(avatarType);
        avatarSetter.Invoke(fixture.Player, new object[] { replacement });
        bool controllerRetained = Object.ReferenceEquals(
            controllerAvatarField.GetValue(fixture.Controller),
            fixture.OldAvatar);
        bool weakUpdated = Object.ReferenceEquals(
            ((WeakReference)playerAvatarField.GetValue(fixture.Player)).Target,
            replacement);
        return new ScenarioResult(
            controllerRetained && weakUpdated,
            "controller_retained:" + controllerRetained +
                ",weak_updated:" + weakUpdated,
            "controller_retained:True,weak_updated:True");
    }

    private Fixture CreateFixture()
    {
        object player = NewUninitialized(playerType);
        object controller = NewUninitialized(controllerType);
        object avatar = NewUninitialized(avatarType);
        playerAvatarField.SetValue(player, new WeakReference(avatar));
        playerControllerField.SetValue(player, controller);
        controllerAvatarField.SetValue(controller, avatar);
        return new Fixture(player, controller, avatar);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }

    private sealed class Fixture
    {
        internal object Player { get; private set; }
        internal object Controller { get; private set; }
        internal object OldAvatar { get; private set; }

        internal Fixture(object player, object controller, object oldAvatar)
        {
            Player = player;
            Controller = controller;
            OldAvatar = oldAvatar;
        }
    }
}
