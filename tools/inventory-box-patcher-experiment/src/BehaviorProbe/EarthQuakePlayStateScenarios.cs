using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class EarthQuakePlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        EarthQuakePlayStateHarness.InstallProbes(magicka);
        EarthQuakePlayStateHarness harness =
            new EarthQuakePlayStateHarness(magicka);
        try
        {
            report.Add(
                "earthquake.execute_current_scene",
                harness.ExecuteCurrentScene());
            report.Add(
                "earthquake.current_camera",
                harness.CurrentCamera());
            report.Add(
                "earthquake.current_entity_manager",
                harness.CurrentEntityManager());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class EarthQuakePlayStateHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-earthquake";

    private readonly Type earthQuakeType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type gameSceneType;
    private readonly Type polygonSceneType;
    private readonly Type cameraType;
    private readonly Type entityManagerType;
    private readonly Type entityType;
    private readonly Type vectorType;
    private readonly MethodInfo execute;
    private readonly MethodInfo newQuake;
    private readonly MethodInfo quake;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo effectManagerSingletonField;
    private readonly FieldInfo audioManagerSingletonField;
    private readonly object originalRecentPlayState;
    private readonly object originalEffectManager;
    private readonly object originalAudioManager;
    private readonly HarmonyInstance harmony;

    internal EarthQuakePlayStateHarness(Assembly magicka)
    {
        earthQuakeType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.EarthQuake",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        gameSceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        polygonSceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        cameraType = magicka.GetType(
            "Magicka.Graphics.MagickCamera",
            true);
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        execute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { vectorType, playStateType },
            typeof(bool));
        newQuake = RequireMethod(
            "NewQuake",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { vectorType.MakeByRefType(), typeof(float) },
            typeof(void));
        quake = RequireMethod(
            "Quake",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            new Type[] { vectorType.MakeByRefType(), typeof(float) },
            typeof(void));
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        legacyPlayStateField = earthQuakeType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        Type effectManager = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        Type audioManager = magicka.GetType(
            "Magicka.Audio.AudioManager",
            true);
        effectManagerSingletonField = RuntimeReflection.RequireField(
            effectManager,
            "mSingelton");
        audioManagerSingletonField = RuntimeReflection.RequireField(
            audioManager,
            "instance");
        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalEffectManager = effectManagerSingletonField.GetValue(null);
        originalAudioManager = audioManagerSingletonField.GetValue(null);
        effectManagerSingletonField.SetValue(
            null,
            NewUninitialized(effectManager));
        audioManagerSingletonField.SetValue(
            null,
            NewUninitialized(audioManager));
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        EarthQuakePlayStateProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        effectManagerSingletonField.SetValue(null, originalEffectManager);
        audioManagerSingletonField.SetValue(null, originalAudioManager);
    }

    internal ScenarioResult ExecuteCurrentScene()
    {
        object suppliedScene = NewUninitialized(gameSceneType);
        object currentScene = NewUninitialized(gameSceneType);
        object supplied = NewLevelPlayState(suppliedScene);
        object current = NewLevelPlayState(currentScene);
        recentPlayStateField.SetValue(null, current);
        object effect = NewEffect();
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(effect, null);

        EarthQuakePlayStateProbe.Reset();
        EarthQuakePlayStateProbe.Enabled = true;
        bool returned = (bool)Invoke(
            execute,
            effect,
            new object[] { Activator.CreateInstance(vectorType), supplied });
        EarthQuakePlayStateProbe.Enabled = false;

        bool released = legacyPlayStateField == null ||
            legacyPlayStateField.GetValue(effect) == null;
        bool currentUsed = ReferenceEquals(
            EarthQuakePlayStateProbe.SegmentScene,
            currentScene);
        bool passed = !returned && released && currentUsed &&
            EarthQuakePlayStateProbe.SegmentCalls == 1;
        string actual = "returned:" + returned +
            ",released:" + released +
            ",scene:" + (currentUsed ? "current" : "supplied") +
            ",calls:" + EarthQuakePlayStateProbe.SegmentCalls;
        return new ScenarioResult(
            passed,
            actual,
            "returned:False,released:True,scene:current,calls:1");
    }

    internal ScenarioResult CurrentCamera()
    {
        object staleCamera = NewUninitialized(cameraType);
        object currentCamera = NewUninitialized(cameraType);
        object stale = NewCameraPlayState(staleCamera);
        object current = NewCameraPlayState(currentCamera);
        recentPlayStateField.SetValue(null, current);
        object effect = NewEffect();
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(effect, stale);
        object position = Activator.CreateInstance(vectorType);

        EarthQuakePlayStateProbe.Reset();
        EarthQuakePlayStateProbe.Enabled = true;
        Invoke(newQuake, effect, new object[] { position, 1.25f });
        EarthQuakePlayStateProbe.Enabled = false;

        bool currentUsed = ReferenceEquals(
            EarthQuakePlayStateProbe.Camera,
            currentCamera);
        bool passed = currentUsed && EarthQuakePlayStateProbe.CameraCalls == 1;
        return new ScenarioResult(
            passed,
            "calls:" + EarthQuakePlayStateProbe.CameraCalls +
                ",camera:" + (currentUsed ? "current" : "stale"),
            "calls:1,camera:current");
    }

    internal ScenarioResult CurrentEntityManager()
    {
        object staleManager = NewUninitialized(entityManagerType);
        object currentManager = NewUninitialized(entityManagerType);
        object stale = NewManagerPlayState(staleManager);
        object current = NewManagerPlayState(currentManager);
        recentPlayStateField.SetValue(null, current);
        object effect = NewEffect();
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(effect, stale);
        RuntimeReflection.WriteField(
            effect,
            "mQuakeEmitter",
            NewUninitialized(
                RuntimeReflection.FindLoadedType(
                    "Microsoft.Xna.Framework.Audio.AudioEmitter")));
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(
            entityType);
        EarthQuakePlayStateProbe.EmptyEntities =
            Activator.CreateInstance(listType);

        EarthQuakePlayStateProbe.ResetCounts();
        EarthQuakePlayStateProbe.Enabled = true;
        bool reachedTail = false;
        try
        {
            Invoke(
                quake,
                effect,
                new object[] { Activator.CreateInstance(vectorType), 10f });
        }
        catch (EarthQuakeTailReachedException)
        {
            reachedTail = true;
        }
        finally
        {
            EarthQuakePlayStateProbe.Enabled = false;
        }

        bool queryCurrent = ReferenceEquals(
            EarthQuakePlayStateProbe.QueryManager,
            currentManager);
        bool returnCurrent = ReferenceEquals(
            EarthQuakePlayStateProbe.ReturnManager,
            currentManager);
        bool passed = reachedTail && queryCurrent && returnCurrent &&
            EarthQuakePlayStateProbe.QueryCalls == 1 &&
            EarthQuakePlayStateProbe.ReturnCalls == 1;
        string actual = "tail:" + reachedTail +
            ",query:" + (queryCurrent ? "current" : "stale") +
            ",return:" + (returnCurrent ? "current" : "stale") +
            ",query_calls:" + EarthQuakePlayStateProbe.QueryCalls +
            ",return_calls:" + EarthQuakePlayStateProbe.ReturnCalls;
        return new ScenarioResult(
            passed,
            actual,
            "tail:True,query:current,return:current,query_calls:1," +
                "return_calls:1");
    }

    private object NewEffect()
    {
        object effect = NewUninitialized(earthQuakeType);
        RuntimeReflection.WriteField(effect, "mTTL", 0f);
        return effect;
    }

    private object NewLevelPlayState(object scene)
    {
        object level = NewUninitialized(levelType);
        RuntimeReflection.WriteField(level, "mCurrentScene", scene);
        object state = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(state, "mLevel", level);
        return state;
    }

    private object NewCameraPlayState(object camera)
    {
        object scene = NewUninitialized(polygonSceneType);
        RuntimeReflection.WriteField(scene, "mCamera", camera);
        object state = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(state, "mScene", scene);
        return state;
    }

    private object NewManagerPlayState(object manager)
    {
        object state = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(state, "mEntityManager", manager);
        return state;
    }

    private MethodInfo RequireMethod(
        string name,
        BindingFlags flags,
        Type[] parameters,
        Type returnType)
    {
        MethodInfo method = earthQuakeType.GetMethod(
            name,
            flags,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != returnType)
            throw new MissingMethodException(earthQuakeType.FullName, name);
        return method;
    }

    internal static void InstallProbes(Assembly magicka)
    {
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type segment = RuntimeReflection.FindLoadedType("JigLibX.Geometry.Segment");
        Type gameScene = magicka.GetType("Magicka.Levels.GameScene", true);
        Type camera = magicka.GetType(
            "Magicka.Graphics.MagickCamera",
            true);
        Type manager = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        Type entity = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        Type list = typeof(System.Collections.Generic.List<>).MakeGenericType(entity);
        Type effectManager = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        Type visualReference = magicka.GetType(
            "Magicka.Graphics.VisualEffectReference",
            true);
        Type audioManager = magicka.GetType("Magicka.Audio.AudioManager", true);
        Type banks = magicka.GetType("Magicka.Audio.Banks", true);
        Type emitter = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Audio.AudioEmitter");
        HarmonyInstance harmony = HarmonyInstance.Create(HarmonyOwner);

        PatchGeneric(
            harmony,
            gameScene.GetMethod(
                "SegmentIntersect",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] {
                    typeof(float).MakeByRefType(), vector.MakeByRefType(),
                    vector.MakeByRefType(), segment
                },
                null),
            "SegmentPrefix",
            vector);
        Patch(
            harmony,
            camera.GetMethod(
                "CameraShake",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { vector, typeof(float), typeof(float) },
                null),
            "CameraPrefix");
        PatchGeneric(
            harmony,
            manager.GetMethod(
                "GetEntities",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] {
                    vector, typeof(float), typeof(bool), typeof(bool)
                },
                null),
            "GetEntitiesPrefix",
            list);
        Patch(
            harmony,
            manager.GetMethod(
                "ReturnEntityList",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { list },
                null),
            "ReturnEntitiesPrefix");
        Patch(
            harmony,
            effectManager.GetMethod(
                "IsActive",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { visualReference.MakeByRefType() },
                null),
            "IsActivePrefix");
        Patch(
            harmony,
            audioManager.GetMethod(
                "PlayCue",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { banks, typeof(int), emitter },
                null),
            "PlayCuePrefix");
    }

    private static void Patch(
        HarmonyInstance harmony,
        MethodInfo target,
        string prefix)
    {
        if (target == null)
            throw new MissingMethodException(prefix);
        harmony.Patch(
            target,
            new HarmonyMethod(
                typeof(EarthQuakePlayStateProbe).GetMethod(prefix)),
            null,
            null);
    }

    private static void PatchGeneric(
        HarmonyInstance harmony,
        MethodInfo target,
        string prefix,
        Type argument)
    {
        if (target == null)
            throw new MissingMethodException(prefix);
        MethodInfo probe = typeof(EarthQuakePlayStateProbe).GetMethod(
            prefix).MakeGenericMethod(argument);
        harmony.Patch(target, new HarmonyMethod(probe), null, null);
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

public sealed class EarthQuakeTailReachedException : Exception
{
}

public static class EarthQuakePlayStateProbe
{
    public static bool Enabled;
    public static int SegmentCalls;
    public static object SegmentScene;
    public static int CameraCalls;
    public static object Camera;
    public static int QueryCalls;
    public static object QueryManager;
    public static int ReturnCalls;
    public static object ReturnManager;
    public static object EmptyEntities;

    public static void Reset()
    {
        ResetCounts();
        SegmentCalls = 0;
        SegmentScene = null;
        CameraCalls = 0;
        Camera = null;
    }

    public static void ResetCounts()
    {
        QueryCalls = 0;
        QueryManager = null;
        ReturnCalls = 0;
        ReturnManager = null;
    }

    public static bool SegmentPrefix<TVector>(
        object __instance,
        ref bool __result,
        ref float __0,
        ref TVector __1,
        ref TVector __2)
    {
        if (!Enabled)
            return true;
        SegmentCalls++;
        SegmentScene = __instance;
        __result = false;
        __0 = 0f;
        __1 = default(TVector);
        __2 = default(TVector);
        return false;
    }

    public static bool CameraPrefix(object __instance)
    {
        if (!Enabled)
            return true;
        CameraCalls++;
        Camera = __instance;
        return false;
    }

    public static bool GetEntitiesPrefix<TList>(
        object __instance,
        ref TList __result)
    {
        if (!Enabled)
            return true;
        QueryCalls++;
        QueryManager = __instance;
        __result = (TList)EmptyEntities;
        return false;
    }

    public static bool ReturnEntitiesPrefix(object __instance)
    {
        if (!Enabled)
            return true;
        ReturnCalls++;
        ReturnManager = __instance;
        return false;
    }

    public static bool IsActivePrefix(ref bool __result)
    {
        if (!Enabled)
            return true;
        __result = false;
        return false;
    }

    public static bool PlayCuePrefix()
    {
        if (!Enabled)
            return true;
        throw new EarthQuakeTailReachedException();
    }
}
