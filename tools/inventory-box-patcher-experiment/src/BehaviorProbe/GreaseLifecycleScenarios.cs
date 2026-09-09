using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class GreaseLifecycleScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        GreaseLifecycleHarness harness =
            new GreaseLifecycleHarness(magicka, runtimePatchEnabled);
        try
        {
            report.Add(
                "grease.play_state_release",
                harness.PlayStateRelease());
            report.Add(
                "grease.current_play_state",
                harness.CurrentPlayState());
            report.Add(
                "grease.cache_cleanup",
                harness.CacheCleanup(false));
            report.Add(
                "grease.cache_cleanup_idempotent",
                harness.CacheCleanup(true));
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class GreaseLifecycleHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-grease-lifecycle";

    private readonly bool runtimePatchEnabled;
    private readonly Assembly magicka;
    private readonly Type greaseType;
    private readonly Type greaseFieldType;
    private readonly Type playStateType;
    private readonly Type entityManagerType;
    private readonly Type entityType;
    private readonly Type ownerType;
    private readonly Type bodyType;
    private readonly Type vectorType;
    private readonly Type dataChannelType;
    private readonly MethodInfo execute;
    private readonly MethodInfo update;
    private readonly MethodInfo manualGreaseCleanup;
    private readonly MethodInfo manualFieldCleanup;
    private readonly FieldInfo greaseCache;
    private readonly FieldInfo fieldCache;
    private readonly FieldInfo greasePlayState;
    private readonly FieldInfo fieldPlayState;
    private readonly FieldInfo entityPlayState;
    private readonly FieldInfo greaseOwner;
    private readonly FieldInfo fieldOwner;
    private readonly FieldInfo animatedPart;
    private readonly FieldInfo hitListOwner;
    private readonly FieldInfo recentPlayState;
    private readonly FieldInfo ttl;
    private readonly FieldInfo audioSingleton;
    private readonly FieldInfo effectSingleton;
    private readonly FieldInfo spellSingleton;
    private readonly FieldInfo decalSingleton;
    private readonly FieldInfo networkSingleton;
    private readonly object originalGreaseCache;
    private readonly object originalFieldCache;
    private readonly object originalHitListOwner;
    private readonly object originalRecentPlayState;
    private readonly object originalAudioSingleton;
    private readonly object originalEffectSingleton;
    private readonly object originalSpellSingleton;
    private readonly object originalDecalSingleton;
    private readonly object originalNetworkSingleton;
    private readonly HarmonyInstance harmony;

    internal GreaseLifecycleHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.magicka = magicka;
        this.runtimePatchEnabled = runtimePatchEnabled;
        greaseType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease",
            true);
        greaseFieldType = greaseType.GetNestedType(
            "GreaseField",
            BindingFlags.Public | BindingFlags.NonPublic);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type caster = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        execute = greaseType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { caster, playStateType },
            null);
        update = FindMethod(greaseType, "Update", 2);
        dataChannelType = update.GetParameters()[0].ParameterType;
        if (greaseFieldType == null || execute == null ||
            execute.ReturnType != typeof(bool) ||
            update.ReturnType != typeof(void))
            throw new MissingMethodException(
                greaseType.FullName,
                "Grease behavior targets");

        greaseCache = RequireField(greaseType, "sCache");
        fieldCache = RequireField(greaseFieldType, "sCache");
        greasePlayState = greaseType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        fieldPlayState = greaseFieldType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        entityPlayState = RequireField(entityType, "mPlayState");
        greaseOwner = RequireField(greaseType, "mOwner");
        fieldOwner = RequireField(greaseFieldType, "mOwner");
        animatedPart = greaseFieldType.GetField(
            "mAnimatedLevelPart",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        hitListOwner = RequireField(greaseFieldType, "sHitListOwner");
        recentPlayState = RequireField(playStateType, "sRecentPlayState");
        ttl = RequireField(greaseType, "mTTL");
        Type audioManager = magicka.GetType("Magicka.Audio.AudioManager", true);
        Type effectManager = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        Type spellManager = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        Type decalManager = magicka.GetType(
            "Magicka.Graphics.DecalManager",
            true);
        Type networkManager = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        audioSingleton = RequireField(audioManager, "instance");
        effectSingleton = RequireField(effectManager, "mSingelton");
        spellSingleton = RequireField(spellManager, "mSingelton");
        decalSingleton = decalManager.GetField(
            "mSingelton",
            BindingFlags.Static | BindingFlags.NonPublic);
        networkSingleton = RequireField(networkManager, "sSingelton");
        originalGreaseCache = greaseCache.GetValue(null);
        originalFieldCache = fieldCache.GetValue(null);
        originalHitListOwner = hitListOwner.GetValue(null);
        originalRecentPlayState = recentPlayState.GetValue(null);
        originalAudioSingleton = audioSingleton.GetValue(null);
        originalEffectSingleton = effectSingleton.GetValue(null);
        originalSpellSingleton = spellSingleton.GetValue(null);
        originalDecalSingleton = decalSingleton == null
            ? null
            : decalSingleton.GetValue(null);
        originalNetworkSingleton = networkSingleton.GetValue(null);
        manualGreaseCleanup = FindCleanup(greaseType);
        manualFieldCleanup = FindCleanup(greaseFieldType);
        if ((manualGreaseCleanup == null) != (manualFieldCleanup == null))
            throw new InvalidOperationException(
                "Only part of the manual Grease cleanup contract is present.");

        harmony = InstallDependencyStubs(caster);
    }

    internal void Dispose()
    {
        GreaseLifecycleProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
        greaseCache.SetValue(null, originalGreaseCache);
        fieldCache.SetValue(null, originalFieldCache);
        hitListOwner.SetValue(null, originalHitListOwner);
        recentPlayState.SetValue(null, originalRecentPlayState);
        audioSingleton.SetValue(null, originalAudioSingleton);
        effectSingleton.SetValue(null, originalEffectSingleton);
        spellSingleton.SetValue(null, originalSpellSingleton);
        if (decalSingleton != null)
            decalSingleton.SetValue(null, originalDecalSingleton);
        networkSingleton.SetValue(null, originalNetworkSingleton);
    }

    internal ScenarioResult PlayStateRelease()
    {
        object playState = NewUninitialized(playStateType);
        object owner = CreateOwner(playState);
        object grease = NewUninitialized(greaseType);
        if (greasePlayState != null)
            greasePlayState.SetValue(grease, null);
        GreaseLifecycleProbe.Reset();
        GreaseLifecycleProbe.Enabled = true;
        bool result = (bool)Invoke(
            execute,
            grease,
            new object[] { owner, playState });
        GreaseLifecycleProbe.Enabled = false;

        bool retained = greasePlayState != null &&
            ReferenceEquals(greasePlayState.GetValue(grease), playState);
        bool ownerPreserved = ReferenceEquals(
            greaseOwner.GetValue(grease),
            owner);
        float time = Convert.ToSingle(ttl.GetValue(grease));
        bool passed = result && !retained && ownerPreserved &&
            Math.Abs(time - 0.5f) < 0.0001f &&
            GreaseLifecycleProbe.PlayCueCalls == 1 &&
            GreaseLifecycleProbe.StartEffectCalls == 1 &&
            GreaseLifecycleProbe.AddEffectCalls == 1;
        string actual = "result:" + result +
            ",state:" + (retained ? "retained" : "released") +
            ",owner:" + ownerPreserved +
            ",ttl:" + time +
            ",audio:" + GreaseLifecycleProbe.PlayCueCalls +
            ",effect:" + GreaseLifecycleProbe.StartEffectCalls +
            ",registered:" + GreaseLifecycleProbe.AddEffectCalls;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,state:released,owner:True,ttl:0.5," +
                "audio:1,effect:1,registered:1");
    }

    internal ScenarioResult CurrentPlayState()
    {
        GreaseManagerFixture currentManager = CreateManager();
        GreaseManagerFixture staleManager = CreateManager();
        object current = CreatePlayState(currentManager.Manager, true);
        object stale = CreatePlayState(staleManager.Manager, false);
        recentPlayState.SetValue(null, current);
        object owner = CreateOwner(current);
        object grease = NewUninitialized(greaseType);
        greaseOwner.SetValue(grease, owner);
        ttl.SetValue(grease, 0.41f);
        if (greasePlayState != null)
            greasePlayState.SetValue(grease, stale);
        ConfigureNetwork();
        GreaseLifecycleProbe.Reset();
        GreaseLifecycleProbe.Enabled = true;

        Exception failure = null;
        try
        {
            Invoke(
                update,
                grease,
                new object[]
                {
                    Enum.ToObject(dataChannelType, 0),
                    0.001f
                });
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        GreaseLifecycleProbe.Enabled = false;

        bool currentUsed =
            currentManager.IsOnlyCachedListReturnedAndCleared();
        bool staleUnused = staleManager.IsOriginalCacheUntouched();
        float time = Convert.ToSingle(ttl.GetValue(grease));
        bool passed = failure == null && currentUsed && staleUnused &&
            GreaseLifecycleProbe.UpdateEffectCalls == 1 &&
            Math.Abs(time - 0.409f) < 0.0001f;
        return new ScenarioResult(
            passed,
            "exception:" +
                (failure == null ? "none" : failure.GetType().FullName) +
                ",current_used:" + currentUsed +
                ",stale_unused:" + staleUnused +
                ",effect_updates:" +
                GreaseLifecycleProbe.UpdateEffectCalls +
                ",ttl:" + time,
            "exception:none,current_used:True,stale_unused:True," +
                "effect_updates:1,ttl:0.409");
    }

    internal ScenarioResult CacheCleanup(bool repeat)
    {
        object playState = NewUninitialized(playStateType);
        object owner = CreateOwner(playState);
        object part = animatedPart == null
            ? null
            : NewUninitialized(
                magicka.GetType("Magicka.Levels.AnimatedLevelPart", true));
        object grease = NewUninitialized(greaseType);
        object field = NewUninitialized(greaseFieldType);
        greaseOwner.SetValue(grease, owner);
        fieldOwner.SetValue(field, owner);
        if (animatedPart != null)
            animatedPart.SetValue(field, part);
        entityPlayState.SetValue(field, playState);
        if (greasePlayState != null)
            greasePlayState.SetValue(grease, playState);
        if (fieldPlayState != null)
            fieldPlayState.SetValue(field, playState);
        hitListOwner.SetValue(null, field);

        IList greases = NewList(greaseCache.FieldType, grease);
        IList fields = NewList(fieldCache.FieldType, field);
        greaseCache.SetValue(null, greases);
        fieldCache.SetValue(null, fields);
        GreaseLifecycleProbe.Enabled = true;
        InvokeCleanup();
        if (repeat)
            InvokeCleanup();
        GreaseLifecycleProbe.Enabled = false;

        bool cachesReleased = greases.Count == 0 && fields.Count == 0;
        bool greaseReleased = greaseOwner.GetValue(grease) == null &&
            (greasePlayState == null ||
                greasePlayState.GetValue(grease) == null);
        bool fieldReleased = fieldOwner.GetValue(field) == null &&
            (animatedPart == null || animatedPart.GetValue(field) == null) &&
            entityPlayState.GetValue(field) == null &&
            (fieldPlayState == null ||
                fieldPlayState.GetValue(field) == null);
        bool hitOwnerReleased = hitListOwner.GetValue(null) == null;
        bool passed = cachesReleased && greaseReleased &&
            fieldReleased && hitOwnerReleased;
        string actual = "caches:" +
            (cachesReleased ? "released" : "retained") +
            ",grease:" +
            (greaseReleased ? "released" : "retained") +
            ",field:" +
            (fieldReleased ? "released" : "retained") +
            ",hit_owner:" +
            (hitOwnerReleased ? "released" : "retained");
        return new ScenarioResult(
            passed,
            actual,
            "caches:released,grease:released,field:released," +
                "hit_owner:released");
    }

    private void InvokeCleanup()
    {
        if (manualGreaseCleanup != null)
        {
            Invoke(manualGreaseCleanup, null, new object[0]);
            Invoke(manualFieldCleanup, null, new object[0]);
            return;
        }
        if (!runtimePatchEnabled)
            return;
        Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
            .Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.GreaseLifecyclePatch",
                true);
        MethodInfo cleanup = patch.GetMethod(
            "CleanupCaches",
            BindingFlags.Static | BindingFlags.Public);
        Invoke(cleanup, null, new object[0]);
    }

    private object CreateOwner(object playState)
    {
        object owner = NewUninitialized(ownerType);
        object body = NewUninitialized(bodyType);
        bodyType.GetProperty("Position").SetValue(
            body,
            Activator.CreateInstance(vectorType),
            null);
        RuntimeReflection.WriteField(owner, "mBody", body);
        RuntimeReflection.WriteField(owner, "mPlayState", playState);
        return owner;
    }

    private GreaseManagerFixture CreateManager()
    {
        object manager = NewUninitialized(entityManagerType);
        Type entityList = typeof(System.Collections.Generic.List<>)
            .MakeGenericType(entityType);
        Type queryQueue = typeof(System.Collections.Generic.Queue<>)
            .MakeGenericType(entityList);
        object cachedList = Activator.CreateInstance(entityList);
        entityList.GetMethod("Add").Invoke(
            cachedList,
            new object[] { null });
        object queue = Activator.CreateInstance(queryQueue);
        queryQueue.GetMethod("Enqueue").Invoke(
            queue,
            new object[] { cachedList });
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
            Activator.CreateInstance(
                typeof(System.Collections.Generic.List<>).MakeGenericType(
                    shields.FieldType.GetGenericArguments()[0])));
        return new GreaseManagerFixture(
            manager,
            queue,
            queryQueue.GetMethod("Peek"),
            cachedList);
    }

    private object CreatePlayState(object manager, bool withScene)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        if (withScene)
        {
            Type levelType = magicka.GetType("Magicka.Levels.Level", true);
            Type sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
            object level = NewUninitialized(levelType);
            RuntimeReflection.WriteField(
                level,
                "mCurrentScene",
                NewUninitialized(sceneType));
            RuntimeReflection.WriteField(playState, "mLevel", level);
        }
        return playState;
    }

    private void ConfigureNetwork()
    {
        Type manager = magicka.GetType("Magicka.Network.NetworkManager", true);
        networkSingleton.SetValue(
            null,
            NewUninitialized(manager));
    }

    private HarmonyInstance InstallDependencyStubs(Type caster)
    {
        Type specialAbility = greaseType.BaseType;
        Type audioManager = magicka.GetType("Magicka.Audio.AudioManager", true);
        Type effectManager = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        Type spellManager = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        Type scene = magicka.GetType("Magicka.Levels.GameScene", true);
        Type decalManager = magicka.GetType(
            "Magicka.Graphics.DecalManager",
            true);

        audioSingleton.SetValue(
            null,
            NewUninitialized(audioManager));
        effectSingleton.SetValue(
            null,
            NewUninitialized(effectManager));
        spellSingleton.SetValue(
            null,
            NewUninitialized(spellManager));
        if (decalSingleton != null)
            decalSingleton.SetValue(null, NewUninitialized(decalManager));

        MethodInfo baseExecute = specialAbility.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { caster, playStateType },
            null);
        MethodInfo playCue = FindMethod(audioManager, "PlayCue", 3);
        MethodInfo startEffect = FindMethod(effectManager, "StartEffect", 4);
        MethodInfo updateEffect = FindMethod(
            effectManager,
            "UpdatePositionDirection",
            3);
        MethodInfo stopEffect = FindMethod(effectManager, "Stop", 1);
        MethodInfo addEffect = FindMethod(spellManager, "AddSpellEffect", 1);
        MethodInfo segmentIntersect = FindMethod(scene, "SegmentIntersect", 4);

        HarmonyInstance result = HarmonyInstance.Create(HarmonyOwner);
        result.Patch(baseExecute, Prefix("BaseExecutePrefix"), null, null);
        result.Patch(
            playCue,
            GenericPrefix("PlayCuePrefix", playCue.ReturnType),
            null,
            null);
        Type effectReference = startEffect.GetParameters()[3]
            .ParameterType.GetElementType();
        result.Patch(
            startEffect,
            GenericPrefix("StartEffectPrefix", effectReference),
            null,
            null);
        result.Patch(updateEffect, Prefix("UpdateEffectPrefix"), null, null);
        result.Patch(stopEffect, Prefix("StopEffectPrefix"), null, null);
        result.Patch(addEffect, Prefix("AddEffectPrefix"), null, null);
        Type vector = segmentIntersect.GetParameters()[1]
            .ParameterType.GetElementType();
        result.Patch(
            segmentIntersect,
            GenericPrefix("SegmentIntersectPrefix", vector),
            null,
            null);

        MethodInfo getDecal = FindOptionalMethod(
            decalManager,
            "GetDecalTTL",
            2);
        MethodInfo setDecal = FindOptionalMethod(
            decalManager,
            "SetDecalTTL",
            2);
        if (getDecal != null)
            result.Patch(getDecal, Prefix("GetDecalPrefix"), null, null);
        if (setDecal != null)
            result.Patch(setDecal, Prefix("SetDecalPrefix"), null, null);
        return result;
    }

    private static IList NewList(Type listType, object value)
    {
        IList list = (IList)Activator.CreateInstance(listType);
        list.Add(value);
        return list;
    }

    private static MethodInfo FindCleanup(Type type)
    {
        return type.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
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

    private static MethodInfo FindMethod(
        Type type,
        string name,
        int parameterCount)
    {
        MethodInfo method = FindOptionalMethod(type, name, parameterCount);
        if (method == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static MethodInfo FindOptionalMethod(
        Type type,
        string name,
        int parameterCount)
    {
        MethodInfo found = null;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != name ||
                methods[index].GetParameters().Length != parameterCount ||
                methods[index].IsGenericMethodDefinition)
                continue;
            if (found != null)
                throw new AmbiguousMatchException(type.FullName + "." + name);
            found = methods[index];
        }
        return found;
    }

    private static HarmonyMethod Prefix(string name)
    {
        return new HarmonyMethod(
            typeof(GreaseLifecycleProbe).GetMethod(name));
    }

    private static HarmonyMethod GenericPrefix(string name, Type argument)
    {
        return new HarmonyMethod(
            typeof(GreaseLifecycleProbe).GetMethod(name).MakeGenericMethod(
                new Type[] { argument }));
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

internal sealed class GreaseManagerFixture
{
    private readonly object queue;
    private readonly MethodInfo peek;
    private readonly object cachedList;

    internal object Manager { get; private set; }

    internal GreaseManagerFixture(
        object manager,
        object queue,
        MethodInfo peek,
        object cachedList)
    {
        Manager = manager;
        this.queue = queue;
        this.peek = peek;
        this.cachedList = cachedList;
    }

    internal bool IsOnlyCachedListReturnedAndCleared()
    {
        return ((ICollection)queue).Count == 1 &&
            ReferenceEquals(peek.Invoke(queue, null), cachedList) &&
            ((ICollection)cachedList).Count == 0;
    }

    internal bool IsOriginalCacheUntouched()
    {
        return ((ICollection)queue).Count == 1 &&
            ReferenceEquals(peek.Invoke(queue, null), cachedList) &&
            ((ICollection)cachedList).Count == 1;
    }
}

public static class GreaseLifecycleProbe
{
    public static bool Enabled;
    public static int PlayCueCalls;
    public static int StartEffectCalls;
    public static int UpdateEffectCalls;
    public static int StopEffectCalls;
    public static int AddEffectCalls;

    public static void Reset()
    {
        Enabled = false;
        PlayCueCalls = 0;
        StartEffectCalls = 0;
        UpdateEffectCalls = 0;
        StopEffectCalls = 0;
        AddEffectCalls = 0;
    }

    public static bool BaseExecutePrefix(ref bool __result)
    {
        if (!Enabled)
            return true;
        __result = true;
        return false;
    }

    public static bool PlayCuePrefix<TResult>(ref TResult __result)
    {
        if (!Enabled)
            return true;
        PlayCueCalls++;
        __result = default(TResult);
        return false;
    }

    public static bool StartEffectPrefix<TReference>(
        ref TReference oRef,
        ref bool __result)
    {
        if (!Enabled)
            return true;
        StartEffectCalls++;
        oRef = default(TReference);
        __result = true;
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

    public static bool StopEffectPrefix()
    {
        if (!Enabled)
            return true;
        StopEffectCalls++;
        return false;
    }

    public static bool AddEffectPrefix()
    {
        if (!Enabled)
            return true;
        AddEffectCalls++;
        return false;
    }

    public static bool SegmentIntersectPrefix<TVector>(
        ref float oFrac,
        ref TVector oPos,
        ref TVector oNrm,
        ref bool __result)
    {
        if (!Enabled)
            return true;
        oFrac = 0f;
        oPos = default(TVector);
        oNrm = default(TVector);
        __result = false;
        return false;
    }

    public static bool GetDecalPrefix(ref float oTTL, ref bool __result)
    {
        oTTL = 0f;
        __result = true;
        return false;
    }

    public static bool SetDecalPrefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}
