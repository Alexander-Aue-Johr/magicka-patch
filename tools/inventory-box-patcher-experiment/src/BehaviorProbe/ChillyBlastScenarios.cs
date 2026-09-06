using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class ChillyBlastScenarios
{
    private const string TypeName =
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ChillyBlast";

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        if (magicka.GetType(TypeName, false) == null)
        {
            report.AddNotApplicable(
                "chilly_blast.play_state_release",
                "ChillyBlast is not present in this Magicka version");
            report.AddNotApplicable(
                "chilly_blast.execute_behavior",
                "ChillyBlast is not present in this Magicka version");
            report.AddNotApplicable(
                "chilly_blast.current_query_manager",
                "ChillyBlast is not present in this Magicka version");
            return;
        }

        ChillyBlastHarness harness = new ChillyBlastHarness(magicka);
        try
        {
            report.Add(
                "chilly_blast.play_state_release",
                harness.PlayStateRelease());
            report.Add(
                "chilly_blast.execute_behavior",
                harness.ExecuteBehavior());
            report.Add(
                "chilly_blast.current_query_manager",
                harness.CurrentQueryManager());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class ChillyBlastHarness
{
    private readonly Assembly magicka;
    private readonly Type bodyType;
    private readonly Type dataChannelType;
    private readonly Type entityManagerType;
    private readonly Type entityType;
    private readonly Type ownerImplementationType;
    private readonly Type playStateType;
    private readonly Type chillyBlastType;
    private readonly Type vectorType;
    private readonly MethodInfo execute;
    private readonly MethodInfo update;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo ownerTurnSpeedField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo ttlField;
    private readonly HarmonyInstance probeHarmony;

    internal ChillyBlastHarness(Assembly magicka)
    {
        this.magicka = magicka;
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
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
        chillyBlastType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ChillyBlast",
            true);
        update = FindMethod(chillyBlastType, "Update", 2, false);
        dataChannelType = update.GetParameters()[0].ParameterType;
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);

        execute = chillyBlastType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { ownerType, playStateType },
            null);
        if (execute == null || execute.ReturnType != typeof(bool) ||
            update.ReturnType != typeof(void))
            throw new MissingMethodException(chillyBlastType.FullName, "Execute/Update");

        legacyPlayStateField = chillyBlastType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        ownerField = RuntimeReflection.RequireField(chillyBlastType, "mOwner");
        ownerTurnSpeedField = RuntimeReflection.RequireField(
            chillyBlastType,
            "mOwnerTurnSpeed");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        ttlField = RuntimeReflection.RequireField(chillyBlastType, "mTTL");

        probeHarmony = InstallDependencyStubs(ownerType);
    }

    internal void Dispose()
    {
        probeHarmony.UnpatchAll("org.magickacommunitypatch.behavior-probe-chilly-blast");
    }

    internal ScenarioResult PlayStateRelease()
    {
        ChillyBlastExecution execution = CreateExecution();
        ChillyBlastProbe.Reset();
        bool result = InvokeExecute(execution);
        bool retained = legacyPlayStateField != null && ReferenceEquals(
            legacyPlayStateField.GetValue(execution.Effect),
            execution.PlayState);
        return new ScenarioResult(
            result && !retained,
            "result:" + result + ",state:" + (retained ? "retained" : "released"),
            "result:True,state:released");
    }

    internal ScenarioResult ExecuteBehavior()
    {
        ChillyBlastExecution execution = CreateExecution();
        ChillyBlastProbe.Reset();
        bool result = InvokeExecute(execution);
        bool owner = ReferenceEquals(
            ownerField.GetValue(execution.Effect),
            execution.Owner);
        float ownerTurnSpeed = Convert.ToSingle(
            ownerTurnSpeedField.GetValue(execution.Effect));
        float ttl = Convert.ToSingle(ttlField.GetValue(execution.Effect));
        bool passed = result && owner && Math.Abs(ownerTurnSpeed - 2.5f) < 0.0001f &&
            Math.Abs(ttl - 1f) < 0.0001f &&
            ChillyBlastProbe.PlayCueCalls == 1 &&
            ChillyBlastProbe.StartEffectCalls == 1 &&
            ChillyBlastProbe.AddEffectCalls == 1;
        string actual = "result:" + result +
            ",owner:" + owner +
            ",owner_turn_speed:" + ownerTurnSpeed +
            ",ttl:" + ttl +
            ",audio:" + ChillyBlastProbe.PlayCueCalls +
            ",start_effect:" + ChillyBlastProbe.StartEffectCalls +
            ",add_effect:" + ChillyBlastProbe.AddEffectCalls;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,owner:True,owner_turn_speed:2.5,ttl:1," +
                "audio:1,start_effect:1,add_effect:1");
    }

    internal ScenarioResult CurrentQueryManager()
    {
        ChillyBlastManagerFixture staleManager = CreateManager();
        ChillyBlastManagerFixture currentManager = CreateManager();
        object currentPlayState = CreatePlayState(currentManager.Manager);
        object stalePlayState = CreatePlayState(staleManager.Manager);
        recentPlayStateField.SetValue(null, currentPlayState);

        object owner = CreateOwner(currentPlayState);
        object effect = NewUninitialized(chillyBlastType);
        ownerField.SetValue(effect, owner);
        ttlField.SetValue(effect, 1f);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(effect, stalePlayState);
        ConfigureOfflineNetwork();

        ChillyBlastProbe.Reset();
        InvokeUpdate(effect, 0.1f);

        bool currentUsed = currentManager.IsOnlyCachedListReturnedAndCleared();
        bool staleUnused = staleManager.IsOriginalCacheUntouched();
        float ttl = Convert.ToSingle(ttlField.GetValue(effect));
        bool passed = currentUsed && staleUnused &&
            ChillyBlastProbe.UpdateEffectCalls == 1 &&
            Math.Abs(ttl - 0.9f) < 0.0001f;
        string actual = "current_used:" + currentUsed +
            ",stale_unused:" + staleUnused +
            ",effect_updates:" + ChillyBlastProbe.UpdateEffectCalls +
            ",ttl:" + ttl;
        return new ScenarioResult(
            passed,
            actual,
            "current_used:True,stale_unused:True,effect_updates:1,ttl:0.9");
    }

    private ChillyBlastExecution CreateExecution()
    {
        object playState = NewUninitialized(playStateType);
        object owner = CreateOwner(playState);
        object effect = NewUninitialized(chillyBlastType);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(effect, null);
        return new ChillyBlastExecution(effect, owner, playState);
    }

    private object CreateOwner(object playState)
    {
        object owner = NewUninitialized(ownerImplementationType);
        object body = NewUninitialized(bodyType);
        bodyType.GetProperty("Position").SetValue(
            body,
            Activator.CreateInstance(vectorType),
            null);
        RuntimeReflection.WriteField(owner, "mBody", body);
        RuntimeReflection.WriteField(owner, "mPlayState", playState);
        RuntimeReflection.WriteField(owner, "mTurnSpeed", 2.5f);
        return owner;
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

    private object CreatePlayState(object manager)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        return playState;
    }

    private void ConfigureOfflineNetwork()
    {
        Type networkManagerType = magicka.GetType("Magicka.Network.NetworkManager", true);
        RuntimeReflection.RequireField(networkManagerType, "sSingelton").SetValue(
            null,
            NewUninitialized(networkManagerType));
    }

    private bool InvokeExecute(ChillyBlastExecution execution)
    {
        try
        {
            return (bool)execute.Invoke(
                execution.Effect,
                new object[] { execution.Owner, execution.PlayState });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private void InvokeUpdate(object effect, float deltaTime)
    {
        try
        {
            update.Invoke(
                effect,
                new object[] { Enum.ToObject(dataChannelType, 0), deltaTime });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private HarmonyInstance InstallDependencyStubs(Type ownerType)
    {
        Type specialAbilityType = chillyBlastType.BaseType;
        Type audioManagerType = magicka.GetType("Magicka.Audio.AudioManager", true);
        Type effectManagerType = magicka.GetType("Magicka.Graphics.EffectManager", true);
        Type spellManagerType = magicka.GetType("Magicka.GameLogic.Spells.SpellManager", true);

        RuntimeReflection.RequireField(audioManagerType, "instance").SetValue(
            null,
            NewUninitialized(audioManagerType));
        RuntimeReflection.RequireField(effectManagerType, "mSingelton").SetValue(
            null,
            NewUninitialized(effectManagerType));
        RuntimeReflection.RequireField(spellManagerType, "mSingelton").SetValue(
            null,
            NewUninitialized(spellManagerType));

        MethodInfo baseExecute = specialAbilityType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { ownerType, playStateType },
            null);
        MethodInfo playCue = FindMethod(audioManagerType, "PlayCue", 3, false);
        MethodInfo startEffect = FindMethod(effectManagerType, "StartEffect", 4, false);
        MethodInfo updateEffect = FindMethod(
            effectManagerType,
            "UpdatePositionDirection",
            3,
            false);
        MethodInfo addEffect = FindMethod(spellManagerType, "AddSpellEffect", 1, false);
        if (baseExecute == null)
            throw new MissingMethodException("ChillyBlast test dependencies are incomplete.");

        Type visualEffectReferenceType = startEffect.GetParameters()[3].ParameterType.GetElementType();
        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-chilly-blast");
        harmony.Patch(baseExecute, Prefix("BaseExecutePrefix"), null, null);
        harmony.Patch(
            playCue,
            GenericPrefix("PlayCuePrefix", playCue.ReturnType),
            null,
            null);
        harmony.Patch(
            startEffect,
            GenericPrefix("StartEffectPrefix", visualEffectReferenceType),
            null,
            null);
        harmony.Patch(updateEffect, Prefix("UpdateEffectPrefix"), null, null);
        harmony.Patch(addEffect, Prefix("AddEffectPrefix"), null, null);
        return harmony;
    }

    private static MethodInfo FindMethod(
        Type type,
        string name,
        int parameterCount,
        bool generic)
    {
        MethodInfo result = null;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != name ||
                methods[index].GetParameters().Length != parameterCount ||
                methods[index].IsGenericMethodDefinition != generic)
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
        return new HarmonyMethod(typeof(ChillyBlastProbe).GetMethod(name));
    }

    private static HarmonyMethod GenericPrefix(string name, Type argument)
    {
        return new HarmonyMethod(
            typeof(ChillyBlastProbe).GetMethod(name).MakeGenericMethod(
                new Type[] { argument }));
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

internal sealed class ChillyBlastManagerFixture
{
    private readonly object queryQueue;
    private readonly MethodInfo peek;
    private readonly object cachedList;

    internal object Manager { get; private set; }

    internal ChillyBlastManagerFixture(
        object manager,
        object queryQueue,
        MethodInfo peek,
        object cachedList)
    {
        Manager = manager;
        this.queryQueue = queryQueue;
        this.peek = peek;
        this.cachedList = cachedList;
    }

    internal bool IsOnlyCachedListReturnedAndCleared()
    {
        return ((ICollection)queryQueue).Count == 1 &&
            ReferenceEquals(peek.Invoke(queryQueue, null), cachedList) &&
            ((ICollection)cachedList).Count == 0;
    }

    internal bool IsOriginalCacheUntouched()
    {
        return ((ICollection)queryQueue).Count == 1 &&
            ReferenceEquals(peek.Invoke(queryQueue, null), cachedList) &&
            ((ICollection)cachedList).Count == 1;
    }
}

internal sealed class ChillyBlastExecution
{
    internal object Effect { get; private set; }
    internal object Owner { get; private set; }
    internal object PlayState { get; private set; }

    internal ChillyBlastExecution(object effect, object owner, object playState)
    {
        Effect = effect;
        Owner = owner;
        PlayState = playState;
    }
}

public static class ChillyBlastProbe
{
    public static int PlayCueCalls;
    public static int StartEffectCalls;
    public static int AddEffectCalls;
    public static int UpdateEffectCalls;

    public static void Reset()
    {
        PlayCueCalls = 0;
        StartEffectCalls = 0;
        AddEffectCalls = 0;
        UpdateEffectCalls = 0;
    }

    public static bool BaseExecutePrefix(ref bool __result)
    {
        __result = true;
        return false;
    }

    public static bool PlayCuePrefix<TResult>(ref TResult __result)
    {
        PlayCueCalls++;
        __result = default(TResult);
        return false;
    }

    public static bool StartEffectPrefix<TReference>(
        ref TReference oRef,
        ref bool __result)
    {
        StartEffectCalls++;
        oRef = default(TReference);
        __result = true;
        return false;
    }

    public static bool AddEffectPrefix()
    {
        AddEffectCalls++;
        return false;
    }

    public static bool UpdateEffectPrefix(ref bool __result)
    {
        UpdateEffectCalls++;
        __result = true;
        return false;
    }
}
