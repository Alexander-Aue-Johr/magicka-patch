using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class ArrowRainPlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ArrowRainPlayStateHarness harness =
            new ArrowRainPlayStateHarness(magicka);
        try
        {
            report.Add(
                "arrow_rain.vector_release",
                harness.VectorExecuteRelease());
            report.Add(
                "arrow_rain.owner_release",
                harness.OwnerExecuteRelease());
            report.Add(
                "arrow_rain.update_current_play_state",
                harness.UpdateUsesCurrentPlayState());
            report.Add(
                "arrow_rain.remove_current_scene",
                harness.RemoveUsesCurrentScene());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class ArrowRainPlayStateHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-arrow-rain";

    private readonly Type arrowRainType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type sceneType;
    private readonly Type vectorType;
    private readonly Type dataChannelType;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo levelField;
    private readonly FieldInfo currentSceneField;
    private readonly FieldInfo sceneField;
    private readonly FieldInfo ttlField;
    private readonly FieldInfo launchedField;
    private readonly FieldInfo arrowSpawnTimerField;
    private readonly FieldInfo lastArrowNumberField;
    private readonly FieldInfo intensityField;
    private readonly FieldInfo lifeTimeField;
    private readonly FieldInfo abilityElementField;
    private readonly FieldInfo networkManagerSingletonField;
    private readonly FieldInfo spellManagerSingletonField;
    private readonly FieldInfo spellEffectsField;
    private readonly MethodInfo vectorExecute;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo update;
    private readonly MethodInfo onRemove;
    private readonly PropertyInfo lightTargetIntensity;
    private readonly object originalRecentPlayState;
    private readonly object originalNetworkManager;
    private readonly object originalSpellManager;
    private readonly HarmonyInstance harmony;

    internal ArrowRainPlayStateHarness(Assembly magicka)
    {
        arrowRainType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ArrowRain",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        Type spellCasterType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type missileType = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity",
            true);
        Type networkManagerType = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");

        recentPlayStateField = RequireField(
            playStateType,
            "sRecentPlayState",
            playStateType,
            BindingFlags.Static | BindingFlags.NonPublic);
        legacyPlayStateField = arrowRainType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (legacyPlayStateField != null &&
            legacyPlayStateField.FieldType != playStateType)
            throw new MissingFieldException(
                arrowRainType.FullName,
                "mPlayState");
        levelField = RequireField(
            playStateType,
            "mLevel",
            levelType,
            BindingFlags.Instance | BindingFlags.NonPublic);
        currentSceneField = RequireField(
            levelType,
            "mCurrentScene",
            sceneType,
            BindingFlags.Instance | BindingFlags.NonPublic);
        sceneField = RequireField(
            arrowRainType,
            "mScene",
            sceneType,
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        ttlField = RequireDeclaredField("mTTL", typeof(float));
        launchedField = RequireDeclaredField("mLaunched", typeof(bool));
        arrowSpawnTimerField = RequireDeclaredField(
            "mArrowSpawnTimer",
            typeof(float));
        lastArrowNumberField = RequireDeclaredField(
            "mLastArrowNum",
            typeof(int));
        intensityField = RequireDeclaredField("INTESITY", typeof(double));
        lifeTimeField = RequireDeclaredField("LIFE_TIME", typeof(float));
        abilityElementField = arrowRainType.GetField(
            "mAbilityElement",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (abilityElementField == null ||
            !abilityElementField.FieldType.IsEnum)
            throw new MissingFieldException(
                arrowRainType.FullName,
                "mAbilityElement");

        networkManagerSingletonField = RequireField(
            networkManagerType,
            "sSingelton",
            networkManagerType,
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
            throw new MissingFieldException(
                spellManagerType.FullName,
                "mEffects");

        vectorExecute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { vectorType, playStateType },
            typeof(bool));
        ownerExecute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { spellCasterType, playStateType },
            typeof(bool));
        update = RequireMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { dataChannelType, typeof(float) },
            typeof(void));
        onRemove = RequireMethod(
            "OnRemove",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            Type.EmptyTypes,
            typeof(void));

        MethodInfo getMissile = missileType.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { playStateType },
            null);
        lightTargetIntensity = sceneType.GetProperty(
            "LightTargetIntensity",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo setLight = lightTargetIntensity == null
            ? null
            : lightTargetIntensity.GetSetMethod();
        if (getMissile == null || getMissile.ReturnType != missileType)
            throw new MissingMethodException(
                missileType.FullName,
                "GetInstance");
        if (setLight == null || setLight.ReturnType != typeof(void))
            throw new MissingMethodException(
                sceneType.FullName,
                "set_LightTargetIntensity");

        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalNetworkManager = networkManagerSingletonField.GetValue(null);
        originalSpellManager = spellManagerSingletonField.GetValue(null);
        networkManagerSingletonField.SetValue(
            null,
            NewUninitialized(networkManagerType));
        object spellManager = NewUninitialized(spellManagerType);
        spellEffectsField.SetValue(
            spellManager,
            Activator.CreateInstance(spellEffectsField.FieldType));
        spellManagerSingletonField.SetValue(null, spellManager);

        harmony = HarmonyInstance.Create(HarmonyOwner);
        harmony.Patch(
            getMissile,
            new HarmonyMethod(
                typeof(ArrowRainPlayStateProbe).GetMethod(
                    "GetMissilePrefix")),
            null,
            null);
    }

    internal void Dispose()
    {
        ArrowRainPlayStateProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        networkManagerSingletonField.SetValue(null, originalNetworkManager);
        spellManagerSingletonField.SetValue(null, originalSpellManager);
    }

    internal ScenarioResult VectorExecuteRelease()
    {
        return ExecuteRelease(vectorExecute, true);
    }

    internal ScenarioResult OwnerExecuteRelease()
    {
        return ExecuteRelease(ownerExecute, false);
    }

    internal ScenarioResult UpdateUsesCurrentPlayState()
    {
        object stale = NewPlayState(NewUninitialized(sceneType));
        object current = NewPlayState(NewUninitialized(sceneType));
        recentPlayStateField.SetValue(null, current);
        object arrow = NewUninitialized(arrowRainType);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(arrow, stale);
        ttlField.SetValue(arrow, 10f);
        launchedField.SetValue(arrow, true);
        arrowSpawnTimerField.SetValue(arrow, 0f);
        lastArrowNumberField.SetValue(arrow, 0);
        intensityField.SetValue(arrow, 2.0);
        lifeTimeField.SetValue(arrow, 4.5f);
        abilityElementField.SetValue(
            arrow,
            Enum.ToObject(abilityElementField.FieldType, 0));

        ArrowRainPlayStateProbe.Reset();
        ArrowRainPlayStateProbe.Enabled = true;
        bool reachedMissile = false;
        try
        {
            Invoke(
                update,
                arrow,
                new object[] {
                    Activator.CreateInstance(dataChannelType),
                    1f
                });
        }
        catch (ArrowRainMissileReachedException)
        {
            reachedMissile = true;
        }
        finally
        {
            ArrowRainPlayStateProbe.Enabled = false;
        }

        bool currentUsed = ReferenceEquals(
            ArrowRainPlayStateProbe.MissilePlayState,
            current);
        bool passed = reachedMissile && currentUsed &&
            ArrowRainPlayStateProbe.MissileCalls == 1;
        return new ScenarioResult(
            passed,
            "tail:" + reachedMissile +
                ",state:" + (currentUsed ? "current" : "stale") +
                ",calls:" + ArrowRainPlayStateProbe.MissileCalls,
            "tail:True,state:current,calls:1");
    }

    internal ScenarioResult RemoveUsesCurrentScene()
    {
        object staleScene = NewUninitialized(sceneType);
        object currentScene = NewUninitialized(sceneType);
        object current = NewPlayState(currentScene);
        recentPlayStateField.SetValue(null, current);
        object arrow = NewUninitialized(arrowRainType);
        sceneField.SetValue(arrow, staleScene);
        ttlField.SetValue(arrow, 4f);
        lightTargetIntensity.SetValue(staleScene, 0.25f, null);
        lightTargetIntensity.SetValue(currentScene, 0.5f, null);

        Invoke(onRemove, arrow, new object[0]);

        float staleLight = Convert.ToSingle(
            lightTargetIntensity.GetValue(staleScene, null));
        float currentLight = Convert.ToSingle(
            lightTargetIntensity.GetValue(currentScene, null));
        float ttl = Convert.ToSingle(ttlField.GetValue(arrow));
        bool passed = staleLight == 0.25f && currentLight == 1f && ttl == 0f;
        return new ScenarioResult(
            passed,
            "stale_light:" + staleLight +
                ",current_light:" + currentLight +
                ",ttl:" + ttl,
            "stale_light:0.25,current_light:1,ttl:0");
    }

    private ScenarioResult ExecuteRelease(
        MethodInfo method,
        bool vectorOverload)
    {
        object suppliedScene = NewUninitialized(sceneType);
        object supplied = NewPlayState(suppliedScene);
        object current = NewPlayState(NewUninitialized(sceneType));
        recentPlayStateField.SetValue(null, current);
        object arrow = NewUninitialized(arrowRainType);
        ttlField.SetValue(arrow, 0f);
        sceneField.SetValue(arrow, null);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(arrow, null);
        object first = vectorOverload
            ? Activator.CreateInstance(vectorType)
            : null;

        bool returned = (bool)Invoke(
            method,
            arrow,
            new object[] { first, supplied });
        bool sceneReleased = sceneField.GetValue(arrow) == null;
        bool stateReleased = legacyPlayStateField == null ||
            legacyPlayStateField.GetValue(arrow) == null;
        bool passed = !returned && sceneReleased && stateReleased;
        return new ScenarioResult(
            passed,
            "returned:" + returned +
                ",scene_released:" + sceneReleased +
                ",state_released:" + stateReleased,
            "returned:False,scene_released:True,state_released:True");
    }

    private object NewPlayState(object scene)
    {
        object level = NewUninitialized(levelType);
        currentSceneField.SetValue(level, scene);
        object state = NewUninitialized(playStateType);
        levelField.SetValue(state, level);
        return state;
    }

    private FieldInfo RequireDeclaredField(string name, Type type)
    {
        return RequireField(
            arrowRainType,
            name,
            type,
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
    }

    private MethodInfo RequireMethod(
        string name,
        BindingFlags flags,
        Type[] parameters,
        Type returnType)
    {
        MethodInfo method = arrowRainType.GetMethod(
            name,
            flags,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != returnType)
            throw new MissingMethodException(arrowRainType.FullName, name);
        return method;
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

    private static object Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
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

public sealed class ArrowRainMissileReachedException : Exception
{
}

public static class ArrowRainPlayStateProbe
{
    public static bool Enabled;
    public static int MissileCalls;
    public static object MissilePlayState;

    public static void Reset()
    {
        MissileCalls = 0;
        MissilePlayState = null;
    }

    public static bool GetMissilePrefix(object __0)
    {
        if (!Enabled)
            return true;
        MissileCalls++;
        MissilePlayState = __0;
        throw new ArrowRainMissileReachedException();
    }
}
