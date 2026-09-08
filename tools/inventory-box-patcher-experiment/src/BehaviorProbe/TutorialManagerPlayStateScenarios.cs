using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class TutorialManagerPlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        TutorialManagerPlayStateHarness harness =
            new TutorialManagerPlayStateHarness(magicka);
        report.Add(
            "tutorial_play_state.initialize_release",
            harness.InitializeRelease());
        report.Add(
            "tutorial_play_state.current_update",
            harness.CurrentUpdate());
    }
}

internal sealed class TutorialManagerPlayStateHarness
{
    private readonly Type dataChannelType;
    private readonly Type playStateType;
    private readonly Type tutorialType;
    private readonly MethodInfo initialize;
    private readonly MethodInfo update;
    private readonly FieldInfo holdOffInputTimerField;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo timeField;

    internal TutorialManagerPlayStateHarness(Assembly magicka)
    {
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        tutorialType = magicka.GetType(
            "Magicka.Graphics.TutorialManager",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        initialize = tutorialType.GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playStateType },
            null);
        update = tutorialType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (initialize == null || update == null)
            throw new MissingMethodException(
                "TutorialManager behavior targets are incomplete.");

        holdOffInputTimerField = RuntimeReflection.RequireField(
            tutorialType,
            "mHoldOffInputTimer");
        timeField = RuntimeReflection.RequireField(tutorialType, "mTime");
        legacyPlayStateField = tutorialType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");

        InstallLanguageStop();
    }

    internal ScenarioResult InitializeRelease()
    {
        object tutorial = NewUninitialized(tutorialType);
        object supplied = NewUninitialized(playStateType);
        TutorialManagerPlayStateProbe.StopLanguageAccess = true;
        bool stopped = false;
        try
        {
            Invoke(initialize, tutorial, new object[] { supplied });
        }
        catch (TutorialManagerLanguageStopException)
        {
            stopped = true;
        }
        finally
        {
            TutorialManagerPlayStateProbe.StopLanguageAccess = false;
        }

        bool retained = legacyPlayStateField != null &&
            ReferenceEquals(legacyPlayStateField.GetValue(tutorial), supplied);
        bool passed = stopped && !retained;
        string actual = "stopped:" + stopped + ",play_state:" +
            (retained ? "retained" : "released");
        return new ScenarioResult(
            passed,
            actual,
            "stopped:True,play_state:released");
    }

    internal ScenarioResult CurrentUpdate()
    {
        object tutorial = NewUninitialized(tutorialType);
        object current = NewUninitialized(playStateType);
        Type gameType = RuntimeReflection.RequireField(
            playStateType,
            "mGameType").FieldType;
        RuntimeReflection.WriteField(
            current,
            "mGameType",
            Enum.Parse(gameType, "Challenge"));
        recentPlayStateField.SetValue(null, current);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(tutorial, null);
        holdOffInputTimerField.SetValue(tutorial, 3f);
        timeField.SetValue(tutorial, 5f);

        bool completed = true;
        string failure = "none";
        try
        {
            Invoke(
                update,
                tutorial,
                new object[] { Enum.ToObject(dataChannelType, 0), 0.5f });
        }
        catch (NullReferenceException)
        {
            completed = false;
            failure = "null_reference";
        }

        float holdOff = Convert.ToSingle(
            holdOffInputTimerField.GetValue(tutorial));
        float time = Convert.ToSingle(timeField.GetValue(tutorial));
        bool passed = completed && Math.Abs(holdOff - 2.5f) < 0.0001f &&
            Math.Abs(time - 5.5f) < 0.0001f;
        string actual = "completed:" + completed + ",failure:" + failure +
            ",hold_off:" + holdOff + ",time:" + time;
        return new ScenarioResult(
            passed,
            actual,
            "completed:True,failure:none,hold_off:2.5,time:5.5");
    }

    private void InstallLanguageStop()
    {
        Type languageManager = tutorialType.Assembly.GetType(
            "Magicka.Localization.LanguageManager",
            true);
        MethodInfo getter = languageManager.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();
        HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-tutorial-play-state").Patch(
                getter,
                new HarmonyMethod(
                    typeof(TutorialManagerPlayStateProbe).GetMethod(
                        "LanguageInstancePrefix")),
                null,
                null);
    }

    private static void Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class TutorialManagerPlayStateProbe
{
    public static bool StopLanguageAccess;

    public static void LanguageInstancePrefix()
    {
        if (StopLanguageAccess)
            throw new TutorialManagerLanguageStopException();
    }
}

public sealed class TutorialManagerLanguageStopException : Exception
{
}
