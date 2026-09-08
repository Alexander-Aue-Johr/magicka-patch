using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class MeteorShowerScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        MeteorShowerHarness harness = new MeteorShowerHarness(magicka);
        try
        {
            report.Add(
                "meteor_shower.vector_current_play_state",
                harness.VectorExecuteUsesCurrentPlayState());
            report.Add(
                "meteor_shower.owner_current_play_state",
                harness.OwnerExecuteUsesCurrentPlayState());
            report.Add(
                "meteor_shower.active_release",
                harness.ActiveRelease());
            report.Add(
                "meteor_shower.stop_failure_release",
                harness.StopFailureRelease());
            report.Add(
                "meteor_shower.already_stopping_release",
                harness.AlreadyStoppingRelease());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class MeteorShowerHarness
{
    private readonly Type meteorType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type sceneType;
    private readonly Type avatarType;
    private readonly Type cueType;
    private readonly Type vectorType;
    private readonly FieldInfo ttlField;
    private readonly FieldInfo sceneField;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo rumbleField;
    private readonly FieldInfo playStateLevelField;
    private readonly FieldInfo currentSceneField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo spellManagerSingletonField;
    private readonly FieldInfo spellEffectsField;
    private readonly PropertyInfo lightTargetIntensity;
    private readonly MethodInfo vectorExecute;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo onRemove;
    private readonly HarmonyInstance harmony;
    private readonly object originalRecentPlayState;
    private readonly object originalSpellManager;

    internal MeteorShowerHarness(Assembly magicka)
    {
        meteorType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.MeteorShower",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        avatarType = magicka.GetType("Magicka.GameLogic.Entities.Avatar", true);
        Type spellCasterType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        cueType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Audio.Cue");
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");

        ttlField = RequireField(meteorType, "mTTL", typeof(float));
        sceneField = RequireField(meteorType, "mScene", sceneType);
        legacyPlayStateField = meteorType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (legacyPlayStateField != null &&
            legacyPlayStateField.FieldType != playStateType)
            throw new MissingFieldException(meteorType.FullName, "mPlayState");
        ownerField = RequireField(meteorType, "mOwner", spellCasterType);
        rumbleField = RequireField(meteorType, "mRumble", cueType);
        playStateLevelField = RequireField(playStateType, "mLevel", levelType);
        currentSceneField = RequireField(levelType, "mCurrentScene", sceneType);
        recentPlayStateField = RequireField(
            playStateType,
            "sRecentPlayState",
            playStateType,
            BindingFlags.Static | BindingFlags.NonPublic);
        spellManagerSingletonField = RequireField(
            spellManagerType,
            "mSingelton",
            spellManagerType,
            BindingFlags.Static | BindingFlags.NonPublic);
        spellEffectsField = spellManagerType.GetField(
            "mEffects",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (spellEffectsField == null)
            throw new MissingFieldException(spellManagerType.FullName, "mEffects");

        lightTargetIntensity = sceneType.GetProperty(
            "LightTargetIntensity",
            BindingFlags.Instance | BindingFlags.Public);
        if (lightTargetIntensity == null ||
            lightTargetIntensity.PropertyType != typeof(float) ||
            !lightTargetIntensity.CanRead || !lightTargetIntensity.CanWrite)
            throw new MissingMemberException(
                sceneType.FullName,
                "LightTargetIntensity");

        vectorExecute = meteorType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { vectorType, playStateType },
            null);
        ownerExecute = meteorType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { spellCasterType, playStateType },
            null);
        onRemove = meteorType.GetMethod(
            "OnRemove",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (vectorExecute == null || vectorExecute.ReturnType != typeof(bool))
            throw new MissingMethodException(meteorType.FullName, "Execute(Vector3, PlayState)");
        if (ownerExecute == null || ownerExecute.ReturnType != typeof(bool))
            throw new MissingMethodException(meteorType.FullName, "Execute(ISpellCaster, PlayState)");
        if (onRemove == null || onRemove.ReturnType != typeof(void))
            throw new MissingMethodException(meteorType.FullName, "OnRemove");

        PropertyInfo isStopping = cueType.GetProperty(
            "IsStopping",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo isStoppingGetter = isStopping == null
            ? null
            : isStopping.GetGetMethod();
        Type stopOptions = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Audio.AudioStopOptions");
        MethodInfo stop = cueType.GetMethod(
            "Stop",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { stopOptions },
            null);
        if (isStoppingGetter == null || isStoppingGetter.ReturnType != typeof(bool))
            throw new MissingMethodException(cueType.FullName, "get_IsStopping");
        if (stop == null || stop.ReturnType != typeof(void))
            throw new MissingMethodException(cueType.FullName, "Stop");

        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalSpellManager = spellManagerSingletonField.GetValue(null);
        object spellManager = NewUninitialized(spellManagerType);
        spellEffectsField.SetValue(
            spellManager,
            Activator.CreateInstance(spellEffectsField.FieldType));
        spellManagerSingletonField.SetValue(null, spellManager);

        harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-meteor-shower");
        harmony.Patch(
            isStoppingGetter,
            new HarmonyMethod(typeof(MeteorShowerProbe).GetMethod("IsStoppingPrefix")),
            null,
            null);
        harmony.Patch(
            stop,
            new HarmonyMethod(typeof(MeteorShowerProbe).GetMethod("StopPrefix")),
            null,
            null);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(
            "org.magickacommunitypatch.behavior-probe-meteor-shower");
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        spellManagerSingletonField.SetValue(null, originalSpellManager);
    }

    internal ScenarioResult VectorExecuteUsesCurrentPlayState()
    {
        return ExecuteUsesCurrentPlayState(vectorExecute, true);
    }

    internal ScenarioResult OwnerExecuteUsesCurrentPlayState()
    {
        return ExecuteUsesCurrentPlayState(ownerExecute, false);
    }

    internal ScenarioResult ActiveRelease()
    {
        MeteorShowerFixture fixture = CreateActiveFixture();
        MeteorShowerProbe.Reset(false, false);
        InvokeOnRemove(fixture.Meteor);
        return CleanupResult(fixture, false, 1);
    }

    internal ScenarioResult StopFailureRelease()
    {
        MeteorShowerFixture fixture = CreateActiveFixture();
        MeteorShowerProbe.Reset(false, true);
        bool expectedFailure = false;
        try
        {
            InvokeOnRemove(fixture.Meteor);
        }
        catch (InvalidOperationException)
        {
            expectedFailure = true;
        }
        return CleanupResult(fixture, expectedFailure, 1);
    }

    internal ScenarioResult AlreadyStoppingRelease()
    {
        MeteorShowerFixture fixture = CreateActiveFixture();
        MeteorShowerProbe.Reset(true, false);
        InvokeOnRemove(fixture.Meteor);
        return CleanupResult(fixture, false, 0);
    }

    private ScenarioResult ExecuteUsesCurrentPlayState(
        MethodInfo execute,
        bool vectorOverload)
    {
        object suppliedScene = NewUninitialized(sceneType);
        object currentScene = NewUninitialized(sceneType);
        object suppliedPlayState = CreatePlayState(suppliedScene);
        object currentPlayState = CreatePlayState(currentScene);
        recentPlayStateField.SetValue(null, currentPlayState);

        object meteor = NewUninitialized(meteorType);
        ttlField.SetValue(meteor, 0f);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(meteor, null);
        lightTargetIntensity.SetValue(suppliedScene, 1f, null);
        lightTargetIntensity.SetValue(currentScene, 1f, null);

        object firstArgument = vectorOverload
            ? Activator.CreateInstance(vectorType)
            : null;
        object returnValue = Invoke(
            execute,
            meteor,
            new object[] { firstArgument, suppliedPlayState });

        bool currentSceneSelected = Object.ReferenceEquals(
            sceneField.GetValue(meteor),
            currentScene);
        bool legacyReleased = legacyPlayStateField == null ||
            legacyPlayStateField.GetValue(meteor) == null;
        float suppliedLight = Convert.ToSingle(
            lightTargetIntensity.GetValue(suppliedScene, null));
        float currentLight = Convert.ToSingle(
            lightTargetIntensity.GetValue(currentScene, null));
        float ttl = Convert.ToSingle(ttlField.GetValue(meteor));
        bool returned = returnValue is bool && (bool)returnValue;
        bool passed = returned && currentSceneSelected && legacyReleased &&
            suppliedLight == 1f && currentLight == 0.4f && ttl == 17.5f;
        string actual = "returned:" + returned +
            ",current_scene:" + currentSceneSelected +
            ",legacy_released:" + legacyReleased +
            ",supplied_light:" + suppliedLight +
            ",current_light:" + currentLight +
            ",ttl:" + ttl;
        return new ScenarioResult(
            passed,
            actual,
            "returned:True,current_scene:True,legacy_released:True," +
                "supplied_light:1,current_light:0.4,ttl:17.5");
    }

    private object CreatePlayState(object scene)
    {
        object level = NewUninitialized(levelType);
        currentSceneField.SetValue(level, scene);
        object playState = NewUninitialized(playStateType);
        playStateLevelField.SetValue(playState, level);
        return playState;
    }

    private MeteorShowerFixture CreateActiveFixture()
    {
        object meteor = NewUninitialized(meteorType);
        object scene = NewUninitialized(sceneType);
        object owner = NewUninitialized(avatarType);
        object cue = NewUninitialized(cueType);
        object playState = NewUninitialized(playStateType);
        ttlField.SetValue(meteor, 4f);
        lightTargetIntensity.SetValue(scene, 0.4f, null);
        sceneField.SetValue(meteor, scene);
        ownerField.SetValue(meteor, owner);
        rumbleField.SetValue(meteor, cue);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(meteor, playState);
        return new MeteorShowerFixture(meteor, scene, owner, cue);
    }

    private ScenarioResult CleanupResult(
        MeteorShowerFixture fixture,
        bool expectedFailure,
        int expectedStopCalls)
    {
        bool released = sceneField.GetValue(fixture.Meteor) == null &&
            ownerField.GetValue(fixture.Meteor) == null &&
            rumbleField.GetValue(fixture.Meteor) == null &&
            (legacyPlayStateField == null ||
                legacyPlayStateField.GetValue(fixture.Meteor) == null);
        float ttl = Convert.ToSingle(ttlField.GetValue(fixture.Meteor));
        float light = Convert.ToSingle(
            lightTargetIntensity.GetValue(fixture.Scene, null));
        bool passed = released && ttl == 0f && light == 1f &&
            MeteorShowerProbe.StopCalls == expectedStopCalls &&
            expectedFailure == MeteorShowerProbe.ThrowOnStop;
        return new ScenarioResult(
            passed,
            "released:" + released + ",ttl:" + ttl +
                ",light:" + light +
                ",stop_calls:" + MeteorShowerProbe.StopCalls +
                ",expected_failure:" + expectedFailure,
            "released:True,ttl:0,light:1,stop_calls:" + expectedStopCalls +
                ",expected_failure:" + MeteorShowerProbe.ThrowOnStop);
    }

    private void InvokeOnRemove(object meteor)
    {
        Invoke(onRemove, meteor, new object[0]);
    }

    private static object Invoke(MethodInfo method, object instance, object[] arguments)
    {
        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static FieldInfo RequireField(
        Type type,
        string name,
        Type expectedType)
    {
        return RequireField(
            type,
            name,
            expectedType,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    }

    private static FieldInfo RequireField(
        Type type,
        string name,
        Type expectedType,
        BindingFlags flags)
    {
        FieldInfo field = type.GetField(name, flags);
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

internal sealed class MeteorShowerFixture
{
    internal object Meteor { get; private set; }
    internal object Scene { get; private set; }
    internal object Owner { get; private set; }
    internal object Cue { get; private set; }

    internal MeteorShowerFixture(
        object meteor,
        object scene,
        object owner,
        object cue)
    {
        Meteor = meteor;
        Scene = scene;
        Owner = owner;
        Cue = cue;
    }
}

public static class MeteorShowerProbe
{
    public static bool IsStopping;
    public static bool ThrowOnStop;
    public static int StopCalls;

    public static void Reset(bool isStopping, bool throwOnStop)
    {
        IsStopping = isStopping;
        ThrowOnStop = throwOnStop;
        StopCalls = 0;
    }

    public static bool IsStoppingPrefix(ref bool __result)
    {
        __result = IsStopping;
        return false;
    }

    public static bool StopPrefix()
    {
        StopCalls++;
        if (ThrowOnStop)
            throw new InvalidOperationException("simulated Cue.Stop failure");
        return false;
    }
}
