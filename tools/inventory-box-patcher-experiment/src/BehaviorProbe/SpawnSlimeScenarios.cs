using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class SpawnSlimeScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        SpawnSlimeHarness harness = new SpawnSlimeHarness(magicka);
        report.Add("spawn_slime.play_state_release", harness.PlayStateRelease(false));
        report.Add("spawn_slime_overkill.play_state_release", harness.PlayStateRelease(true));
        report.Add("spawn_slime.current_nav_mesh", harness.CurrentNavMesh());
        report.Add("spawn_slime.spawn_slimes_current_nav_mesh", harness.SpawnSlimesCurrentNavMesh());
    }
}

internal sealed class SpawnSlimeHarness
{
    private readonly Assembly magicka;
    private readonly Type bodyType;
    private readonly Type characterType;
    private readonly Type characterTemplateType;
    private readonly Type elementsType;
    private readonly Type networkManagerType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type ownerType;
    private readonly Type playStateType;
    private readonly Type spawnSlimeType;
    private readonly Type spawnSlimeOverkillType;
    private readonly Type vectorType;
    private readonly MethodInfo createEntities;
    private readonly MethodInfo execute;
    private readonly MethodInfo executeOverkill;
    private readonly MethodInfo spawnSlimes;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo networkManagerSingleton;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo recentPlayStateField;

    internal SpawnSlimeHarness(Assembly magicka)
    {
        this.magicka = magicka;
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
        characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        characterTemplateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        elementsType = magicka.GetType("Magicka.Elements", true);
        networkManagerType = magicka.GetType("Magicka.Network.NetworkManager", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        ownerType = magicka.GetType("Magicka.GameLogic.Entities.ISpellCaster", true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        spawnSlimeType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SpawnSlime",
            true);
        spawnSlimeOverkillType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SpawnSlimeOverkill",
            true);

        execute = FindExecute(spawnSlimeType);
        executeOverkill = FindExecute(spawnSlimeOverkillType);
        createEntities = FindDeclaredMethod(spawnSlimeType, "CreateEntities", 1);
        spawnSlimes = FindDeclaredMethod(spawnSlimeType, "SpawnSlimes", 2);
        legacyPlayStateField = spawnSlimeType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        networkManagerSingleton = RuntimeReflection.RequireField(
            networkManagerType,
            "sSingelton");
        ownerField = RuntimeReflection.RequireField(spawnSlimeType, "mOwner");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");

        InstallDependencyStubs();
    }

    internal ScenarioResult PlayStateRelease(bool overkill)
    {
        object currentPlayState = NewUninitialized(playStateType);
        object suppliedPlayState = NewUninitialized(playStateType);
        object owner = CreateOwner(currentPlayState, false);
        object ability = NewUninitialized(overkill ? spawnSlimeOverkillType : spawnSlimeType);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(ability, null);
        ConfigureOfflineNetwork();
        SpawnSlimeProbe.Reset(true);

        bool result = InvokeExecute(
            overkill ? executeOverkill : execute,
            ability,
            owner,
            suppliedPlayState);

        bool retained = legacyPlayStateField != null && ReferenceEquals(
            legacyPlayStateField.GetValue(ability),
            suppliedPlayState);
        bool ownerPreserved = ReferenceEquals(ownerField.GetValue(ability), owner);
        bool passed = result && !retained && ownerPreserved &&
            SpawnSlimeProbe.CreateEntitiesCalls == 1 &&
            SpawnSlimeProbe.SpawnCount == 1;
        string actual = "result:" + result +
            ",state:" + (retained ? "retained" : "released") +
            ",owner:" + ownerPreserved +
            ",create_calls:" + SpawnSlimeProbe.CreateEntitiesCalls +
            ",spawn_count:" + SpawnSlimeProbe.SpawnCount;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,state:released,owner:True,create_calls:1,spawn_count:1");
    }

    internal ScenarioResult CurrentNavMesh()
    {
        object stalePlayState;
        object currentPlayState;
        object staleNavMesh;
        object currentNavMesh;
        ConfigurePlayStates(
            out stalePlayState,
            out currentPlayState,
            out staleNavMesh,
            out currentNavMesh);
        object owner = CreateOwner(currentPlayState, true);
        object ability = NewUninitialized(spawnSlimeType);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(ability, stalePlayState);
        ConfigureOfflineNetwork();
        SpawnSlimeProbe.Reset(false);

        bool reached = InvokeUntilNavMesh(
            delegate
            {
                InvokeExecute(execute, ability, owner, stalePlayState);
            });
        bool current = ReferenceEquals(
            SpawnSlimeProbe.ObservedNavMesh,
            currentNavMesh);
        return new ScenarioResult(
            reached && current,
            "reached:" + reached + ",nav_mesh:" + (current ? "current" : "stale"),
            "reached:True,nav_mesh:current");
    }

    internal ScenarioResult SpawnSlimesCurrentNavMesh()
    {
        object stalePlayState;
        object currentPlayState;
        object staleNavMesh;
        object currentNavMesh;
        ConfigurePlayStates(
            out stalePlayState,
            out currentPlayState,
            out staleNavMesh,
            out currentNavMesh);
        object owner = CreateOwner(currentPlayState, true);
        object ability = NewUninitialized(spawnSlimeType);
        ownerField.SetValue(ability, owner);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(ability, stalePlayState);
        ConfigureOfflineNetwork();
        SpawnSlimeProbe.Reset(false);

        bool reached = InvokeUntilNavMesh(
            delegate
            {
                Invoke(spawnSlimes, ability, new object[] { 1, 1 });
            });
        bool current = ReferenceEquals(
            SpawnSlimeProbe.ObservedNavMesh,
            currentNavMesh);
        return new ScenarioResult(
            reached && current,
            "reached:" + reached + ",nav_mesh:" + (current ? "current" : "stale"),
            "reached:True,nav_mesh:current");
    }

    private MethodInfo FindExecute(Type type)
    {
        MethodInfo method = type.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { ownerType, elementsType, playStateType },
            null);
        if (method == null || method.ReturnType != typeof(bool))
            throw new MissingMethodException(type.FullName, "Execute");
        return method;
    }

    private static MethodInfo FindDeclaredMethod(Type type, string name, int parameters)
    {
        MethodInfo result = null;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != name ||
                methods[index].GetParameters().Length != parameters)
                continue;
            if (result != null)
                throw new InvalidOperationException("Multiple " + name + " methods matched.");
            result = methods[index];
        }
        if (result == null)
            throw new MissingMethodException(type.FullName, name);
        return result;
    }

    private object CreateOwner(object playState, bool bodyRequired)
    {
        Type avatarType = magicka.GetType("Magicka.GameLogic.Entities.Avatar", true);
        object owner = NewUninitialized(avatarType);
        RuntimeReflection.WriteField(owner, "mPlayState", playState);
        if (bodyRequired)
        {
            object body = NewUninitialized(bodyType);
            bodyType.GetProperty("Position").SetValue(
                body,
                Activator.CreateInstance(vectorType),
                null);
            RuntimeReflection.WriteField(owner, "mBody", body);
        }
        return owner;
    }

    private void ConfigurePlayStates(
        out object stalePlayState,
        out object currentPlayState,
        out object staleNavMesh,
        out object currentNavMesh)
    {
        stalePlayState = NewUninitialized(playStateType);
        currentPlayState = NewUninitialized(playStateType);
        staleNavMesh = ConfigureLevelChain(stalePlayState);
        currentNavMesh = ConfigureLevelChain(currentPlayState);
        recentPlayStateField.SetValue(null, currentPlayState);
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

    private void ConfigureOfflineNetwork()
    {
        networkManagerSingleton.SetValue(null, NewUninitialized(networkManagerType));
    }

    private void InstallDependencyStubs()
    {
        MethodInfo getTemplate = characterTemplateType.GetMethod(
            "GetCachedTemplate",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { typeof(int) },
            null);
        MethodInfo getCharacterName = characterType.GetProperty(
            "Name",
            BindingFlags.Instance | BindingFlags.Public).GetGetMethod();
        MethodInfo getCharacter = nonPlayerCharacterType.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { playStateType },
            null);
        Type navMeshType = RuntimeReflection.RequireField(
            magicka.GetType("Magicka.Levels.LevelModel", true),
            "mNavMesh").FieldType;
        MethodInfo nearestPosition = null;
        MethodInfo nearestPositionCore = null;
        MethodInfo[] methods = navMeshType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "GetNearestPosition" &&
                methods[index].GetParameters().Length == 3)
            {
                if (nearestPosition != null)
                    throw new InvalidOperationException(
                        "Multiple NavMesh.GetNearestPosition methods matched.");
                nearestPosition = methods[index];
            }
            else if (methods[index].Name == "GetNearestPosition" &&
                methods[index].GetParameters().Length == 5)
            {
                if (nearestPositionCore != null)
                    throw new InvalidOperationException(
                        "Multiple core NavMesh.GetNearestPosition methods matched.");
                nearestPositionCore = methods[index];
            }
        }
        if (getTemplate == null || getCharacterName == null ||
            getCharacter == null || nearestPosition == null ||
            nearestPositionCore == null)
            throw new MissingMethodException("SpawnSlime test dependencies are incomplete.");

        SpawnSlimeProbe.Template = NewUninitialized(characterTemplateType);
        SpawnSlimeProbe.Character = NewUninitialized(nonPlayerCharacterType);
        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-spawn-slime");
        harmony.Patch(
            createEntities,
            new HarmonyMethod(typeof(SpawnSlimeProbe).GetMethod("CreateEntitiesPrefix")),
            null,
            null);
        harmony.Patch(
            getTemplate,
            new HarmonyMethod(
                typeof(SpawnSlimeProbe).GetMethod("GetTemplatePrefix").MakeGenericMethod(
                    new Type[] { characterTemplateType })),
            null,
            null);
        harmony.Patch(
            getCharacterName,
            new HarmonyMethod(typeof(SpawnSlimeProbe).GetMethod("CharacterNamePrefix")),
            null,
            null);
        harmony.Patch(
            getCharacter,
            new HarmonyMethod(
                typeof(SpawnSlimeProbe).GetMethod("GetCharacterPrefix").MakeGenericMethod(
                    new Type[] { nonPlayerCharacterType })),
            null,
            null);
        harmony.Patch(
            nearestPosition,
            new HarmonyMethod(typeof(SpawnSlimeProbe).GetMethod("NavMeshPrefix")),
            null,
            null);
        harmony.Patch(
            nearestPositionCore,
            new HarmonyMethod(typeof(SpawnSlimeProbe).GetMethod("NavMeshPrefix")),
            null,
            null);
    }

    private bool InvokeExecute(
        MethodInfo method,
        object ability,
        object owner,
        object playState)
    {
        object elements = Enum.ToObject(elementsType, 0);
        return (bool)Invoke(
            method,
            ability,
            new object[] { owner, elements, playState });
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

    private static bool InvokeUntilNavMesh(System.Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (SpawnSlimeNavMeshReachedException)
        {
            return true;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class SpawnSlimeProbe
{
    public static object Character;
    public static object Template;
    public static int CreateEntitiesCalls;
    public static int SpawnCount;
    public static object ObservedNavMesh;
    public static bool SkipCreateEntities;

    public static void Reset(bool skipCreateEntities)
    {
        CreateEntitiesCalls = 0;
        SpawnCount = 0;
        ObservedNavMesh = null;
        SkipCreateEntities = skipCreateEntities;
    }

    public static bool CreateEntitiesPrefix(object entitiesToSpawn)
    {
        CreateEntitiesCalls++;
        ICollection collection = entitiesToSpawn as ICollection;
        SpawnCount = collection == null ? -1 : collection.Count;
        return !SkipCreateEntities;
    }

    public static bool GetTemplatePrefix<TTemplate>(ref TTemplate __result)
    {
        __result = (TTemplate)Template;
        return false;
    }

    public static bool CharacterNamePrefix(ref string __result)
    {
        __result = "behavior_probe_owner";
        return false;
    }

    public static bool GetCharacterPrefix<TCharacter>(ref TCharacter __result)
    {
        __result = (TCharacter)Character;
        return false;
    }

    public static void NavMeshPrefix(object __instance)
    {
        ObservedNavMesh = __instance;
        throw new SpawnSlimeNavMeshReachedException();
    }
}

internal sealed class SpawnSlimeNavMeshReachedException : Exception
{
}
