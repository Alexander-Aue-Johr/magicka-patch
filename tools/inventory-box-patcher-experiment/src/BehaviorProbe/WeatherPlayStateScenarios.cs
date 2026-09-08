using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class WeatherPlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        WeatherPlayStateHarness harness = new WeatherPlayStateHarness(magicka);
        try
        {
            report.Add("rain.vector_current_play_state", harness.RainVector());
            report.Add("rain.owner_current_play_state", harness.RainOwner());
            report.Add("rain.update_current_play_state", harness.RainUpdate());
            report.Add("rain.remove_releases_scene", harness.RainRemove());
            report.Add(
                "thunderstorm.vector_current_play_state",
                harness.ThunderstormVector());
            report.Add(
                "thunderstorm.owner_current_play_state",
                harness.ThunderstormOwner());
            report.Add(
                "thunderstorm.update_current_play_state",
                harness.ThunderstormUpdate());
            report.Add(
                "thunderstorm.remove_current_play_state",
                harness.ThunderstormRemove());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class WeatherPlayStateHarness
{
    private readonly Assembly magicka;
    private readonly Type rainType;
    private readonly Type thunderstormType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type gameSceneType;
    private readonly Type polygonSceneType;
    private readonly Type levelModelType;
    private readonly Type navMeshType;
    private readonly Type ownerType;
    private readonly Type vectorType;
    private readonly Type dataChannelType;
    private readonly Type cueType;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo playStateLevelField;
    private readonly FieldInfo gameStateSceneField;
    private readonly FieldInfo levelCurrentSceneField;
    private readonly FieldInfo gameSceneIndoorField;
    private readonly FieldInfo gameSceneModelField;
    private readonly FieldInfo levelModelNavMeshField;
    private readonly FieldInfo polygonSceneCameraField;
    private readonly FieldInfo entityPlayStateField;
    private readonly FieldInfo rainLegacyPlayStateField;
    private readonly FieldInfo rainSceneField;
    private readonly FieldInfo rainCasterField;
    private readonly FieldInfo rainAmbienceField;
    private readonly FieldInfo rainDoDamageField;
    private readonly FieldInfo thunderLegacyPlayStateField;
    private readonly FieldInfo thunderOwnerField;
    private readonly FieldInfo thunderRainField;
    private readonly FieldInfo thunderAmbienceField;
    private readonly FieldInfo thunderIndoorField;
    private readonly FieldInfo thunderBoltTtlField;
    private readonly FieldInfo thunderPerfectStormField;
    private readonly FieldInfo thunderPlayersAliveField;
    private readonly MethodInfo rainVectorExecute;
    private readonly MethodInfo rainOwnerExecute;
    private readonly MethodInfo rainUpdate;
    private readonly MethodInfo rainOnRemove;
    private readonly MethodInfo thunderVectorExecute;
    private readonly MethodInfo thunderOwnerExecute;
    private readonly MethodInfo thunderUpdate;
    private readonly MethodInfo thunderOnRemove;
    private readonly FieldInfo gameSingletonField;
    private readonly FieldInfo networkSingletonField;
    private readonly FieldInfo spellManagerSingletonField;
    private readonly FieldInfo effectManagerSingletonField;
    private readonly FieldInfo achievementSingletonField;
    private readonly object originalRecentPlayState;
    private readonly object originalGame;
    private readonly object originalNetworkManager;
    private readonly object originalSpellManager;
    private readonly object originalEffectManager;
    private readonly object originalAchievementManager;
    private readonly HarmonyInstance harmony;

    internal WeatherPlayStateHarness(Assembly magicka)
    {
        this.magicka = magicka;
        rainType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Rain",
            true);
        thunderstormType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Thunderstorm",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        gameSceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        polygonSceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        levelModelType = magicka.GetType("Magicka.Levels.LevelModel", true);
        navMeshType = magicka.GetType("Magicka.PathFinding.NavMesh", true);
        ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        Type spellCasterType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        dataChannelType = RuntimeReflection.FindLoadedType("PolygonHead.DataChannel");
        cueType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Audio.Cue");

        recentPlayStateField = RequireField(
            playStateType,
            "sRecentPlayState",
            playStateType,
            BindingFlags.Static | BindingFlags.NonPublic);
        playStateLevelField = RequireField(playStateType, "mLevel", levelType);
        gameStateSceneField = RequireHierarchyField(
            playStateType,
            "mScene",
            polygonSceneType);
        levelCurrentSceneField = RequireField(
            levelType,
            "mCurrentScene",
            gameSceneType);
        gameSceneIndoorField = RequireField(
            gameSceneType,
            "mIndoor",
            typeof(bool));
        gameSceneModelField = RequireField(
            gameSceneType,
            "mModel",
            levelModelType);
        levelModelNavMeshField = RequireField(
            levelModelType,
            "mNavMesh",
            navMeshType);
        Type cameraType = magicka.GetType("Magicka.Graphics.MagickCamera", true);
        polygonSceneCameraField = RequireHierarchyField(
            polygonSceneType,
            "mCamera",
            cameraType.BaseType);
        entityPlayStateField = RequireHierarchyField(
            ownerType,
            "mPlayState",
            playStateType);

        rainLegacyPlayStateField = OptionalField(
            rainType,
            "mPlayState",
            playStateType);
        rainSceneField = RequireField(rainType, "mScene", gameSceneType);
        rainCasterField = RequireField(rainType, "mCaster", spellCasterType);
        rainAmbienceField = RequireField(rainType, "mAmbience", cueType);
        rainDoDamageField = RequireField(rainType, "mDoDamage", typeof(bool));

        thunderLegacyPlayStateField = OptionalField(
            thunderstormType,
            "mPlayState",
            playStateType);
        thunderOwnerField = RequireField(
            thunderstormType,
            "mOwner",
            spellCasterType);
        thunderRainField = RequireField(thunderstormType, "mRain", rainType);
        thunderAmbienceField = RequireField(
            thunderstormType,
            "mAmbience",
            cueType);
        thunderIndoorField = RequireField(
            thunderstormType,
            "mIndoor",
            typeof(bool));
        thunderBoltTtlField = RequireField(
            thunderstormType,
            "mBoltTTL",
            typeof(float));
        thunderPerfectStormField = RequireField(
            thunderstormType,
            "mPerfectStorm",
            typeof(bool));
        thunderPlayersAliveField = RequireField(
            thunderstormType,
            "mPlayersAliveAtStart",
            typeof(bool[]));

        rainVectorExecute = RequireMethod(
            rainType,
            "Execute",
            new Type[] { vectorType, playStateType },
            typeof(bool));
        rainOwnerExecute = RequireMethod(
            rainType,
            "Execute",
            new Type[] { spellCasterType, playStateType },
            typeof(bool));
        rainUpdate = RequireMethod(
            rainType,
            "Update",
            new Type[] { dataChannelType, typeof(float) },
            typeof(void));
        rainOnRemove = RequireMethod(
            rainType,
            "OnRemove",
            Type.EmptyTypes,
            typeof(void));
        thunderVectorExecute = RequireMethod(
            thunderstormType,
            "Execute",
            new Type[] { vectorType, playStateType },
            typeof(bool));
        thunderOwnerExecute = RequireMethod(
            thunderstormType,
            "Execute",
            new Type[] { spellCasterType, playStateType },
            typeof(bool));
        thunderUpdate = RequireMethod(
            thunderstormType,
            "Update",
            new Type[] { dataChannelType, typeof(float) },
            typeof(void));
        thunderOnRemove = RequireMethod(
            thunderstormType,
            "OnRemove",
            Type.EmptyTypes,
            typeof(void));

        Type gameType = magicka.GetType("Magicka.Game", true);
        Type playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        Type networkManagerType = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        Type effectManagerType = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        Type achievementType = magicka.GetType(
            "Magicka.Achievements.AchievementsManager",
            true);
        gameSingletonField = RequireField(
            gameType,
            "mSingelton",
            gameType,
            BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo gamePlayersField = RequireField(
            gameType,
            "mPlayers",
            playerType.MakeArrayType());
        networkSingletonField = RequireField(
            networkManagerType,
            "sSingelton",
            networkManagerType,
            BindingFlags.Static | BindingFlags.NonPublic);
        spellManagerSingletonField = RequireField(
            spellManagerType,
            "mSingelton",
            spellManagerType,
            BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo spellEffectsField = RequireAnyField(
            spellManagerType,
            "mEffects",
            BindingFlags.Instance | BindingFlags.NonPublic);
        effectManagerSingletonField = RequireField(
            effectManagerType,
            "mSingelton",
            effectManagerType,
            BindingFlags.Static | BindingFlags.NonPublic);
        achievementSingletonField = RequireField(
            achievementType,
            "sSingelton",
            achievementType,
            BindingFlags.Static | BindingFlags.NonPublic);

        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalGame = gameSingletonField.GetValue(null);
        originalNetworkManager = networkSingletonField.GetValue(null);
        originalSpellManager = spellManagerSingletonField.GetValue(null);
        originalEffectManager = effectManagerSingletonField.GetValue(null);
        originalAchievementManager = achievementSingletonField.GetValue(null);

        object game = NewUninitialized(gameType);
        gamePlayersField.SetValue(game, Array.CreateInstance(playerType, 0));
        gameSingletonField.SetValue(null, game);
        networkSingletonField.SetValue(
            null,
            NewUninitialized(networkManagerType));
        object spellManager = NewUninitialized(spellManagerType);
        spellEffectsField.SetValue(
            spellManager,
            Activator.CreateInstance(spellEffectsField.FieldType));
        spellManagerSingletonField.SetValue(null, spellManager);
        effectManagerSingletonField.SetValue(
            null,
            NewUninitialized(effectManagerType));
        achievementSingletonField.SetValue(
            null,
            NewUninitialized(achievementType));

        harmony = InstallDependencyStubs(
            effectManagerType,
            achievementType);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(
            "org.magickacommunitypatch.behavior-probe-weather-play-state");
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        gameSingletonField.SetValue(null, originalGame);
        networkSingletonField.SetValue(null, originalNetworkManager);
        spellManagerSingletonField.SetValue(null, originalSpellManager);
        effectManagerSingletonField.SetValue(null, originalEffectManager);
        achievementSingletonField.SetValue(null, originalAchievementManager);
    }

    internal ScenarioResult RainVector()
    {
        return RainExecute(rainVectorExecute, Activator.CreateInstance(vectorType));
    }

    internal ScenarioResult RainOwner()
    {
        WeatherPlayStateFixture current = CreatePlayState(false, true, false);
        object owner = NewUninitialized(ownerType);
        entityPlayStateField.SetValue(owner, current.PlayState);
        return RainExecute(rainOwnerExecute, owner, current);
    }

    internal ScenarioResult RainUpdate()
    {
        WeatherPlayStateFixture supplied = CreatePlayState(false, false, false);
        WeatherPlayStateFixture current = CreatePlayState(false, true, false);
        recentPlayStateField.SetValue(null, current.PlayState);
        object rain = NewUninitialized(rainType);
        SetOptional(rainLegacyPlayStateField, rain, supplied.PlayState);
        rainDoDamageField.SetValue(rain, false);
        bool completed = TryInvoke(
            rainUpdate,
            rain,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.1f },
            null);
        bool passed = completed && WeatherPlayStateProbe.UpdateEffectCalls == 1;
        WeatherPlayStateProbe.UpdateEffectCalls = 0;
        return new ScenarioResult(
            passed,
            "completed:" + completed + ",update_calls:" +
                (passed ? 1 : 0),
            "completed:True,update_calls:1");
    }

    internal ScenarioResult RainRemove()
    {
        WeatherPlayStateFixture fixture = CreatePlayState(false, true, false);
        object rain = NewUninitialized(rainType);
        object owner = NewUninitialized(ownerType);
        object cue = NewUninitialized(cueType);
        rainSceneField.SetValue(rain, fixture.GameScene);
        rainCasterField.SetValue(rain, owner);
        rainAmbienceField.SetValue(rain, cue);
        SetLight(fixture.GameScene, 0.333f);
        WeatherPlayStateProbe.ResetCalls();
        bool completed = TryInvoke(rainOnRemove, rain, new object[0], null);
        bool released = rainSceneField.GetValue(rain) == null &&
            rainCasterField.GetValue(rain) == null;
        float light = GetLight(fixture.GameScene);
        bool passed = completed && released && light == 1f &&
            WeatherPlayStateProbe.CueStopCalls == 1 &&
            WeatherPlayStateProbe.StopEffectCalls == 1;
        return new ScenarioResult(
            passed,
            "completed:" + completed + ",released:" + released +
                ",light:" + light + ",cue_stops:" +
                WeatherPlayStateProbe.CueStopCalls + ",effect_stops:" +
                WeatherPlayStateProbe.StopEffectCalls,
            "completed:True,released:True,light:1,cue_stops:1,effect_stops:1");
    }

    internal ScenarioResult ThunderstormVector()
    {
        return ThunderstormExecute(
            thunderVectorExecute,
            Activator.CreateInstance(vectorType));
    }

    internal ScenarioResult ThunderstormOwner()
    {
        WeatherPlayStateFixture current = CreatePlayState(true, true, false);
        object owner = NewUninitialized(ownerType);
        entityPlayStateField.SetValue(owner, current.PlayState);
        return ThunderstormExecute(thunderOwnerExecute, owner, current);
    }

    internal ScenarioResult ThunderstormUpdate()
    {
        WeatherPlayStateFixture supplied = CreatePlayState(false, true, true);
        WeatherPlayStateFixture current = CreatePlayState(false, true, true);
        recentPlayStateField.SetValue(null, current.PlayState);
        object thunderstorm = CreateThunderstorm();
        SetOptional(
            thunderLegacyPlayStateField,
            thunderstorm,
            supplied.PlayState);
        thunderIndoorField.SetValue(thunderstorm, false);
        thunderBoltTtlField.SetValue(thunderstorm, 0f);
        WeatherPlayStateProbe.RecordedNavMesh = null;
        SpawnSlimeProbe.ObservedNavMesh = null;
        WeatherPlayStateProbe.LastInvocationError = null;
        bool reachedNavMesh = TryInvoke(
            thunderUpdate,
            thunderstorm,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.1f },
            typeof(SpawnSlimeNavMeshReachedException));
        object observedNavMesh = SpawnSlimeProbe.ObservedNavMesh ??
            WeatherPlayStateProbe.RecordedNavMesh;
        bool currentSelected = Object.ReferenceEquals(
            observedNavMesh,
            current.NavMesh);
        bool passed = reachedNavMesh && currentSelected;
        return new ScenarioResult(
            passed,
            "reached_nav_mesh:" + reachedNavMesh +
                ",current_nav_mesh:" + currentSelected +
                ",error:" + WeatherPlayStateProbe.LastInvocationError,
            "reached_nav_mesh:True,current_nav_mesh:True");
    }

    internal ScenarioResult ThunderstormRemove()
    {
        WeatherPlayStateFixture supplied = CreatePlayState(false, true, false);
        WeatherPlayStateFixture current = CreatePlayState(false, true, false);
        recentPlayStateField.SetValue(null, current.PlayState);
        object thunderstorm = CreateThunderstorm();
        object owner = NewUninitialized(ownerType);
        object rain = NewUninitialized(rainType);
        thunderOwnerField.SetValue(thunderstorm, owner);
        thunderRainField.SetValue(thunderstorm, rain);
        thunderAmbienceField.SetValue(thunderstorm, NewUninitialized(cueType));
        thunderPerfectStormField.SetValue(thunderstorm, true);
        SetOptional(
            thunderLegacyPlayStateField,
            thunderstorm,
            supplied.PlayState);
        WeatherPlayStateProbe.ResetCalls();
        bool completed = TryInvoke(thunderOnRemove, thunderstorm, new object[0], null);
        bool currentAward = Object.ReferenceEquals(
            WeatherPlayStateProbe.AwardedPlayState,
            current.PlayState);
        bool ownerReleased = thunderOwnerField.GetValue(thunderstorm) == null;
        bool rainPreserved = Object.ReferenceEquals(
            thunderRainField.GetValue(thunderstorm),
            rain);
        bool passed = completed && currentAward && ownerReleased &&
            rainPreserved && WeatherPlayStateProbe.CueStopCalls == 1;
        return new ScenarioResult(
            passed,
            "completed:" + completed + ",current_award:" + currentAward +
                ",owner_released:" + ownerReleased +
                ",rain_preserved:" + rainPreserved + ",cue_stops:" +
                WeatherPlayStateProbe.CueStopCalls,
            "completed:True,current_award:True,owner_released:True," +
                "rain_preserved:True,cue_stops:1");
    }

    private ScenarioResult RainExecute(MethodInfo method, object firstArgument)
    {
        return RainExecute(method, firstArgument, CreatePlayState(false, true, false));
    }

    private ScenarioResult RainExecute(
        MethodInfo method,
        object firstArgument,
        WeatherPlayStateFixture current)
    {
        WeatherPlayStateFixture supplied = CreatePlayState(false, true, false);
        recentPlayStateField.SetValue(null, current.PlayState);
        object rain = NewUninitialized(rainType);
        rainAmbienceField.SetValue(rain, NewUninitialized(cueType));
        SetOptional(rainLegacyPlayStateField, rain, null);
        WeatherPlayStateProbe.SuppressRainExecute = false;
        bool returned = (bool)Invoke(
            method,
            rain,
            new object[] { firstArgument, supplied.PlayState });
        bool currentScene = Object.ReferenceEquals(
            rainSceneField.GetValue(rain),
            current.GameScene);
        bool legacyReleased = rainLegacyPlayStateField == null ||
            rainLegacyPlayStateField.GetValue(rain) == null;
        bool passed = returned && currentScene && legacyReleased;
        return new ScenarioResult(
            passed,
            "returned:" + returned + ",current_scene:" + currentScene +
                ",legacy_released:" + legacyReleased,
            "returned:True,current_scene:True,legacy_released:True");
    }

    private ScenarioResult ThunderstormExecute(
        MethodInfo method,
        object firstArgument)
    {
        return ThunderstormExecute(
            method,
            firstArgument,
            CreatePlayState(true, true, false));
    }

    private ScenarioResult ThunderstormExecute(
        MethodInfo method,
        object firstArgument,
        WeatherPlayStateFixture current)
    {
        WeatherPlayStateFixture supplied = CreatePlayState(false, true, false);
        recentPlayStateField.SetValue(null, current.PlayState);
        object thunderstorm = CreateThunderstorm();
        SetOptional(thunderLegacyPlayStateField, thunderstorm, null);
        WeatherPlayStateProbe.SuppressRainExecute = true;
        bool returned = (bool)Invoke(
            method,
            thunderstorm,
            new object[] { firstArgument, supplied.PlayState });
        WeatherPlayStateProbe.SuppressRainExecute = false;
        bool currentIndoor = (bool)thunderIndoorField.GetValue(thunderstorm);
        bool legacyReleased = thunderLegacyPlayStateField == null ||
            thunderLegacyPlayStateField.GetValue(thunderstorm) == null;
        bool passed = returned && currentIndoor && legacyReleased;
        return new ScenarioResult(
            passed,
            "returned:" + returned + ",current_indoor:" + currentIndoor +
                ",legacy_released:" + legacyReleased,
            "returned:True,current_indoor:True,legacy_released:True");
    }

    private object CreateThunderstorm()
    {
        object thunderstorm = NewUninitialized(thunderstormType);
        thunderPlayersAliveField.SetValue(thunderstorm, new bool[4]);
        thunderRainField.SetValue(thunderstorm, NewUninitialized(rainType));
        return thunderstorm;
    }

    private WeatherPlayStateFixture CreatePlayState(
        bool indoors,
        bool renderScene,
        bool navMesh)
    {
        object gameScene = NewUninitialized(gameSceneType);
        gameSceneIndoorField.SetValue(gameScene, indoors);
        object createdNavMesh = null;
        if (navMesh)
        {
            object levelModel = NewUninitialized(levelModelType);
            createdNavMesh = NewUninitialized(navMeshType);
            levelModelNavMeshField.SetValue(levelModel, createdNavMesh);
            gameSceneModelField.SetValue(gameScene, levelModel);
        }
        object level = NewUninitialized(levelType);
        levelCurrentSceneField.SetValue(level, gameScene);
        object playState = NewUninitialized(playStateType);
        playStateLevelField.SetValue(playState, level);
        if (renderScene)
        {
            object scene = NewUninitialized(polygonSceneType);
            Type cameraType = magicka.GetType("Magicka.Graphics.MagickCamera", true);
            polygonSceneCameraField.SetValue(scene, NewUninitialized(cameraType));
            gameStateSceneField.SetValue(playState, scene);
        }
        return new WeatherPlayStateFixture(
            playState,
            gameScene,
            createdNavMesh);
    }

    private HarmonyInstance InstallDependencyStubs(
        Type effectManagerType,
        Type achievementType)
    {
        MethodInfo isActive = RequireNamedMethod(
            effectManagerType,
            "IsActive",
            1);
        MethodInfo startEffect = RequireNamedMethod(
            effectManagerType,
            "StartEffect",
            4);
        MethodInfo updateEffect = RequireNamedMethod(
            effectManagerType,
            "UpdatePositionDirection",
            3);
        MethodInfo stopEffect = RequireNamedMethod(
            effectManagerType,
            "Stop",
            1);
        MethodInfo navNearest = RequireNamedMethod(navMeshType, "GetNearestPosition", 3);
        MethodInfo award = RequireNamedMethod(
            achievementType,
            "AwardAchievement",
            2);
        MethodInfo cueIsPlaying = RequirePropertyGetter(cueType, "IsPlaying");
        MethodInfo cueIsStopping = RequirePropertyGetter(cueType, "IsStopping");
        Type stopOptions = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Audio.AudioStopOptions");
        MethodInfo cueStop = cueType.GetMethod(
            "Stop",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { stopOptions },
            null);
        if (cueStop == null)
            throw new MissingMethodException(cueType.FullName, "Stop");

        Type effectReferenceType = startEffect.GetParameters()[3].ParameterType.GetElementType();
        HarmonyInstance instance = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-weather-play-state");
        instance.Patch(isActive, Prefix("IsActivePrefix"), null, null);
        instance.Patch(
            startEffect,
            GenericPrefix("StartEffectPrefix", effectReferenceType),
            null,
            null);
        instance.Patch(updateEffect, Prefix("UpdateEffectPrefix"), null, null);
        instance.Patch(
            stopEffect,
            GenericPrefix("StopEffectPrefix", effectReferenceType),
            null,
            null);
        instance.Patch(navNearest, Prefix("NavMeshPrefix"), null, null);
        instance.Patch(award, Prefix("AwardAchievementPrefix"), null, null);
        instance.Patch(cueIsPlaying, Prefix("CueIsPlayingPrefix"), null, null);
        instance.Patch(cueIsStopping, Prefix("CueIsStoppingPrefix"), null, null);
        instance.Patch(cueStop, Prefix("CueStopPrefix"), null, null);
        instance.Patch(rainVectorExecute, Prefix("RainExecutePrefix"), null, null);
        instance.Patch(rainOwnerExecute, Prefix("RainExecutePrefix"), null, null);
        return instance;
    }

    private float GetLight(object scene)
    {
        return Convert.ToSingle(
            gameSceneType.GetProperty("LightTargetIntensity").GetValue(scene, null));
    }

    private void SetLight(object scene, float value)
    {
        gameSceneType.GetProperty("LightTargetIntensity").SetValue(
            scene,
            value,
            null);
    }

    private static bool TryInvoke(
        MethodInfo method,
        object instance,
        object[] arguments,
        Type expectedStop)
    {
        try
        {
            method.Invoke(instance, arguments);
            return expectedStop == null;
        }
        catch (TargetInvocationException exception)
        {
            WeatherPlayStateProbe.LastInvocationError =
                exception.InnerException == null
                    ? exception.GetType().FullName
                    : exception.InnerException.GetType().FullName;
            return expectedStop != null && exception.InnerException != null &&
                expectedStop.IsInstanceOfType(exception.InnerException);
        }
    }

    private static object Invoke(
        MethodInfo method,
        object instance,
        object[] arguments)
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

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        Type[] parameters,
        Type returnType)
    {
        MethodInfo method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != returnType)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static MethodInfo RequireNamedMethod(
        Type type,
        string name,
        int parameterCount)
    {
        MethodInfo found = null;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != name ||
                methods[index].GetParameters().Length != parameterCount)
                continue;
            if (found != null)
                throw new InvalidOperationException(
                    "Multiple " + type.FullName + "." + name +
                    " methods matched.");
            found = methods[index];
        }
        if (found == null)
            throw new MissingMethodException(type.FullName, name);
        return found;
    }

    private static MethodInfo RequirePropertyGetter(Type type, string name)
    {
        PropertyInfo property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo getter = property == null ? null : property.GetGetMethod();
        if (getter == null)
            throw new MissingMethodException(type.FullName, "get_" + name);
        return getter;
    }

    private static FieldInfo OptionalField(
        Type type,
        string name,
        Type expectedType)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field != null && field.FieldType != expectedType)
            throw new MissingFieldException(type.FullName, name);
        return field;
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
                BindingFlags.NonPublic);
    }

    private static FieldInfo RequireField(
        Type type,
        string name,
        Type expectedType,
        BindingFlags flags)
    {
        FieldInfo field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != expectedType)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static FieldInfo RequireAnyField(
        Type type,
        string name,
        BindingFlags flags)
    {
        FieldInfo field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static FieldInfo RequireHierarchyField(
        Type type,
        string name,
        Type expectedType)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                continue;
            if (field.FieldType != expectedType)
                throw new MissingFieldException(current.FullName, name);
            return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static void SetOptional(FieldInfo field, object instance, object value)
    {
        if (field != null)
            field.SetValue(instance, value);
    }

    private static HarmonyMethod Prefix(string name)
    {
        return new HarmonyMethod(typeof(WeatherPlayStateProbe).GetMethod(name));
    }

    private static HarmonyMethod GenericPrefix(string name, Type argument)
    {
        return new HarmonyMethod(
            typeof(WeatherPlayStateProbe).GetMethod(name).MakeGenericMethod(
                new Type[] { argument }));
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

internal sealed class WeatherPlayStateFixture
{
    internal object PlayState { get; private set; }
    internal object GameScene { get; private set; }
    internal object NavMesh { get; private set; }

    internal WeatherPlayStateFixture(
        object playState,
        object gameScene,
        object navMesh)
    {
        PlayState = playState;
        GameScene = gameScene;
        NavMesh = navMesh;
    }
}

internal sealed class WeatherPlayStateStopException : Exception
{
}

public static class WeatherPlayStateProbe
{
    public static bool SuppressRainExecute;
    public static int UpdateEffectCalls;
    public static int StopEffectCalls;
    public static int CueStopCalls;
    public static object RecordedNavMesh;
    public static object AwardedPlayState;
    public static string LastInvocationError;

    public static void ResetCalls()
    {
        UpdateEffectCalls = 0;
        StopEffectCalls = 0;
        CueStopCalls = 0;
        AwardedPlayState = null;
    }

    public static bool IsActivePrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static bool StartEffectPrefix<T>(ref bool __result, ref T __3)
    {
        __result = true;
        __3 = default(T);
        return false;
    }

    public static bool UpdateEffectPrefix(ref bool __result)
    {
        UpdateEffectCalls++;
        __result = true;
        return false;
    }

    public static bool StopEffectPrefix<T>(ref T __0)
    {
        StopEffectCalls++;
        __0 = default(T);
        return false;
    }

    public static bool NavMeshPrefix(object __instance)
    {
        RecordedNavMesh = __instance;
        throw new WeatherPlayStateStopException();
    }

    public static bool AwardAchievementPrefix(object __0)
    {
        AwardedPlayState = __0;
        return false;
    }

    public static bool CueIsPlayingPrefix(ref bool __result)
    {
        __result = true;
        return false;
    }

    public static bool CueIsStoppingPrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static bool CueStopPrefix()
    {
        CueStopCalls++;
        return false;
    }

    public static bool RainExecutePrefix(ref bool __result)
    {
        if (!SuppressRainExecute)
            return true;
        __result = true;
        return false;
    }
}
