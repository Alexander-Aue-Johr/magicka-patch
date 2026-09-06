using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class MagickCameraScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        MagickCameraHarness harness = new MagickCameraHarness(magicka);
        report.Add("camera_follow.bodyless_target", harness.BodylessFollowTarget());
        report.Add("camera_follow.missing_target", harness.MissingFollowTarget());
        report.Add("camera_follow.other_behavior", harness.BodylessTargetInOtherBehavior());
    }
}

internal sealed class MagickCameraHarness
{
    private readonly Type cameraType;
    private readonly Type characterType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type behaviorType;
    private readonly MethodInfo update;

    internal MagickCameraHarness(Assembly magicka)
    {
        cameraType = magicka.GetType("Magicka.Graphics.MagickCamera", true);
        characterType = magicka.GetType("Magicka.GameLogic.Entities.Character", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        behaviorType = magicka.GetType("Magicka.Graphics.CameraBehaviour", true);
        update = Array.Find(
            cameraType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == "Update" && method.GetParameters().Length == 2);
    }

    internal ScenarioResult BodylessFollowTarget()
    {
        return Invoke(NewCamera(NewBodylessEntity(), "FollowEntity"), false, "FollowPlayers");
    }

    internal ScenarioResult MissingFollowTarget()
    {
        return Invoke(NewCamera(null, "FollowEntity"), false, "FollowPlayers");
    }

    internal ScenarioResult BodylessTargetInOtherBehavior()
    {
        return Invoke(NewCamera(NewBodylessEntity(), "FollowPlayers"), true, "FollowPlayers");
    }

    private object NewCamera(object following, string behavior)
    {
        object camera = FormatterServices.GetUninitializedObject(cameraType);
        GC.SuppressFinalize(camera);
        RuntimeReflection.WriteField(camera, "mFollowing", following);
        RuntimeReflection.WriteField(
            camera,
            "mCurrentBehaviour",
            Enum.Parse(behaviorType, behavior));
        RuntimeReflection.WriteField(
            camera,
            "mPlayers",
            Activator.CreateInstance(
                typeof(System.Collections.Generic.List<>).MakeGenericType(characterType)));
        RuntimeReflection.WriteField(
            camera,
            "mNetworkPlayers",
            Activator.CreateInstance(
                typeof(System.Collections.Generic.List<>).MakeGenericType(characterType)));
        return camera;
    }

    private object NewBodylessEntity()
    {
        object entity = FormatterServices.GetUninitializedObject(nonPlayerCharacterType);
        GC.SuppressFinalize(entity);
        return entity;
    }

    private ScenarioResult Invoke(
        object camera,
        bool expectFollowing,
        string expectedBehavior)
    {
        try
        {
            ParameterInfo[] parameters = update.GetParameters();
            object channel = Activator.CreateInstance(parameters[0].ParameterType);
            update.Invoke(camera, new object[] { channel, 0f });
        }
        catch (TargetInvocationException)
        {
        }

        object following = RuntimeReflection.ReadField(camera, "mFollowing");
        string behavior = RuntimeReflection.ReadField(camera, "mCurrentBehaviour").ToString();
        string actual = "following:" + (following == null ? "null" : "set") +
            ",behavior:" + behavior;
        string expected = "following:" + (expectFollowing ? "set" : "null") +
            ",behavior:" + expectedBehavior;
        return new ScenarioResult(
            (following != null) == expectFollowing && behavior == expectedBehavior,
            actual,
            expected);
    }
}
