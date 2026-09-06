using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class BlizzardCleanupScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        BlizzardCleanupHarness harness = new BlizzardCleanupHarness(magicka);
        try
        {
            report.Add("blizzard_cleanup.active_release", harness.ActiveRelease());
            report.Add(
                "blizzard_cleanup.stop_failure_release",
                harness.StopFailureRelease());
            report.Add("blizzard_cleanup.empty", harness.Empty());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class BlizzardCleanupHarness
{
    private readonly Type blizzardType;
    private readonly Type cueType;
    private readonly Type sceneType;
    private readonly Type casterType;
    private readonly FieldInfo ttlField;
    private readonly FieldInfo sceneField;
    private readonly FieldInfo casterField;
    private readonly FieldInfo ambienceField;
    private readonly MethodInfo onRemove;
    private readonly HarmonyInstance harmony;

    internal BlizzardCleanupHarness(Assembly magicka)
    {
        blizzardType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Blizzard",
            true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        casterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        cueType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Audio.Cue");
        Type spellCaster = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);

        ttlField = RequireField("mTTL", typeof(float));
        sceneField = RequireField("mScene", sceneType);
        casterField = RequireField("mCaster", spellCaster);
        ambienceField = RequireField("mAmbience", cueType);
        onRemove = blizzardType.GetMethod(
            "OnRemove",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (onRemove == null || onRemove.ReturnType != typeof(void))
            throw new MissingMethodException(blizzardType.FullName, "OnRemove");

        Type stopOptions = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Audio.AudioStopOptions");
        MethodInfo stop = cueType.GetMethod(
            "Stop",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { stopOptions },
            null);
        if (stop == null || stop.ReturnType != typeof(void))
            throw new MissingMethodException(cueType.FullName, "Stop");

        harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-blizzard-cleanup");
        harmony.Patch(
            stop,
            new HarmonyMethod(typeof(BlizzardCleanupProbe).GetMethod("StopPrefix")),
            null,
            null);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(
            "org.magickacommunitypatch.behavior-probe-blizzard-cleanup");
    }

    internal ScenarioResult ActiveRelease()
    {
        BlizzardCleanupFixture fixture = CreateActiveFixture();
        BlizzardCleanupProbe.Reset(false);
        InvokeOnRemove(fixture.Blizzard);
        return Result(fixture, false);
    }

    internal ScenarioResult StopFailureRelease()
    {
        BlizzardCleanupFixture fixture = CreateActiveFixture();
        BlizzardCleanupProbe.Reset(true);
        bool expectedFailure = false;
        try
        {
            InvokeOnRemove(fixture.Blizzard);
        }
        catch (InvalidOperationException)
        {
            expectedFailure = true;
        }
        return Result(fixture, expectedFailure);
    }

    internal ScenarioResult Empty()
    {
        object blizzard = NewUninitialized(blizzardType);
        ttlField.SetValue(blizzard, 4f);
        BlizzardCleanupProbe.Reset(false);
        InvokeOnRemove(blizzard);
        bool empty = sceneField.GetValue(blizzard) == null &&
            casterField.GetValue(blizzard) == null &&
            ambienceField.GetValue(blizzard) == null;
        float ttl = Convert.ToSingle(ttlField.GetValue(blizzard));
        bool passed = empty && ttl == 0f && BlizzardCleanupProbe.StopCalls == 0;
        return new ScenarioResult(
            passed,
            "empty:" + empty + ",ttl:" + ttl +
                ",stop_calls:" + BlizzardCleanupProbe.StopCalls,
            "empty:True,ttl:0,stop_calls:0");
    }

    private BlizzardCleanupFixture CreateActiveFixture()
    {
        object blizzard = NewUninitialized(blizzardType);
        object scene = NewUninitialized(sceneType);
        object caster = NewUninitialized(casterType);
        object cue = NewUninitialized(cueType);
        ttlField.SetValue(blizzard, 4f);
        sceneField.SetValue(blizzard, scene);
        casterField.SetValue(blizzard, caster);
        ambienceField.SetValue(blizzard, cue);
        return new BlizzardCleanupFixture(blizzard, scene, caster, cue);
    }

    private ScenarioResult Result(
        BlizzardCleanupFixture fixture,
        bool expectedFailure)
    {
        bool released = sceneField.GetValue(fixture.Blizzard) == null &&
            casterField.GetValue(fixture.Blizzard) == null &&
            ambienceField.GetValue(fixture.Blizzard) == null;
        float ttl = Convert.ToSingle(ttlField.GetValue(fixture.Blizzard));
        bool passed = released && ttl == 0f &&
            BlizzardCleanupProbe.StopCalls == 1 && expectedFailure;
        if (!BlizzardCleanupProbe.ThrowOnStop)
            passed = released && ttl == 0f && BlizzardCleanupProbe.StopCalls == 1;
        return new ScenarioResult(
            passed,
            "released:" + released + ",ttl:" + ttl +
                ",stop_calls:" + BlizzardCleanupProbe.StopCalls +
                ",expected_failure:" + expectedFailure,
            "released:True,ttl:0,stop_calls:1,expected_failure:" +
                BlizzardCleanupProbe.ThrowOnStop);
    }

    private FieldInfo RequireField(string name, Type expectedType)
    {
        FieldInfo field = blizzardType.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != expectedType)
            throw new MissingFieldException(blizzardType.FullName, name);
        return field;
    }

    private void InvokeOnRemove(object blizzard)
    {
        try
        {
            onRemove.Invoke(blizzard, new object[0]);
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

internal sealed class BlizzardCleanupFixture
{
    internal object Blizzard { get; private set; }
    internal object Scene { get; private set; }
    internal object Caster { get; private set; }
    internal object Cue { get; private set; }

    internal BlizzardCleanupFixture(
        object blizzard,
        object scene,
        object caster,
        object cue)
    {
        Blizzard = blizzard;
        Scene = scene;
        Caster = caster;
        Cue = cue;
    }
}

public static class BlizzardCleanupProbe
{
    public static int StopCalls;
    public static bool ThrowOnStop;

    public static void Reset(bool throwOnStop)
    {
        StopCalls = 0;
        ThrowOnStop = throwOnStop;
    }

    public static bool StopPrefix()
    {
        StopCalls++;
        if (ThrowOnStop)
            throw new InvalidOperationException("simulated Cue.Stop failure");
        return false;
    }
}
