using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class SummonPlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        SummonPlayStateHarness harness = new SummonPlayStateHarness(magicka);
        try
        {
            report.Add("summon_flamer.current_play_state", harness.CurrentPlayState(false));
            report.Add("summon_spirit.current_play_state", harness.CurrentPlayState(true));
            report.Add("summon_flamer.vector_release", harness.VectorRelease(false));
            report.Add("summon_flamer.owner_release", harness.OwnerRelease(false));
            report.Add("summon_spirit.vector_release", harness.VectorRelease(true));
            report.Add("summon_spirit.owner_release", harness.OwnerRelease(true));
            report.Add("summon_templates.level_dispose", harness.TemplateRelease());
            report.Add(
                "ability_template_cache.level_dispose",
                harness.AbilityTemplateRelease(true));
            report.Add(
                "ability_template_cache.empty_dispose",
                harness.AbilityTemplateRelease(false));
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class SummonPlayStateHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-summon-play-state";

    private readonly Assembly magicka;
    private readonly Type bodyType;
    private readonly Type characterTemplateType;
    private readonly Type networkManagerType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type ownerType;
    private readonly Type playStateType;
    private readonly Type vectorType;
    private readonly SummonAbilityFixture flamer;
    private readonly SummonAbilityFixture spirit;
    private readonly FieldInfo[] abilityTemplateFields;
    private readonly MethodInfo[] abilityDisposeMethods;
    private readonly FieldInfo networkManagerSingleton;
    private readonly FieldInfo recentPlayStateField;
    private readonly MethodInfo getNonPlayerCharacter;
    private readonly MethodInfo getNetworkState;
    private readonly MethodInfo getNetworkManager;
    private readonly HarmonyInstance harmony;

    internal SummonPlayStateHarness(Assembly magicka)
    {
        this.magicka = magicka;
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
        characterTemplateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        networkManagerType = magicka.GetType("Magicka.Network.NetworkManager", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        ownerType = magicka.GetType("Magicka.GameLogic.Entities.ISpellCaster", true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        networkManagerSingleton = RuntimeReflection.RequireField(
            networkManagerType,
            "sSingelton");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        getNonPlayerCharacter = nonPlayerCharacterType.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { playStateType },
            null);
        if (getNonPlayerCharacter == null)
            throw new MissingMethodException(nonPlayerCharacterType.FullName, "GetInstance");
        getNetworkState = networkManagerType.GetProperty(
            "State",
            BindingFlags.Instance | BindingFlags.Public).GetGetMethod();
        getNetworkManager = networkManagerType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();

        flamer = new SummonAbilityFixture(
            magicka,
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonFlamer",
            ownerType,
            playStateType,
            vectorType);
        spirit = new SummonAbilityFixture(
            magicka,
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonSpirit",
            ownerType,
            playStateType,
            vectorType);
        string[] abilityNames = new string[]
        {
            "SummonBug",
            "SummonElemental",
            "MutateBeastman",
            "OtherworldlyDischarge"
        };
        abilityTemplateFields = new FieldInfo[abilityNames.Length];
        abilityDisposeMethods = new MethodInfo[abilityNames.Length];
        for (int index = 0; index < abilityNames.Length; index++)
        {
            Type abilityType = magicka.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                    abilityNames[index],
                true);
            abilityTemplateFields[index] = RuntimeReflection.RequireField(
                abilityType,
                "sTemplate");
            abilityDisposeMethods[index] = abilityType.GetMethod(
                "DisposeCache",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
        }
        harmony = HarmonyInstance.Create(HarmonyOwner);
        harmony.Patch(
            getNonPlayerCharacter,
            new HarmonyMethod(
                typeof(SummonPlayStateProbe).GetMethod("GetCharacterPrefix")
                    .MakeGenericMethod(new Type[] { nonPlayerCharacterType })),
            null,
            null);
        harmony.Patch(
            getNetworkState,
            new HarmonyMethod(
                typeof(SummonPlayStateProbe).GetMethod("NetworkStatePrefix")
                    .MakeGenericMethod(new Type[] { getNetworkState.ReturnType })),
            null,
            null);
        harmony.Patch(
            getNetworkManager,
            new HarmonyMethod(
                typeof(SummonPlayStateProbe).GetMethod("NetworkManagerPrefix")
                    .MakeGenericMethod(new Type[] { networkManagerType })),
            null,
            null);
        InstallNavMeshStubs();
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult VectorRelease(bool useSpirit)
    {
        SummonAbilityFixture fixture = useSpirit ? spirit : flamer;
        object suppliedPlayState = NewUninitialized(playStateType);
        object ability = NewUninitialized(fixture.Type);
        if (fixture.LegacyPlayState != null)
            fixture.LegacyPlayState.SetValue(ability, null);

        ConfigureNetwork(2);
        SummonPlayStateProbe.Reset();
        bool result = (bool)Invoke(
            fixture.VectorExecute,
            ability,
            new object[] { Activator.CreateInstance(vectorType), suppliedPlayState });

        bool retained = fixture.LegacyPlayState != null && ReferenceEquals(
            fixture.LegacyPlayState.GetValue(ability),
            suppliedPlayState);
        bool ownerCleared = fixture.Owner.GetValue(ability) == null;
        bool passed = result && !retained && ownerCleared;
        return new ScenarioResult(
            passed,
            "result:" + result +
                ",state:" + (retained ? "retained" : "released") +
                ",owner_cleared:" + ownerCleared,
            "result:True,state:released,owner_cleared:True");
    }

    internal ScenarioResult OwnerRelease(bool useSpirit)
    {
        SummonAbilityFixture fixture = useSpirit ? spirit : flamer;
        object suppliedPlayState = NewUninitialized(playStateType);
        object owner = CreateOwner(suppliedPlayState);
        object ability = NewUninitialized(fixture.Type);
        if (fixture.LegacyPlayState != null)
            fixture.LegacyPlayState.SetValue(ability, null);

        ConfigureNetwork(2);
        SummonPlayStateProbe.Reset();
        bool result = (bool)Invoke(
            fixture.OwnerExecute,
            ability,
            new object[] { owner, suppliedPlayState });

        bool retained = fixture.LegacyPlayState != null && ReferenceEquals(
            fixture.LegacyPlayState.GetValue(ability),
            suppliedPlayState);
        bool ownerPreserved = ReferenceEquals(fixture.Owner.GetValue(ability), owner);
        bool passed = result && !retained && ownerPreserved;
        return new ScenarioResult(
            passed,
            "result:" + result +
                ",state:" + (retained ? "retained" : "released") +
                ",owner:" + ownerPreserved,
            "result:True,state:released,owner:True");
    }

    internal ScenarioResult CurrentPlayState(bool useSpirit)
    {
        SummonAbilityFixture fixture = useSpirit ? spirit : flamer;
        object stalePlayState;
        object currentPlayState;
        object staleNavMesh;
        object currentNavMesh;
        ConfigurePlayStates(
            out stalePlayState,
            out currentPlayState,
            out staleNavMesh,
            out currentNavMesh);
        object ability = NewUninitialized(fixture.Type);
        if (fixture.LegacyPlayState != null)
            fixture.LegacyPlayState.SetValue(ability, stalePlayState);
        recentPlayStateField.SetValue(null, currentPlayState);
        ConfigureNetwork(0);
        SummonPlayStateProbe.Character = NewUninitialized(nonPlayerCharacterType);
        SummonPlayStateProbe.Reset();

        bool reached = false;
        try
        {
            Invoke(
                fixture.PrivateExecute,
                ability,
                new object[] {
                    Activator.CreateInstance(vectorType),
                    Activator.CreateInstance(vectorType)
                });
        }
        catch (SummonPlayStateNavMeshReachedException)
        {
            reached = true;
        }
        bool current = ReferenceEquals(SummonPlayStateProbe.ObservedNavMesh, currentNavMesh);
        return new ScenarioResult(
            reached && current && SummonPlayStateProbe.GetCharacterCalls == 1,
            "reached:" + reached +
                ",nav_mesh:" + (current ? "current" : "stale") +
                ",get_character_calls:" + SummonPlayStateProbe.GetCharacterCalls,
            "reached:True,nav_mesh:current,get_character_calls:1");
    }

    internal ScenarioResult TemplateRelease()
    {
        object flamerTemplate = NewUninitialized(characterTemplateType);
        object spiritTemplate = NewUninitialized(characterTemplateType);
        flamer.Template.SetValue(null, flamerTemplate);
        spirit.Template.SetValue(null, spiritTemplate);

        if (flamer.DisposeCache != null && spirit.DisposeCache != null)
        {
            Invoke(flamer.DisposeCache, null, new object[0]);
            Invoke(spirit.DisposeCache, null, new object[0]);
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

        bool flamerReleased = flamer.Template.GetValue(null) == null;
        bool spiritReleased = spirit.Template.GetValue(null) == null;
        return new ScenarioResult(
            flamerReleased && spiritReleased,
            "flamer:" + (flamerReleased ? "released" : "retained") +
                ",spirit:" + (spiritReleased ? "released" : "retained"),
            "flamer:released,spirit:released");
    }

    internal ScenarioResult AbilityTemplateRelease(bool seedTemplates)
    {
        object template = seedTemplates
            ? NewUninitialized(characterTemplateType)
            : null;
        for (int index = 0; index < abilityTemplateFields.Length; index++)
            abilityTemplateFields[index].SetValue(null, template);

        bool hasExplicitCleanup = true;
        for (int index = 0; index < abilityDisposeMethods.Length; index++)
            hasExplicitCleanup &= abilityDisposeMethods[index] != null;
        if (hasExplicitCleanup)
        {
            for (int index = 0; index < abilityDisposeMethods.Length; index++)
                Invoke(abilityDisposeMethods[index], null, new object[0]);
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
                // The cleanup runs before the original method reaches game
                // singletons that are unavailable in this isolated harness.
            }
        }

        int remaining = 0;
        for (int index = 0; index < abilityTemplateFields.Length; index++)
        {
            if (abilityTemplateFields[index].GetValue(null) != null)
                remaining++;
            abilityTemplateFields[index].SetValue(null, null);
        }
        return new ScenarioResult(
            remaining == 0,
            "remaining:" + remaining,
            "remaining:0");
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
                new HarmonyMethod(typeof(SummonPlayStateProbe).GetMethod("NavMeshPrefix")),
                null,
                null);
            patched++;
        }
        if (patched != 2)
            throw new InvalidOperationException(
                "Expected two NavMesh.GetNearestPosition overloads, found " + patched + ".");
    }

    private void ConfigureNetwork(int state)
    {
        object networkManager = NewUninitialized(networkManagerType);
        networkManagerSingleton.SetValue(null, networkManager);
        SummonPlayStateProbe.NetworkManager = networkManager;
        SummonPlayStateProbe.NetworkState = Enum.ToObject(getNetworkState.ReturnType, state);
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
}

internal sealed class SummonAbilityFixture
{
    internal Type Type { get; private set; }
    internal FieldInfo LegacyPlayState { get; private set; }
    internal FieldInfo Owner { get; private set; }
    internal FieldInfo Template { get; private set; }
    internal MethodInfo VectorExecute { get; private set; }
    internal MethodInfo OwnerExecute { get; private set; }
    internal MethodInfo PrivateExecute { get; private set; }
    internal MethodInfo DisposeCache { get; private set; }

    internal SummonAbilityFixture(
        Assembly magicka,
        string typeName,
        Type ownerType,
        Type playStateType,
        Type vectorType)
    {
        Type = magicka.GetType(typeName, true);
        LegacyPlayState = Type.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Owner = RuntimeReflection.RequireField(Type, "mOwner");
        Template = RuntimeReflection.RequireField(Type, "sTemplate");
        VectorExecute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { vectorType, playStateType });
        OwnerExecute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { ownerType, playStateType });
        PrivateExecute = RequireMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            new Type[] { vectorType, vectorType });
        DisposeCache = Type.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
    }

    private MethodInfo RequireMethod(
        string name,
        BindingFlags flags,
        Type[] parameterTypes)
    {
        MethodInfo method = Type.GetMethod(name, flags, null, parameterTypes, null);
        if (method == null || method.ReturnType != typeof(bool))
            throw new MissingMethodException(Type.FullName, name);
        return method;
    }
}

public static class SummonPlayStateProbe
{
    public static int GetCharacterCalls;
    public static object NetworkManager;
    public static object NetworkState;
    public static object Character;
    public static object ObservedNavMesh;

    public static void Reset()
    {
        GetCharacterCalls = 0;
        ObservedNavMesh = null;
    }

    public static bool GetCharacterPrefix<TCharacter>(ref TCharacter __result)
    {
        GetCharacterCalls++;
        __result = (TCharacter)Character;
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

    public static void NavMeshPrefix(object __instance)
    {
        ObservedNavMesh = __instance;
        throw new SummonPlayStateNavMeshReachedException();
    }
}

internal sealed class SummonPlayStateNavMeshReachedException : Exception
{
}
