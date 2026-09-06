using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class ChargeAbilityScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ChargeAbilityHarness harness = new ChargeAbilityHarness(magicka);
        try
        {
            report.Add(
                "homing_charge.execute_release",
                harness.ExecuteRelease(false));
            report.Add(
                "stop_charge.execute_release",
                harness.ExecuteRelease(true));
            report.Add(
                "homing_charge.current_query_manager",
                harness.HomingCurrentQueryManager());
            report.Add(
                "stop_charge.current_play_state",
                harness.StopCurrentPlayState());
            report.Add(
                "stop_charge.non_triggering_update",
                harness.StopNonTriggeringUpdate());
            report.Add(
                "charge_abilities.level_dispose",
                harness.CacheRelease());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class ChargeAbilityHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-charge-abilities";

    private readonly Assembly magicka;
    private readonly Type characterBodyType;
    private readonly Type entityManagerType;
    private readonly Type entityType;
    private readonly Type ownerImplementationType;
    private readonly Type playStateType;
    private readonly Type vectorType;
    private readonly Type dataChannelType;
    private readonly FieldInfo recentPlayStateField;
    private readonly MethodInfo playStateDispose;
    private readonly ChargeAbilityFixture homing;
    private readonly ChargeAbilityFixture stop;
    private readonly HarmonyInstance harmony;

    internal ChargeAbilityHarness(Assembly magicka)
    {
        this.magicka = magicka;
        characterBodyType = magicka.GetType("Magicka.Physics.CharacterBody", true);
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        ownerImplementationType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        homing = new ChargeAbilityFixture(
            magicka,
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.HomingCharge",
            ownerType,
            playStateType);
        stop = new ChargeAbilityFixture(
            magicka,
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.StopCharge",
            ownerType,
            playStateType);
        dataChannelType = homing.Update.GetParameters()[0].ParameterType;
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        playStateDispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (playStateDispose == null || playStateDispose.ReturnType != typeof(void))
            throw new MissingMethodException(playStateType.FullName, "Dispose");
        harmony = InstallDependencyStubs(ownerType);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult ExecuteRelease(bool useStop)
    {
        ChargeAbilityFixture fixture = useStop ? stop : homing;
        object playState = NewUninitialized(playStateType);
        object owner = CreateOwner(playState);
        object ability = NewAbility(fixture.Type);
        if (fixture.LegacyPlayState != null)
            fixture.LegacyPlayState.SetValue(ability, null);
        ChargeAbilityProbe.Reset();

        bool result = (bool)Invoke(
            fixture.Execute,
            ability,
            new object[] { owner, playState });
        bool retained = fixture.LegacyPlayState != null && ReferenceEquals(
            fixture.LegacyPlayState.GetValue(ability),
            playState);
        bool ownerPreserved = ReferenceEquals(fixture.Owner.GetValue(ability), owner);
        float ttl = Convert.ToSingle(fixture.Ttl.GetValue(ability));
        int deltaLength = ((Array)fixture.DeltaArray.GetValue(ability)).Length;
        int thresholdLength = ((Array)fixture.ThresholdArray.GetValue(ability)).Length;
        bool passed = result && !retained && ownerPreserved &&
            Math.Abs(ttl - 5f) < 0.0001f && deltaLength == 3 &&
            thresholdLength == 5 && ChargeAbilityProbe.AddEffectCalls == 1;
        string actual = "result:" + result +
            ",state:" + (retained ? "retained" : "released") +
            ",owner:" + ownerPreserved +
            ",ttl:" + ttl +
            ",delta:" + deltaLength +
            ",threshold:" + thresholdLength +
            ",add_effect:" + ChargeAbilityProbe.AddEffectCalls;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,state:released,owner:True,ttl:5,delta:3," +
                "threshold:5,add_effect:1");
    }

    internal ScenarioResult HomingCurrentQueryManager()
    {
        ChillyBlastManagerFixture staleManager = CreateManager();
        ChillyBlastManagerFixture currentManager = CreateManager();
        object stalePlayState = CreatePlayState(staleManager.Manager);
        object currentPlayState = CreatePlayState(currentManager.Manager);
        recentPlayStateField.SetValue(null, currentPlayState);
        object ability = NewUpdateAbility(homing, currentPlayState, stalePlayState);

        ChargeAbilityProbe.Reset();
        Exception failure = null;
        try
        {
            InvokeUpdate(homing, ability, 0.1f);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        bool currentUsed = currentManager.IsOnlyCachedListReturnedAndCleared();
        bool staleUnused = staleManager.IsOriginalCacheUntouched();
        float ttl = Convert.ToSingle(homing.Ttl.GetValue(ability));
        bool passed = failure == null && currentUsed && staleUnused &&
            Math.Abs(ttl - 4.9f) < 0.0001f;
        return new ScenarioResult(
            passed,
            "exception:" + (failure == null ? "none" : failure.GetType().FullName) +
                ",current_used:" + currentUsed +
                ",stale_unused:" + staleUnused +
                ",ttl:" + ttl,
            "exception:none,current_used:True,stale_unused:True,ttl:4.9");
    }

    internal ScenarioResult StopCurrentPlayState()
    {
        object stalePlayState = CreatePlayState(NewUninitialized(entityManagerType));
        object currentPlayState = CreatePlayState(NewUninitialized(entityManagerType));
        recentPlayStateField.SetValue(null, currentPlayState);
        object ability = NewUpdateAbility(stop, currentPlayState, stalePlayState);
        stop.Ttl.SetValue(ability, 2f);
        stop.Charging.SetValue(ability, true);

        ChargeAbilityProbe.Reset();
        Exception failure = null;
        try
        {
            InvokeUpdate(stop, ability, 0.1f);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        bool current = ReferenceEquals(
            ChargeAbilityProbe.GreasePlayState,
            currentPlayState);
        float ttl = Convert.ToSingle(stop.Ttl.GetValue(ability));
        bool charging = (bool)stop.Charging.GetValue(ability);
        bool passed = failure == null && current &&
            ChargeAbilityProbe.GreaseCalls == 1 &&
            Math.Abs(ttl) < 0.0001f && !charging;
        return new ScenarioResult(
            passed,
            "exception:" + (failure == null ? "none" : failure.GetType().FullName) +
                ",state:" + (current ? "current" : "stale") +
                ",grease_calls:" + ChargeAbilityProbe.GreaseCalls +
                ",ttl:" + ttl +
                ",charging:" + charging,
            "exception:none,state:current,grease_calls:1,ttl:0,charging:False");
    }

    internal ScenarioResult StopNonTriggeringUpdate()
    {
        object playState = CreatePlayState(NewUninitialized(entityManagerType));
        recentPlayStateField.SetValue(null, playState);
        object ability = NewUpdateAbility(stop, playState, playState);

        ChargeAbilityProbe.Reset();
        InvokeUpdate(stop, ability, 0.1f);
        float ttl = Convert.ToSingle(stop.Ttl.GetValue(ability));
        bool charging = (bool)stop.Charging.GetValue(ability);
        bool passed = ChargeAbilityProbe.GreaseCalls == 0 &&
            Math.Abs(ttl - 4.9f) < 0.0001f && !charging;
        return new ScenarioResult(
            passed,
            "grease_calls:" + ChargeAbilityProbe.GreaseCalls +
                ",ttl:" + ttl +
                ",charging:" + charging,
            "grease_calls:0,ttl:4.9,charging:False");
    }

    internal ScenarioResult CacheRelease()
    {
        IList homingCache = NewCache(homing);
        IList stopCache = NewCache(stop);
        homing.Cache.SetValue(null, homingCache);
        stop.Cache.SetValue(null, stopCache);

        if (homing.DisposeCache != null && stop.DisposeCache != null)
        {
            Invoke(homing.DisposeCache, null, new object[0]);
            Invoke(stop.DisposeCache, null, new object[0]);
        }
        else
        {
            object playState = NewUninitialized(playStateType);
            RuntimeReflection.WriteField(playState, "mInitialized", true);
            try
            {
                Invoke(playStateDispose, playState, new object[0]);
            }
            catch (Exception)
            {
                // Cache cleanup runs before disposal reaches live game singletons.
            }
        }

        bool homingReleased = homingCache.Count == 0;
        bool stopReleased = stopCache.Count == 0;
        return new ScenarioResult(
            homingReleased && stopReleased,
            "homing:" + (homingReleased ? "released" : "retained") +
                ",stop:" + (stopReleased ? "released" : "retained"),
            "homing:released,stop:released");
    }

    private IList NewCache(ChargeAbilityFixture fixture)
    {
        IList cache = (IList)Activator.CreateInstance(fixture.Cache.FieldType);
        object ability = NewAbility(fixture.Type);
        fixture.Owner.SetValue(ability, CreateOwner(NewUninitialized(playStateType)));
        cache.Add(ability);
        return cache;
    }

    private object NewUpdateAbility(
        ChargeAbilityFixture fixture,
        object ownerPlayState,
        object legacyPlayState)
    {
        object ability = NewAbility(fixture.Type);
        fixture.Owner.SetValue(ability, CreateOwner(ownerPlayState));
        fixture.Ttl.SetValue(ability, 5f);
        fixture.Charging.SetValue(ability, false);
        fixture.DeltaArray.SetValue(ability, new float[3]);
        fixture.ThresholdArray.SetValue(
            ability,
            new float[] { 0.015f, 0.015f, 0.015f, 0.015f, 0.015f });
        if (fixture.LegacyPlayState != null)
            fixture.LegacyPlayState.SetValue(ability, legacyPlayState);
        return ability;
    }

    private object CreateOwner(object playState)
    {
        object owner = NewUninitialized(ownerImplementationType);
        object body = NewUninitialized(characterBodyType);
        PropertyInfo position = characterBodyType.GetProperty(
            "Position",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        PropertyInfo speedMultiplier = characterBodyType.GetProperty(
            "SpeedMultiplier",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (position == null || speedMultiplier == null)
            throw new MissingMemberException(characterBodyType.FullName);
        position.SetValue(body, Activator.CreateInstance(vectorType), null);
        speedMultiplier.SetValue(body, 1f, null);
        RuntimeReflection.WriteField(owner, "mBody", body);
        RuntimeReflection.WriteField(owner, "mPlayState", playState);
        return owner;
    }

    private object CreatePlayState(object manager)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        return playState;
    }

    private ChillyBlastManagerFixture CreateManager()
    {
        object manager = NewUninitialized(entityManagerType);
        Type entityListType = typeof(System.Collections.Generic.List<>).MakeGenericType(
            entityType);
        Type queryQueueType = typeof(System.Collections.Generic.Queue<>).MakeGenericType(
            entityListType);
        object cachedList = Activator.CreateInstance(entityListType);
        entityListType.GetMethod("Add").Invoke(cachedList, new object[] { null });
        object queryQueue = Activator.CreateInstance(queryQueueType);
        queryQueueType.GetMethod("Enqueue").Invoke(queryQueue, new object[] { cachedList });
        RuntimeReflection.WriteField(manager, "mQuaryLists", queryQueue);

        Array grid = Array.CreateInstance(entityListType, 16, 16);
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
                grid.SetValue(Activator.CreateInstance(entityListType), x, z);
        }
        RuntimeReflection.WriteField(manager, "mQuadGrid", grid);
        FieldInfo shields = RuntimeReflection.RequireField(entityManagerType, "mShields");
        shields.SetValue(
            manager,
            Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(
                shields.FieldType.GetGenericArguments()[0])));
        return new ChillyBlastManagerFixture(
            manager,
            queryQueue,
            queryQueueType.GetMethod("Peek"),
            cachedList);
    }

    private void InvokeUpdate(
        ChargeAbilityFixture fixture,
        object ability,
        float deltaTime)
    {
        Invoke(
            fixture.Update,
            ability,
            new object[] { Enum.ToObject(dataChannelType, 0), deltaTime });
    }

    private HarmonyInstance InstallDependencyStubs(Type ownerType)
    {
        Type specialAbilityType = homing.Type.BaseType;
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        Type greaseSplashType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.GreaseSplash",
            true);
        RuntimeReflection.RequireField(spellManagerType, "mSingelton").SetValue(
            null,
            NewUninitialized(spellManagerType));

        MethodInfo baseExecute = specialAbilityType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { ownerType, playStateType },
            null);
        MethodInfo addEffect = FindMethod(spellManagerType, "AddSpellEffect", 1);
        MethodInfo greaseInstance = greaseSplashType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();
        MethodInfo greaseExecute = greaseSplashType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { ownerType, playStateType },
            null);
        if (baseExecute == null || greaseInstance == null || greaseExecute == null)
            throw new MissingMethodException("Charge ability test dependencies are incomplete.");

        ChargeAbilityProbe.GreaseInstance = NewUninitialized(greaseSplashType);
        RuntimeReflection.RequireField(greaseSplashType, "sSingelton").SetValue(
            null,
            ChargeAbilityProbe.GreaseInstance);
        HarmonyInstance result = HarmonyInstance.Create(HarmonyOwner);
        result.Patch(baseExecute, Prefix("BaseExecutePrefix"), null, null);
        result.Patch(addEffect, Prefix("AddEffectPrefix"), null, null);
        result.Patch(
            greaseInstance,
            GenericPrefix("GreaseInstancePrefix", greaseSplashType),
            null,
            null);
        result.Patch(
            greaseExecute,
            GenericPrefix("GreaseExecutePrefix", playStateType),
            null,
            null);
        return result;
    }

    private static MethodInfo FindMethod(Type type, string name, int parameterCount)
    {
        MethodInfo result = null;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != name ||
                methods[index].GetParameters().Length != parameterCount)
                continue;
            if (result != null)
                throw new InvalidOperationException(
                    "Multiple " + type.FullName + "." + name + " methods matched.");
            result = methods[index];
        }
        if (result == null)
            throw new MissingMethodException(type.FullName, name);
        return result;
    }

    private static HarmonyMethod Prefix(string name)
    {
        return new HarmonyMethod(typeof(ChargeAbilityProbe).GetMethod(name));
    }

    private static HarmonyMethod GenericPrefix(string name, Type argument)
    {
        return new HarmonyMethod(
            typeof(ChargeAbilityProbe).GetMethod(name).MakeGenericMethod(
                new Type[] { argument }));
    }

    private static object Invoke(MethodInfo method, object target, object[] arguments)
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

    private static object NewAbility(Type type)
    {
        return Activator.CreateInstance(type, true);
    }
}

internal sealed class ChargeAbilityFixture
{
    internal Type Type { get; private set; }
    internal FieldInfo LegacyPlayState { get; private set; }
    internal FieldInfo Owner { get; private set; }
    internal FieldInfo Ttl { get; private set; }
    internal FieldInfo Charging { get; private set; }
    internal FieldInfo DeltaArray { get; private set; }
    internal FieldInfo ThresholdArray { get; private set; }
    internal FieldInfo Cache { get; private set; }
    internal MethodInfo Execute { get; private set; }
    internal MethodInfo Update { get; private set; }
    internal MethodInfo DisposeCache { get; private set; }

    internal ChargeAbilityFixture(
        Assembly magicka,
        string typeName,
        Type ownerType,
        Type playStateType)
    {
        Type = magicka.GetType(typeName, true);
        LegacyPlayState = Type.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        Owner = RuntimeReflection.RequireField(Type, "mOwner");
        Ttl = RuntimeReflection.RequireField(Type, "mTTL");
        Charging = RuntimeReflection.RequireField(Type, "mCharging");
        DeltaArray = RuntimeReflection.RequireField(Type, "deltaArray");
        ThresholdArray = RuntimeReflection.RequireField(Type, "thresholdArray");
        Cache = RuntimeReflection.RequireField(Type, "sCache");
        Execute = Type.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { ownerType, playStateType },
            null);
        Update = Type.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        DisposeCache = Type.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (Execute == null || Execute.ReturnType != typeof(bool) ||
            Update == null || Update.ReturnType != typeof(void))
            throw new MissingMethodException(Type.FullName, "Execute/Update");
    }
}

public static class ChargeAbilityProbe
{
    public static int AddEffectCalls;
    public static int GreaseCalls;
    public static object GreaseInstance;
    public static object GreasePlayState;

    public static void Reset()
    {
        AddEffectCalls = 0;
        GreaseCalls = 0;
        GreasePlayState = null;
    }

    public static bool BaseExecutePrefix(ref bool __result)
    {
        __result = true;
        return false;
    }

    public static bool AddEffectPrefix()
    {
        AddEffectCalls++;
        return false;
    }

    public static bool GreaseInstancePrefix<TResult>(ref TResult __result)
    {
        __result = (TResult)GreaseInstance;
        return false;
    }

    public static bool GreaseExecutePrefix<TPlayState>(
        TPlayState iPlayState,
        ref bool __result)
    {
        GreaseCalls++;
        GreasePlayState = iPlayState;
        __result = true;
        return false;
    }
}
