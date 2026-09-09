using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class UnderGroundAttackScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        UnderGroundAttackHarness harness =
            new UnderGroundAttackHarness(magicka);
        try
        {
            report.Add(
                "underground_attack.play_state_release",
                harness.PlayStateRelease());
            report.Add(
                "underground_attack.initialize_current_play_state",
                harness.InitializeCurrentPlayState());
            report.Add(
                "underground_attack.update_current_play_state",
                harness.UpdateCurrentPlayState());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class UnderGroundAttackHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-underground-attack";

    private readonly Assembly magicka;
    private readonly Type attackType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type sceneType;
    private readonly Type entityType;
    private readonly Type entityManagerType;
    private readonly Type ownerType;
    private readonly Type vector3Type;
    private readonly Type vector2Type;
    private readonly Type dataChannelType;
    private readonly ConstructorInfo constructor;
    private readonly MethodInfo initialize;
    private readonly MethodInfo update;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo cacheField;
    private readonly FieldInfo rangeField;
    private readonly FieldInfo effectManagerSingletonField;
    private readonly object originalRecentPlayState;
    private readonly object originalCache;
    private readonly object originalEffectManager;
    private readonly HarmonyInstance harmony;

    internal UnderGroundAttackHarness(Assembly magicka)
    {
        this.magicka = magicka;
        attackType = magicka.GetType(
            "Magicka.GameLogic.Spells.UnderGroundAttack",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        vector3Type = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        vector2Type = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector2");
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");

        constructor = attackType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playStateType },
            null);
        initialize = FindInitialize();
        update = attackType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (constructor == null || initialize == null || update == null)
            throw new MissingMethodException(
                "UnderGroundAttack behavior targets are incomplete.");

        playStateField = attackType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        recentPlayStateField = RequireField(
            playStateType,
            "sRecentPlayState");
        cacheField = RequireField(attackType, "sCache");
        rangeField = RequireField(attackType, "mRange");

        Type effectManager = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        effectManagerSingletonField = RequireField(
            effectManager,
            "mSingelton");
        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalCache = cacheField.GetValue(null);
        originalEffectManager = effectManagerSingletonField.GetValue(null);
        effectManagerSingletonField.SetValue(
            null,
            NewUninitialized(effectManager));

        harmony = InstallStubs(effectManager);
    }

    internal void Dispose()
    {
        UnderGroundAttackProbe.Reset();
        harmony.UnpatchAll(HarmonyOwner);
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        cacheField.SetValue(null, originalCache);
        effectManagerSingletonField.SetValue(null, originalEffectManager);
    }

    internal ScenarioResult PlayStateRelease()
    {
        object supplied = NewPlayState(null, false);
        object attack = NewAttack(supplied);
        bool retained = playStateField != null &&
            ReferenceEquals(playStateField.GetValue(attack), supplied);
        return new ScenarioResult(
            !retained,
            "play_state:" + (retained ? "retained" : "released"),
            "play_state:released");
    }

    internal ScenarioResult InitializeCurrentPlayState()
    {
        UnderGroundAttackManagerFixture manager = NewManager();
        object scene = NewUninitialized(sceneType);
        object current = NewPlayState(manager.Manager, true, scene);
        object stale = NewPlayState(null, false);
        object owner = NewUninitialized(ownerType);
        object attack = NewAttack(stale);
        recentPlayStateField.SetValue(null, current);
        cacheField.SetValue(
            null,
            Activator.CreateInstance(cacheField.FieldType));
        UnderGroundAttackProbe.Reset();
        UnderGroundAttackProbe.Enabled = true;
        UnderGroundAttackProbe.Mode = 1;

        Exception failure = null;
        try
        {
            Invoke(
                initialize,
                attack,
                new object[]
                {
                    Activator.CreateInstance(vector3Type),
                    Activator.CreateInstance(vector2Type),
                    owner,
                    0d,
                    1f,
                    Activator.CreateInstance(
                        initialize.GetParameters()[5].ParameterType),
                    false
                });
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        UnderGroundAttackProbe.Enabled = false;

        IList cache = (IList)cacheField.GetValue(null);
        bool cached = cache.Count == 1 &&
            ReferenceEquals(cache[0], attack);
        bool currentScene =
            UnderGroundAttackProbe.SegmentCalls == 1 &&
            ReferenceEquals(
                UnderGroundAttackProbe.FirstScene,
                scene);
        bool passed = failure == null && cached && currentScene;
        return new ScenarioResult(
            passed,
            "exception:" +
                (failure == null ? "none" : failure.GetType().FullName) +
                ",current_scene:" + currentScene +
                ",cached:" + cached,
            "exception:none,current_scene:True,cached:True");
    }

    internal ScenarioResult UpdateCurrentPlayState()
    {
        UnderGroundAttackManagerFixture manager = NewManager();
        object scene = NewUninitialized(sceneType);
        object current = NewPlayState(manager.Manager, true, scene);
        object stale = NewPlayState(null, false);
        object attack = NewAttack(stale);
        recentPlayStateField.SetValue(null, current);
        rangeField.SetValue(attack, 1f);

        object normal = Activator.CreateInstance(vector3Type);
        vector3Type.GetField("Y").SetValue(normal, 1f);
        UnderGroundAttackProbe.Reset();
        UnderGroundAttackProbe.Normal = normal;
        UnderGroundAttackProbe.Enabled = true;
        UnderGroundAttackProbe.Mode = 2;

        Exception failure = null;
        try
        {
            Invoke(
                update,
                attack,
                new object[]
                {
                    Enum.ToObject(dataChannelType, 0),
                    0.01f
                });
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        UnderGroundAttackProbe.Enabled = false;

        bool currentScene =
            UnderGroundAttackProbe.SegmentCalls == 2 &&
            ReferenceEquals(
                UnderGroundAttackProbe.FirstScene,
                scene) &&
            ReferenceEquals(
                UnderGroundAttackProbe.SecondScene,
                scene);
        bool currentManager = manager.IsQueryListReturned();
        bool effectsUpdated =
            UnderGroundAttackProbe.UpdateEffectCalls == 5;
        bool passed = failure == null && currentScene &&
            currentManager && effectsUpdated;
        return new ScenarioResult(
            passed,
            "exception:" +
                (failure == null ? "none" : failure.GetType().FullName) +
                ",current_scene:" + currentScene +
                ",current_manager:" + currentManager +
                ",effect_updates:" +
                UnderGroundAttackProbe.UpdateEffectCalls,
            "exception:none,current_scene:True,current_manager:True," +
                "effect_updates:5");
    }

    private MethodInfo FindInitialize()
    {
        MethodInfo found = null;
        MethodInfo[] methods = attackType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            MethodInfo candidate = methods[index];
            ParameterInfo[] parameters = candidate.GetParameters();
            if (candidate.Name != "Initialize" ||
                candidate.ReturnType != typeof(void) ||
                parameters.Length != 7 ||
                parameters[0].ParameterType != vector3Type.MakeByRefType() ||
                parameters[1].ParameterType != vector2Type.MakeByRefType() ||
                parameters[3].ParameterType != typeof(double) ||
                parameters[4].ParameterType != typeof(float) ||
                parameters[6].ParameterType != typeof(bool))
                continue;
            if (found != null)
                throw new AmbiguousMatchException(
                    attackType.FullName + ".Initialize");
            found = candidate;
        }
        return found;
    }

    private object NewAttack(object playState)
    {
        try
        {
            return constructor.Invoke(new object[] { playState });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private object NewPlayState(object manager, bool withScene)
    {
        return NewPlayState(manager, withScene, null);
    }

    private object NewPlayState(
        object manager,
        bool withScene,
        object suppliedScene)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        if (withScene)
        {
            object level = NewUninitialized(levelType);
            RuntimeReflection.WriteField(
                level,
                "mCurrentScene",
                suppliedScene ?? NewUninitialized(sceneType));
            RuntimeReflection.WriteField(playState, "mLevel", level);
        }
        return playState;
    }

    private UnderGroundAttackManagerFixture NewManager()
    {
        object manager = NewUninitialized(entityManagerType);
        Type entityList = typeof(System.Collections.Generic.List<>)
            .MakeGenericType(entityType);
        Type queryQueue = typeof(System.Collections.Generic.Queue<>)
            .MakeGenericType(entityList);
        object list = Activator.CreateInstance(entityList);
        entityList.GetMethod("Add").Invoke(list, new object[] { null });
        object queue = Activator.CreateInstance(queryQueue);
        queryQueue.GetMethod("Enqueue").Invoke(queue, new object[] { list });
        RuntimeReflection.WriteField(manager, "mQuaryLists", queue);

        Array grid = Array.CreateInstance(entityList, 16, 16);
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
                grid.SetValue(Activator.CreateInstance(entityList), x, z);
        }
        RuntimeReflection.WriteField(manager, "mQuadGrid", grid);

        FieldInfo shields = RequireField(entityManagerType, "mShields");
        shields.SetValue(
            manager,
            Activator.CreateInstance(shields.FieldType));
        return new UnderGroundAttackManagerFixture(
            queue,
            queryQueue.GetMethod("Peek"),
            list,
            manager);
    }

    private HarmonyInstance InstallStubs(Type effectManager)
    {
        MethodInfo segment = sceneType.GetMethod(
            "SegmentIntersect",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            FindSegmentSignature(),
            null);
        MethodInfo updateEffect = null;
        MethodInfo[] methods = effectManager.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "UpdatePositionDirection" &&
                methods[index].GetParameters().Length == 3)
                updateEffect = methods[index];
        }
        if (segment == null || updateEffect == null)
            throw new MissingMethodException(
                "UnderGroundAttack dependency probes are incomplete.");

        HarmonyInstance instance = HarmonyInstance.Create(HarmonyOwner);
        instance.Patch(
            segment,
            new HarmonyMethod(
                typeof(UnderGroundAttackProbe).GetMethod(
                    "SegmentIntersectPrefix").MakeGenericMethod(
                        new Type[] { vector3Type })),
            null,
            null);
        instance.Patch(
            updateEffect,
            new HarmonyMethod(
                typeof(UnderGroundAttackProbe).GetMethod(
                    "UpdateEffectPrefix")),
            null,
            null);
        return instance;
    }

    private Type[] FindSegmentSignature()
    {
        Type segment = RuntimeReflection.FindLoadedType(
            "JigLibX.Geometry.Segment");
        return new Type[]
        {
            typeof(float).MakeByRefType(),
            vector3Type.MakeByRefType(),
            vector3Type.MakeByRefType(),
            segment
        };
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null)
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

internal sealed class UnderGroundAttackManagerFixture
{
    private readonly object queue;
    private readonly MethodInfo peek;
    private readonly object list;

    internal object Manager { get; private set; }

    internal UnderGroundAttackManagerFixture(
        object queue,
        MethodInfo peek,
        object list,
        object manager)
    {
        this.queue = queue;
        this.peek = peek;
        this.list = list;
        Manager = manager;
    }

    internal bool IsQueryListReturned()
    {
        return ((ICollection)queue).Count == 1 &&
            ReferenceEquals(peek.Invoke(queue, null), list) &&
            ((ICollection)list).Count == 0;
    }
}

public static class UnderGroundAttackProbe
{
    public static bool Enabled;
    public static int Mode;
    public static int SegmentCalls;
    public static int UpdateEffectCalls;
    public static object Normal;
    public static object FirstScene;
    public static object SecondScene;

    public static void Reset()
    {
        Enabled = false;
        Mode = 0;
        SegmentCalls = 0;
        UpdateEffectCalls = 0;
        Normal = null;
        FirstScene = null;
        SecondScene = null;
    }

    public static bool SegmentIntersectPrefix<TVector>(
        object __instance,
        ref float oFrac,
        ref TVector oPos,
        ref TVector oNrm,
        ref bool __result)
    {
        if (!Enabled)
            return true;
        SegmentCalls++;
        if (SegmentCalls == 1)
            FirstScene = __instance;
        else if (SegmentCalls == 2)
            SecondScene = __instance;
        oFrac = 0f;
        oPos = default(TVector);
        oNrm = Normal == null ? default(TVector) : (TVector)Normal;
        __result = Mode == 2 && SegmentCalls == 1;
        return false;
    }

    public static bool UpdateEffectPrefix(ref bool __result)
    {
        if (!Enabled)
            return true;
        UpdateEffectCalls++;
        __result = true;
        return false;
    }
}
