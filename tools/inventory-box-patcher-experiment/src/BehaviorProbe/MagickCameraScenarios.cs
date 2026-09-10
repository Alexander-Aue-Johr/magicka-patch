using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;
using Harmony.ILCopying;

internal static class MagickCameraScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        MagickCameraHarness harness = new MagickCameraHarness(magicka);
        report.Add("camera_follow.bodyless_target", harness.BodylessFollowTarget());
        report.Add("camera_follow.missing_target", harness.MissingFollowTarget());
        report.Add("camera_follow.other_behavior", harness.BodylessTargetInOtherBehavior());
        report.Add("camera_lifetime.playstate_not_retained",
            harness.PlayStateNotRetained());
        report.Add("camera_lifetime.current_influence_playstate",
            harness.CurrentInfluencePlayState(runtimePatchEnabled));
        report.Add("camera_lifetime.collections_cleared",
            harness.CollectionsCleared(runtimePatchEnabled));
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

    internal ScenarioResult PlayStateNotRetained()
    {
        object camera = NewCamera(null, "FollowPlayers");
        Type playState = cameraType.Assembly.GetType(
            "Magicka.GameLogic.GameStates.PlayState", true);
        object state = FormatterServices.GetUninitializedObject(playState);
        GC.SuppressFinalize(state);
        MethodInfo set = cameraType.GetMethod("SetPlayState",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        set.Invoke(camera, new object[] { state });
        bool released = RuntimeReflection.ReadField(camera, "mPlayState") == null;
        return new ScenarioResult(released,
            "retained:" + (!released), "retained:False");
    }

    internal ScenarioResult CurrentInfluencePlayState(bool runtimePatchEnabled)
    {
        MethodInfo method = cameraType.GetMethod("GetInfluenceVector",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        List<CodeInstruction> instructions = Decode(method);
        if (runtimePatchEnabled)
            instructions = new List<CodeInstruction>(
                Magicka.CommunityPatch.Runtime.MagickCameraLifetimePatch
                    .InfluenceTranspiler(instructions));
        int recentReads = 0;
        int storedReads = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodInfo call = instructions[index].operand as MethodInfo;
            if (call != null && call.Name == "get_RecentPlayState")
                recentReads++;
            FieldInfo field = instructions[index].operand as FieldInfo;
            if (field != null && field.Name == "mPlayState")
                storedReads++;
        }
        string actual = "recent:" + recentReads + ",stored:" + storedReads;
        return new ScenarioResult(recentReads == 1 && storedReads == 0,
            actual, "recent:1,stored:0");
    }

    internal ScenarioResult CollectionsCleared(bool runtimePatchEnabled)
    {
        MethodInfo declared = cameraType.GetMethod("Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (declared == null && cameraType.Assembly.GetName().Version <
            new Version(1, 10, 0, 0))
            return new ScenarioResult(true, "not_applicable", "not_applicable");
        object camera = NewCamera(null, "FollowPlayers");
        IList players = (IList)RuntimeReflection.ReadField(camera, "mPlayers");
        IList network = (IList)RuntimeReflection.ReadField(camera, "mNetworkPlayers");
        object player = FormatterServices.GetUninitializedObject(nonPlayerCharacterType);
        object remote = FormatterServices.GetUninitializedObject(nonPlayerCharacterType);
        GC.SuppressFinalize(player);
        GC.SuppressFinalize(remote);
        players.Add(player);
        network.Add(remote);
        Type effectsType = cameraType.GetField("mVisualEffects",
            BindingFlags.Instance | BindingFlags.NonPublic).FieldType;
        IDictionary effects = (IDictionary)Activator.CreateInstance(effectsType);
        Type effectValueType = effectsType.GetGenericArguments()[1];
        effects.Add(1, Activator.CreateInstance(effectValueType));
        RuntimeReflection.WriteField(camera, "mVisualEffects", effects);
        if (runtimePatchEnabled)
            Magicka.CommunityPatch.Runtime.MagickCameraLifetimePatch.DisposePrefix(camera);
        else if (declared != null)
            declared.Invoke(camera, null);
        bool cleared = players.Count == 0 && network.Count == 0 && effects.Count == 0;
        return new ScenarioResult(cleared,
            "players:" + players.Count + ",network:" + network.Count +
                ",effects:" + effects.Count,
            "players:0,network:0,effects:0");
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod("ReadCameraLifetime",
            typeof(void), Type.EmptyTypes, typeof(MagickCameraHarness), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
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
