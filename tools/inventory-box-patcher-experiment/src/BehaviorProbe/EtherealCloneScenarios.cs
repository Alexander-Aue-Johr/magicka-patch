using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class EtherealCloneScenarios
{
    internal static void Prepare(Assembly magicka)
    {
        EtherealCloneHarness.InstallProbesEarly(magicka);
    }

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        EtherealCloneHarness harness = new EtherealCloneHarness(magicka);
        try
        {
            report.Add(
                "ethereal_clone.play_state_release",
                harness.PlayStateRelease());
            report.Add(
                "ethereal_clone.current_nav_mesh",
                harness.CurrentNavMesh());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class EtherealCloneHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-ethereal-clone";

    private readonly Assembly magicka;
    private readonly Type bodyType;
    private readonly Type characterType;
    private readonly Type cloneType;
    private readonly Type networkManagerType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type playStateType;
    private readonly MethodInfo execute;
    private readonly MethodInfo spawnClone;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo recentPlayStateField;
    private readonly HarmonyInstance harmony;

    internal EtherealCloneHarness(Assembly magicka)
    {
        this.magicka = magicka;
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        cloneType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                "EtherealClone",
            true);
        characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type templateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        networkManagerType = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        execute = cloneType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { ownerType, playStateType },
            null);
        spawnClone = cloneType.GetMethod(
            "SpawnClone",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { templateType, typeof(int), typeof(uint) },
            null);
        if (execute == null || spawnClone == null)
            throw new MissingMethodException(
                "EtherealClone behavior targets are incomplete.");

        legacyPlayStateField = cloneType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        ownerField = RuntimeReflection.RequireField(cloneType, "mOwner");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");

        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult PlayStateRelease()
    {
        object clone = NewUninitialized(cloneType);
        object owner = NewUninitialized(characterType);
        RuntimeReflection.WriteField(owner, "mBody", NewUninitialized(bodyType));
        object supplied = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(owner, "mPlayState", supplied);
        ConfigureNetwork("Server");
        EtherealCloneProbe.Reset();
        bool result = (bool)Invoke(
            execute,
            clone,
            new object[] { owner, supplied });

        bool retained = legacyPlayStateField != null &&
            ReferenceEquals(legacyPlayStateField.GetValue(clone), supplied);
        bool ownerAssigned = ReferenceEquals(ownerField.GetValue(clone), owner);
        bool passed = !result && ownerAssigned && !retained;
        string actual = "result:" + result + ",owner:" + ownerAssigned +
            ",play_state:" + (retained ? "retained" : "released");
        return new ScenarioResult(
            passed,
            actual,
            "result:False,owner:True,play_state:released");
    }

    internal ScenarioResult CurrentNavMesh()
    {
        object clone = NewUninitialized(cloneType);
        object owner = NewUninitialized(characterType);
        RuntimeReflection.WriteField(owner, "mBody", NewUninitialized(bodyType));
        object current = NewPlayStateWithNavMesh();
        object stale = NewPlayStateWithNavMesh();
        object currentNavMesh = GetNavMesh(current);
        RuntimeReflection.WriteField(owner, "mPlayState", current);
        ownerField.SetValue(clone, owner);
        recentPlayStateField.SetValue(null, current);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(clone, stale);
        ConfigureNetwork("Server");
        EtherealCloneProbe.Reset();
        EtherealCloneProbe.Character =
            NewUninitialized(nonPlayerCharacterType);
        EtherealCloneProbe.StopAtNavMesh = true;

        bool stopped = false;
        bool nullReference = false;
        try
        {
            Invoke(spawnClone, clone, new object[] { null, 0, (uint)1 });
        }
        catch (EtherealCloneNavMeshReachedException)
        {
            stopped = true;
        }
        catch (NullReferenceException)
        {
            nullReference = true;
        }
        finally
        {
            EtherealCloneProbe.StopAtNavMesh = false;
        }

        bool usedCurrent = ReferenceEquals(
            EtherealCloneProbe.ObservedNavMesh,
            currentNavMesh);
        bool passed = stopped && usedCurrent &&
            EtherealCloneProbe.CharacterCalls == 1;
        string actual = "stopped:" + stopped + ",null_reference:" +
            nullReference + ",nav_mesh:" +
            (usedCurrent ? "current" : "stale") + ",character_calls:" +
            EtherealCloneProbe.CharacterCalls;
        return new ScenarioResult(
            passed,
            actual,
            "stopped:True,null_reference:False,nav_mesh:current,character_calls:1");
    }

    internal static void InstallProbesEarly(Assembly magicka)
    {
        Type playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type networkManagerType = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        Type nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        MethodInfo getNetworkManager = networkManagerType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();
        MethodInfo getNetworkState = networkManagerType.GetProperty(
            "State",
            BindingFlags.Instance | BindingFlags.Public).GetGetMethod();
        MethodInfo getCharacter = nonPlayerCharacterType.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { playStateType },
            null);

        HarmonyInstance harmony = HarmonyInstance.Create(HarmonyOwner);
        harmony.Patch(
            getNetworkManager,
            new HarmonyMethod(
                typeof(EtherealCloneProbe).GetMethod("NetworkManagerPrefix")
                    .MakeGenericMethod(new Type[] { networkManagerType })),
            null,
            null);
        harmony.Patch(
            getNetworkState,
            new HarmonyMethod(
                typeof(EtherealCloneProbe).GetMethod("NetworkStatePrefix")
                    .MakeGenericMethod(new Type[] { getNetworkState.ReturnType })),
            null,
            null);
        harmony.Patch(
            getCharacter,
            new HarmonyMethod(
                typeof(EtherealCloneProbe).GetMethod("CharacterPrefix")
                    .MakeGenericMethod(new Type[] { nonPlayerCharacterType })),
            null,
            null);

        Type levelModelType = magicka.GetType(
            "Magicka.Levels.LevelModel",
            true);
        Type navMeshType = RuntimeReflection.RequireField(
            levelModelType,
            "mNavMesh").FieldType;
        MethodInfo[] methods = navMeshType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        int patched = 0;
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "GetNearestPosition" ||
                methods[index].GetParameters().Length != 3)
                continue;
            harmony.Patch(
                methods[index],
                new HarmonyMethod(
                    typeof(EtherealCloneProbe).GetMethod("NavMeshPrefix")),
                null,
                null);
            patched++;
        }
        if (patched != 1)
            throw new InvalidOperationException(
                "Expected one three-parameter NavMesh lookup, found " +
                patched + ".");
    }

    private void ConfigureNetwork(string stateName)
    {
        object manager = NewUninitialized(networkManagerType);
        EtherealCloneProbe.NetworkManager = manager;
        Type stateType = networkManagerType.GetProperty("State").PropertyType;
        EtherealCloneProbe.NetworkState = Enum.Parse(stateType, stateName);
    }

    private object NewPlayStateWithNavMesh()
    {
        Type levelType = magicka.GetType("Magicka.Levels.Level", true);
        Type sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        Type modelType = magicka.GetType("Magicka.Levels.LevelModel", true);
        FieldInfo navMeshField = RuntimeReflection.RequireField(
            modelType,
            "mNavMesh");
        object playState = NewUninitialized(playStateType);
        object level = NewUninitialized(levelType);
        object scene = NewUninitialized(sceneType);
        object model = NewUninitialized(modelType);
        object navMesh = NewUninitialized(navMeshField.FieldType);
        navMeshField.SetValue(model, navMesh);
        RuntimeReflection.WriteField(scene, "mModel", model);
        RuntimeReflection.WriteField(level, "mCurrentScene", scene);
        RuntimeReflection.WriteField(playState, "mLevel", level);
        return playState;
    }

    private static object GetNavMesh(object playState)
    {
        object level = RuntimeReflection.ReadField(playState, "mLevel");
        object scene = RuntimeReflection.ReadField(level, "mCurrentScene");
        object model = RuntimeReflection.ReadField(scene, "mModel");
        return RuntimeReflection.ReadField(model, "mNavMesh");
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

public static class EtherealCloneProbe
{
    public static object NetworkManager;
    public static object NetworkState;
    public static object Character;
    public static object ObservedNavMesh;
    public static int CharacterCalls;
    public static bool StopAtNavMesh;

    public static void Reset()
    {
        Character = null;
        ObservedNavMesh = null;
        CharacterCalls = 0;
        StopAtNavMesh = false;
    }

    public static bool NetworkManagerPrefix<TManager>(ref TManager __result)
    {
        __result = (TManager)NetworkManager;
        return false;
    }

    public static bool NetworkStatePrefix<TState>(ref TState __result)
    {
        __result = (TState)NetworkState;
        return false;
    }

    public static bool CharacterPrefix<TCharacter>(ref TCharacter __result)
    {
        CharacterCalls++;
        __result = (TCharacter)Character;
        return false;
    }

    public static void NavMeshPrefix(object __instance)
    {
        ObservedNavMesh = __instance;
        if (StopAtNavMesh)
            throw new EtherealCloneNavMeshReachedException();
    }
}

public sealed class EtherealCloneNavMeshReachedException : Exception
{
}
