using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class SummonCrossScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        SummonCrossHarness harness = new SummonCrossHarness(magicka);
        try
        {
            report.Add("summon_cross.vector_release", harness.VectorRelease());
            report.Add("summon_cross.owner_release", harness.OwnerRelease());
            report.Add("summon_cross.current_play_state", harness.CurrentPlayState());
            report.Add("summon_cross.level_dispose", harness.LevelDispose());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class SummonCrossHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-summon-cross";

    private readonly Assembly magicka;
    private readonly Type bodyType;
    private readonly Type characterTemplateType;
    private readonly Type crossType;
    private readonly Type networkManagerType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type ownerType;
    private readonly Type playStateType;
    private readonly Type vectorType;
    private readonly FieldInfo cacheField;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo networkManagerSingleton;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo templateField;
    private readonly MethodInfo disposeCache;
    private readonly MethodInfo getNetworkState;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo privateExecute;
    private readonly MethodInfo vectorExecute;
    private readonly HarmonyInstance harmony;

    internal SummonCrossHarness(Assembly magicka)
    {
        this.magicka = magicka;
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
        characterTemplateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        crossType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonCross",
            true);
        networkManagerType = magicka.GetType("Magicka.Network.NetworkManager", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        ownerType = magicka.GetType("Magicka.GameLogic.Entities.ISpellCaster", true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);

        cacheField = RuntimeReflection.RequireField(crossType, "sCache");
        legacyPlayStateField = crossType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        networkManagerSingleton = RuntimeReflection.RequireField(
            networkManagerType,
            "sSingelton");
        ownerField = RuntimeReflection.RequireField(crossType, "mOwner");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        templateField = RuntimeReflection.RequireField(crossType, "sTemplate");
        vectorExecute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { vectorType, playStateType });
        ownerExecute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { ownerType, playStateType });
        privateExecute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            Type.EmptyTypes);
        disposeCache = crossType.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        getNetworkState = networkManagerType.GetProperty(
            "State",
            BindingFlags.Instance | BindingFlags.Public).GetGetMethod();

        harmony = HarmonyInstance.Create(HarmonyOwner);
        InstallDependencyStubs();
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult VectorRelease()
    {
        object suppliedPlayState = NewUninitialized(playStateType);
        object owner = CreateOwner(suppliedPlayState);
        object ability = NewUninitialized(crossType);
        ownerField.SetValue(ability, owner);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(ability, null);
        recentPlayStateField.SetValue(null, suppliedPlayState);

        ConfigureNetwork(2);
        SummonCrossProbe.Reset();
        bool result;
        try
        {
            result = (bool)Invoke(
                vectorExecute,
                ability,
                new object[] { Activator.CreateInstance(vectorType), suppliedPlayState });
        }
        catch (Exception exception)
        {
            return ExecutionFailure(exception);
        }

        bool retained = legacyPlayStateField != null && ReferenceEquals(
            legacyPlayStateField.GetValue(ability),
            suppliedPlayState);
        bool ownerPreserved = ReferenceEquals(ownerField.GetValue(ability), owner);
        bool passed = result && !retained && ownerPreserved &&
            SummonCrossProbe.BubbleCalls == 1 &&
            SummonCrossProbe.AddEffectCalls == 1;
        return new ScenarioResult(
            passed,
            "result:" + result +
                ",state:" + (retained ? "retained" : "released") +
                ",owner:" + ownerPreserved +
                ",bubble:" + SummonCrossProbe.BubbleCalls +
                ",add_effect:" + SummonCrossProbe.AddEffectCalls,
            "result:True,state:released,owner:True,bubble:1,add_effect:1");
    }

    internal ScenarioResult OwnerRelease()
    {
        object suppliedPlayState = NewUninitialized(playStateType);
        object owner = CreateOwner(suppliedPlayState);
        object ability = NewUninitialized(crossType);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(ability, null);
        recentPlayStateField.SetValue(null, suppliedPlayState);

        ConfigureNetwork(2);
        SummonCrossProbe.Reset();
        bool result;
        try
        {
            result = (bool)Invoke(
                ownerExecute,
                ability,
                new object[] { owner, suppliedPlayState });
        }
        catch (Exception exception)
        {
            return ExecutionFailure(exception);
        }

        bool retained = legacyPlayStateField != null && ReferenceEquals(
            legacyPlayStateField.GetValue(ability),
            suppliedPlayState);
        bool ownerPreserved = ReferenceEquals(ownerField.GetValue(ability), owner);
        bool passed = result && !retained && ownerPreserved &&
            SummonCrossProbe.BubbleCalls == 1 &&
            SummonCrossProbe.AddEffectCalls == 1;
        return new ScenarioResult(
            passed,
            "result:" + result +
                ",state:" + (retained ? "retained" : "released") +
                ",owner:" + ownerPreserved +
                ",bubble:" + SummonCrossProbe.BubbleCalls +
                ",add_effect:" + SummonCrossProbe.AddEffectCalls,
            "result:True,state:released,owner:True,bubble:1,add_effect:1");
    }

    internal ScenarioResult CurrentPlayState()
    {
        object stalePlayState = NewUninitialized(playStateType);
        object currentPlayState = NewUninitialized(playStateType);
        object staleNavMesh = ConfigureLevelChain(stalePlayState);
        object currentNavMesh = ConfigureLevelChain(currentPlayState);
        recentPlayStateField.SetValue(null, currentPlayState);

        object ability = NewUninitialized(crossType);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(ability, stalePlayState);
        ConfigureNetwork(0);
        SummonCrossProbe.Reset();

        bool reached = false;
        try
        {
            Invoke(privateExecute, ability, new object[0]);
        }
        catch (SummonCrossNavMeshReachedException)
        {
            reached = true;
        }

        bool characterCurrent = ReferenceEquals(
            SummonCrossProbe.ObservedPlayState,
            currentPlayState);
        bool navMeshCurrent = ReferenceEquals(
            SummonCrossProbe.ObservedNavMesh,
            currentNavMesh);
        bool staleUnused = !ReferenceEquals(
            SummonCrossProbe.ObservedNavMesh,
            staleNavMesh);
        bool passed = reached && characterCurrent && navMeshCurrent && staleUnused &&
            SummonCrossProbe.GetCharacterCalls == 1;
        return new ScenarioResult(
            passed,
            "reached:" + reached +
                ",character_state:" + (characterCurrent ? "current" : "stale") +
                ",nav_mesh:" + (navMeshCurrent ? "current" : "stale") +
                ",get_character_calls:" + SummonCrossProbe.GetCharacterCalls,
            "reached:True,character_state:current,nav_mesh:current," +
                "get_character_calls:1");
    }

    internal ScenarioResult LevelDispose()
    {
        IList cache = (IList)Activator.CreateInstance(cacheField.FieldType);
        cache.Add(NewUninitialized(crossType));
        cacheField.SetValue(null, cache);
        templateField.SetValue(null, NewUninitialized(characterTemplateType));

        if (disposeCache != null)
        {
            Invoke(disposeCache, null, new object[0]);
        }
        else
        {
            object playState = NewUninitialized(playStateType);
            RuntimeReflection.WriteField(playState, "mInitialized", true);
            MethodInfo dispose = playStateType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null)
                throw new MissingMethodException(playStateType.FullName, "Dispose");
            try
            {
                Invoke(dispose, playState, new object[0]);
            }
            catch (Exception)
            {
                // Original level disposal needs live game singletons. The runtime
                // cleanup is inserted before those dependencies are accessed.
            }
        }

        bool poolReleased = cache.Count == 0;
        bool templateReleased = templateField.GetValue(null) == null;
        return new ScenarioResult(
            poolReleased && templateReleased,
            "pool:" + (poolReleased ? "released" : "retained") +
                ",template:" + (templateReleased ? "released" : "retained"),
            "pool:released,template:released");
    }

    private object CreateOwner(object playState)
    {
        Type avatarType = magicka.GetType("Magicka.GameLogic.Entities.Avatar", true);
        object owner = NewUninitialized(avatarType);
        object body = NewUninitialized(bodyType);
        bodyType.GetProperty("Position").SetValue(
            body,
            Activator.CreateInstance(vectorType),
            null);
        RuntimeReflection.WriteField(owner, "mBody", body);
        RuntimeReflection.WriteField(owner, "mPlayState", playState);
        return owner;
    }

    private static ScenarioResult ExecutionFailure(Exception exception)
    {
        string stack = exception.StackTrace == null
            ? "<none>"
            : exception.StackTrace.Replace("\r", " ").Replace("\n", " ");
        return new ScenarioResult(
            false,
            "exception:" + exception.GetType().FullName +
                ",message:" + exception.Message.Replace("\r", " ").Replace("\n", " ") +
                ",get_character:" + SummonCrossProbe.GetCharacterCalls +
                ",bubble:" + SummonCrossProbe.BubbleCalls +
                ",add_effect:" + SummonCrossProbe.AddEffectCalls +
                ",stack:" + stack,
            "result:True,state:released,owner:True,bubble:1,add_effect:1");
    }

    private object ConfigureLevelChain(object playState)
    {
        Type levelType = magicka.GetType("Magicka.Levels.Level", true);
        Type sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        Type levelModelType = magicka.GetType("Magicka.Levels.LevelModel", true);
        FieldInfo navMeshField = RuntimeReflection.RequireField(levelModelType, "mNavMesh");
        object level = NewUninitialized(levelType);
        object scene = NewUninitialized(sceneType);
        object model = NewUninitialized(levelModelType);
        object navMesh = NewUninitialized(navMeshField.FieldType);
        navMeshField.SetValue(model, navMesh);
        RuntimeReflection.WriteField(scene, "mModel", model);
        RuntimeReflection.WriteField(level, "mCurrentScene", scene);
        RuntimeReflection.WriteField(playState, "mLevel", level);
        return navMesh;
    }

    private void ConfigureNetwork(int state)
    {
        object networkManager = NewUninitialized(networkManagerType);
        networkManagerSingleton.SetValue(null, networkManager);
        SummonCrossProbe.NetworkManager = networkManager;
        SummonCrossProbe.NetworkState = Enum.ToObject(getNetworkState.ReturnType, state);
    }

    private void InstallDependencyStubs()
    {
        MethodInfo getCharacter = nonPlayerCharacterType.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { playStateType },
            null);
        MethodInfo getNetworkManager = networkManagerType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();
        Type characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        MethodInfo addBubble = FindMethod(characterType, "AddBubbleShield", 1);
        MethodInfo addAura = FindMethod(characterType, "AddAura", -1);
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        MethodInfo addEffect = FindMethod(spellManagerType, "AddSpellEffect", 1);
        RuntimeReflection.RequireField(spellManagerType, "mSingelton").SetValue(
            null,
            NewUninitialized(spellManagerType));

        harmony.Patch(
            getCharacter,
            new HarmonyMethod(
                typeof(SummonCrossProbe).GetMethod("GetCharacterPrefix")
                    .MakeGenericMethod(
                        new Type[] { playStateType, nonPlayerCharacterType })),
            null,
            null);
        harmony.Patch(
            getNetworkState,
            new HarmonyMethod(
                typeof(SummonCrossProbe).GetMethod("NetworkStatePrefix")
                    .MakeGenericMethod(new Type[] { getNetworkState.ReturnType })),
            null,
            null);
        harmony.Patch(
            getNetworkManager,
            new HarmonyMethod(
                typeof(SummonCrossProbe).GetMethod("NetworkManagerPrefix")
                    .MakeGenericMethod(new Type[] { networkManagerType })),
            null,
            null);
        harmony.Patch(
            addBubble,
            new HarmonyMethod(typeof(SummonCrossProbe).GetMethod("AddBubblePrefix")),
            null,
            null);
        harmony.Patch(
            addAura,
            new HarmonyMethod(typeof(SummonCrossProbe).GetMethod("AddAuraPrefix")),
            null,
            null);
        harmony.Patch(
            addEffect,
            new HarmonyMethod(typeof(SummonCrossProbe).GetMethod("AddEffectPrefix")),
            null,
            null);
        InstallNavMeshStubs();
    }

    private void InstallNavMeshStubs()
    {
        Type levelModelType = magicka.GetType("Magicka.Levels.LevelModel", true);
        Type navMeshType = RuntimeReflection.RequireField(levelModelType, "mNavMesh").FieldType;
        MethodInfo[] methods = navMeshType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        int patched = 0;
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "GetNearestPosition")
                continue;
            int parameterCount = methods[index].GetParameters().Length;
            if (parameterCount != 3 && parameterCount != 5)
                continue;
            harmony.Patch(
                methods[index],
                new HarmonyMethod(typeof(SummonCrossProbe).GetMethod("NavMeshPrefix")),
                null,
                null);
            patched++;
        }
        if (patched != 2)
            throw new InvalidOperationException(
                "Expected two NavMesh.GetNearestPosition overloads, found " + patched + ".");
    }

    private MethodInfo RequireMethod(string name, BindingFlags flags, Type[] parameterTypes)
    {
        MethodInfo method = crossType.GetMethod(name, flags, null, parameterTypes, null);
        if (method == null || method.ReturnType != typeof(bool))
            throw new MissingMethodException(crossType.FullName, name);
        return method;
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
                (parameterCount >= 0 &&
                    methods[index].GetParameters().Length != parameterCount))
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

    private static object Invoke(MethodInfo method, object target, object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            if (exception.InnerException is SummonCrossNavMeshReachedException)
                throw exception.InnerException;
            throw new InvalidOperationException(
                (exception.InnerException ?? exception).ToString(),
                exception.InnerException ?? exception);
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class SummonCrossProbe
{
    public static int AddEffectCalls;
    public static int BubbleCalls;
    public static int GetCharacterCalls;
    public static object NetworkManager;
    public static object NetworkState;
    public static object ObservedNavMesh;
    public static object ObservedPlayState;

    public static void Reset()
    {
        AddEffectCalls = 0;
        BubbleCalls = 0;
        GetCharacterCalls = 0;
        ObservedNavMesh = null;
        ObservedPlayState = null;
    }

    public static bool GetCharacterPrefix<TPlayState, TCharacter>(
        TPlayState __0,
        ref TCharacter __result)
    {
        GetCharacterCalls++;
        ObservedPlayState = __0;
        object character = FormatterServices.GetUninitializedObject(typeof(TCharacter));
        GC.SuppressFinalize(character);
        __result = (TCharacter)character;
        return false;
    }

    public static bool NetworkStatePrefix<TState>(ref TState __result)
    {
        __result = (TState)NetworkState;
        return false;
    }

    public static bool NetworkManagerPrefix<TManager>(ref TManager __result)
    {
        __result = (TManager)NetworkManager;
        return false;
    }

    public static bool AddBubblePrefix()
    {
        BubbleCalls++;
        return false;
    }

    public static bool AddAuraPrefix()
    {
        BubbleCalls++;
        return false;
    }

    public static bool AddEffectPrefix()
    {
        AddEffectCalls++;
        return false;
    }

    public static void NavMeshPrefix(object __instance)
    {
        ObservedNavMesh = __instance;
        throw new SummonCrossNavMeshReachedException();
    }
}

internal sealed class SummonCrossNavMeshReachedException : Exception
{
}
