using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class StarfallScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        StarfallHarness harness = new StarfallHarness(magicka);
        report.Add("starfall.play_state_release", harness.PlayStateRelease());
        report.Add("starfall.current_play_state", harness.CurrentPlayState());
        report.Add("starfall.no_damage_queue", harness.NoDamageQueue());
    }
}

internal sealed class StarfallHarness
{
    private readonly Assembly magicka;
    private readonly Type dataChannelType;
    private readonly Type infoType;
    private readonly Type playStateType;
    private readonly Type starfallType;
    private readonly Type vectorType;
    private readonly MethodInfo execute;
    private readonly MethodInfo update;
    private readonly FieldInfo legacyPlayState;
    private readonly FieldInfo recentPlayState;
    private readonly FieldInfo queueField;
    private readonly object starfall;

    internal StarfallHarness(Assembly magicka)
    {
        this.magicka = magicka;
        dataChannelType = RuntimeReflection.FindLoadedType("PolygonHead.DataChannel");
        playStateType = magicka.GetType("Magicka.GameLogic.GameStates.PlayState", true);
        starfallType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Starfall",
            true);
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        execute = starfallType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { ownerType, playStateType, vectorType, typeof(bool) },
            null);
        update = starfallType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (execute == null || update == null)
            throw new MissingMethodException("Starfall behavior targets are incomplete.");

        legacyPlayState = RuntimeReflection.RequireField(starfallType, "sPlayState");
        recentPlayState = RuntimeReflection.RequireField(playStateType, "sRecentPlayState");
        queueField = RuntimeReflection.RequireField(starfallType, "sQueue");
        infoType = starfallType.GetNestedType("Info", BindingFlags.NonPublic);
        if (infoType == null)
            throw new MissingMemberException(starfallType.FullName, "Info");

        starfall = NewUninitialized(starfallType);
        InstallSegmentProbe(magicka);
    }

    internal ScenarioResult PlayStateRelease()
    {
        object playState = NewUninitialized(playStateType);
        ClearQueue();
        legacyPlayState.SetValue(null, null);
        bool result = InvokeExecute(playState, false);
        bool retained = ReferenceEquals(legacyPlayState.GetValue(null), playState);
        return new ScenarioResult(
            !retained && result,
            "result:" + result + ",state:" + (retained ? "retained" : "released"),
            "result:True,state:released");
    }

    internal ScenarioResult CurrentPlayState()
    {
        object oldState = NewUninitialized(playStateType);
        object currentState = NewUninitialized(playStateType);
        ClearQueue();
        AddPendingStrike();
        object expectedLevelModel = ConfigureLevelChain(magicka, currentState);
        legacyPlayState.SetValue(null, oldState);
        recentPlayState.SetValue(null, currentState);
        StarfallProbe.ObservedLevelModel = null;

        bool reached = false;
        string failure = "none";
        try
        {
            update.Invoke(
                starfall,
                new object[] { Activator.CreateInstance(dataChannelType), 0.02f });
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException;
            while (inner != null && inner.InnerException != null)
                inner = inner.InnerException;
            reached = inner is StarfallLevelProbeReachedException;
            failure = inner == null ? "missing" : inner.GetType().FullName;
        }

        bool current = ReferenceEquals(
            StarfallProbe.ObservedLevelModel,
            expectedLevelModel);
        return new ScenarioResult(
            reached && current,
            "level_reached:" + reached + ",state:" +
                (current ? "current" : "stale") + ",failure:" + failure,
            "level_reached:True,state:current");
    }

    internal ScenarioResult NoDamageQueue()
    {
        ClearQueue();
        bool result = InvokeExecute(NewUninitialized(playStateType), false);
        int count = (int)queueField.GetValue(null).GetType().GetProperty("Count").GetValue(
            queueField.GetValue(null),
            null);
        return new ScenarioResult(
            result && count == 0,
            "result:" + result + ",count:" + count,
            "result:True,count:0");
    }

    private bool InvokeExecute(object playState, bool dealDamage)
    {
        object position = Activator.CreateInstance(vectorType);
        try
        {
            return (bool)execute.Invoke(
                starfall,
                new object[] { null, playState, position, dealDamage });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private void ClearQueue()
    {
        object queue = queueField.GetValue(null);
        queue.GetType().GetMethod("Clear").Invoke(queue, null);
    }

    private void AddPendingStrike()
    {
        object info = Activator.CreateInstance(infoType);
        infoType.GetField("CastDelay").SetValue(info, 0.01f);
        object queue = queueField.GetValue(null);
        queue.GetType().GetMethod("Add").Invoke(queue, new object[] { info });
    }

    private static object ConfigureLevelChain(Assembly magicka, object playState)
    {
        Type levelType = magicka.GetType("Magicka.Levels.Level", true);
        Type sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        Type levelModelType = magicka.GetType("Magicka.Levels.LevelModel", true);
        object level = NewUninitialized(levelType);
        object scene = NewUninitialized(sceneType);
        object levelModel = NewUninitialized(levelModelType);
        RuntimeReflection.WriteField(playState, "mLevel", level);
        RuntimeReflection.WriteField(level, "mCurrentScene", scene);
        RuntimeReflection.WriteField(scene, "mModel", levelModel);
        return levelModel;
    }

    private static void InstallSegmentProbe(Assembly magicka)
    {
        Type levelModelType = magicka.GetType("Magicka.Levels.LevelModel", true);
        MethodInfo segmentIntersect = null;
        MethodInfo[] methods = levelModelType.GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name == "SegmentIntersect" &&
                parameters.Length == 7 &&
                parameters[6].ParameterType == typeof(bool))
            {
                if (segmentIntersect != null)
                    throw new InvalidOperationException(
                        "Multiple Starfall SegmentIntersect probes matched.");
                segmentIntersect = methods[index];
            }
        }
        if (segmentIntersect == null)
            throw new MissingMethodException(levelModelType.FullName, "SegmentIntersect");
        HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-starfall").Patch(
                segmentIntersect,
                new HarmonyMethod(typeof(StarfallProbe).GetMethod("SegmentPrefix")),
                null,
                null);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class StarfallProbe
{
    public static object ObservedLevelModel;

    public static void SegmentPrefix(object __instance)
    {
        ObservedLevelModel = __instance;
        throw new StarfallLevelProbeReachedException();
    }
}

internal sealed class StarfallLevelProbeReachedException : Exception
{
}
