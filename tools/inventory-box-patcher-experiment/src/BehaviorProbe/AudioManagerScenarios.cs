using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class AudioManagerScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        AudioManagerHarness harness = new AudioManagerHarness(magicka);
        report.Add("audio_stop_all.disposed_cue", harness.DisposedCue());
        report.Add("audio_stop_all.empty", harness.Empty());
    }
}

internal sealed class AudioManagerHarness
{
    private readonly Type managerType;
    private readonly Type cueType;
    private readonly Type cueListType;
    private readonly Type stopOptionsType;
    private readonly FieldInfo activeCuesField;
    private readonly FieldInfo cueDisposedField;
    private readonly MethodInfo stopAll;

    internal AudioManagerHarness(Assembly magicka)
    {
        managerType = magicka.GetType("Magicka.Audio.AudioManager", true);
        cueType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Audio.Cue");
        cueListType = typeof(System.Collections.Generic.List<>).MakeGenericType(cueType);
        stopOptionsType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Audio.AudioStopOptions");
        activeCuesField = RuntimeReflection.RequireField(managerType, "mActiveCues");
        cueDisposedField = RuntimeReflection.RequireField(cueType, "_isDisposed");
        stopAll = managerType.GetMethod(
            "StopAll",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { stopOptionsType },
            null);
        if (stopAll == null || stopAll.ReturnType != typeof(void))
            throw new MissingMethodException(managerType.FullName, "StopAll");
    }

    internal ScenarioResult DisposedCue()
    {
        object cue = NewUninitialized(cueType);
        cueDisposedField.SetValue(cue, true);
        IList cues = (IList)Activator.CreateInstance(cueListType);
        cues.Add(cue);
        return Invoke(cues);
    }

    internal ScenarioResult Empty()
    {
        return Invoke((IList)Activator.CreateInstance(cueListType));
    }

    private ScenarioResult Invoke(IList cues)
    {
        object manager = NewUninitialized(managerType);
        activeCuesField.SetValue(manager, cues);
        object stopOptions = Enum.ToObject(stopOptionsType, 0);
        string actual = "none";
        try
        {
            stopAll.Invoke(manager, new object[] { stopOptions });
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            actual = inner.GetType().FullName;
        }
        const string expected = "none";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
