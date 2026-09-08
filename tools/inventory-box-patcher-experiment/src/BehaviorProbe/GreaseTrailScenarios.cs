using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class GreaseTrailScenarios
{
    internal static void Prepare(Assembly magicka)
    {
        GreaseTrailHarness.InstallProbesEarly(magicka);
    }

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        GreaseTrailHarness harness = new GreaseTrailHarness(magicka);
        try
        {
            report.Add(
                "grease_trail.play_state_release",
                harness.PlayStateRelease());
            report.Add(
                "grease_trail.current_play_state",
                harness.CurrentPlayState());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class GreaseTrailHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-grease-trail";

    private readonly Type bodyType;
    private readonly Type dataChannelType;
    private readonly Type entityManagerType;
    private readonly Type ownerType;
    private readonly Type playStateType;
    private readonly Type trailType;
    private readonly MethodInfo execute;
    private readonly MethodInfo update;
    private readonly FieldInfo intervalField;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo ttlField;
    private readonly HarmonyInstance harmony;

    internal GreaseTrailHarness(Assembly magicka)
    {
        trailType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                "GreaseTrail",
            true);
        ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        Type casterType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        execute = trailType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { casterType, playStateType },
            null);
        update = trailType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (execute == null || update == null)
            throw new MissingMethodException(
                "GreaseTrail behavior targets are incomplete.");

        intervalField = RuntimeReflection.RequireField(trailType, "mInterval");
        legacyPlayStateField = trailType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        ownerField = RuntimeReflection.RequireField(trailType, "mOwner");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        ttlField = RuntimeReflection.RequireField(trailType, "mTTL");
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        GreaseTrailProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult PlayStateRelease()
    {
        object trail = NewUninitialized(trailType);
        object supplied = NewPlayState(NewUninitialized(entityManagerType));
        object owner = NewOwner(supplied);
        ConfigureNetwork("Offline");
        GreaseTrailProbe.Reset();
        GreaseTrailProbe.Enabled = true;

        bool result = (bool)Invoke(
            execute,
            trail,
            new object[] { owner, supplied });
        GreaseTrailProbe.Enabled = false;

        bool retained = legacyPlayStateField != null &&
            ReferenceEquals(legacyPlayStateField.GetValue(trail), supplied);
        bool ownerAssigned = ReferenceEquals(ownerField.GetValue(trail), owner);
        float ttl = Convert.ToSingle(ttlField.GetValue(trail));
        bool passed = result && ownerAssigned && !retained &&
            GreaseTrailProbe.EffectCalls == 1 &&
            ReferenceEquals(GreaseTrailProbe.Effect, trail) &&
            Math.Abs(ttl - 20f) < 0.0001f;
        string actual = "result:" + result + ",owner:" + ownerAssigned +
            ",play_state:" + (retained ? "retained" : "released") +
            ",effect_calls:" + GreaseTrailProbe.EffectCalls +
            ",ttl:" + ttl;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,owner:True,play_state:released,effect_calls:1,ttl:20");
    }

    internal ScenarioResult CurrentPlayState()
    {
        object trail = NewUninitialized(trailType);
        object currentManager = NewUninitialized(entityManagerType);
        object staleManager = NewUninitialized(entityManagerType);
        object current = NewPlayState(currentManager);
        object stale = NewPlayState(staleManager);
        object owner = NewOwner(current);
        object field = GreaseTrailProbe.NewGreaseField();
        ownerField.SetValue(trail, owner);
        ttlField.SetValue(trail, 20f);
        intervalField.SetValue(trail, 0f);
        recentPlayStateField.SetValue(null, current);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(trail, stale);
        ConfigureNetwork("Offline");
        GreaseTrailProbe.Reset();
        GreaseTrailProbe.GreaseField = field;
        GreaseTrailProbe.Enabled = true;

        Invoke(
            update,
            trail,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.1f });
        GreaseTrailProbe.Enabled = false;

        bool usedCurrent = ReferenceEquals(
            GreaseTrailProbe.ObservedPlayState,
            current);
        bool managerCurrent = ReferenceEquals(
            GreaseTrailProbe.ObservedManager,
            currentManager);
        bool sameField = ReferenceEquals(
            GreaseTrailProbe.AddedEntity,
            field);
        bool passed = GreaseTrailProbe.GetInstanceCalls == 1 &&
            GreaseTrailProbe.InitializeCalls == 1 &&
            GreaseTrailProbe.AddEntityCalls == 1 && usedCurrent &&
            managerCurrent && sameField;
        string actual = "get_calls:" +
            GreaseTrailProbe.GetInstanceCalls + ",init_calls:" +
            GreaseTrailProbe.InitializeCalls + ",add_calls:" +
            GreaseTrailProbe.AddEntityCalls + ",play_state:" +
            (usedCurrent ? "current" : "stale") + ",manager:" +
            (managerCurrent ? "current" : "stale") + ",entity:" +
            (sameField ? "same" : "changed");
        return new ScenarioResult(
            passed,
            actual,
            "get_calls:1,init_calls:1,add_calls:1,play_state:current," +
                "manager:current,entity:same");
    }

    internal static void InstallProbesEarly(Assembly magicka)
    {
        Type trailType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                "GreaseTrail",
            true);
        Type greaseType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease",
            true);
        Type greaseFieldType = greaseType.GetNestedType(
            "GreaseField",
            BindingFlags.Public);
        Type playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        Type entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        Type networkManagerType = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        Type casterType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type animatedPartType = magicka.GetType(
            "Magicka.Levels.AnimatedLevelPart",
            true);
        Type vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");

        MethodInfo getNetworkManager = networkManagerType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();
        MethodInfo getNetworkState = networkManagerType.GetProperty(
            "State",
            BindingFlags.Instance | BindingFlags.Public).GetGetMethod();
        MethodInfo addEffect = spellManagerType.GetMethod(
            "AddSpellEffect",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { trailType.GetInterface("IAbilityEffect") },
            null);
        MethodInfo getGrease = greaseFieldType.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playStateType },
            null);
        MethodInfo initialize = greaseFieldType.GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[]
            {
                casterType,
                animatedPartType,
                vectorType.MakeByRefType(),
                vectorType.MakeByRefType()
            },
            null);
        MethodInfo addEntity = entityManagerType.GetMethod(
            "AddEntity",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { entityType },
            null);
        if (greaseFieldType == null || getNetworkManager == null ||
            getNetworkState == null || addEffect == null ||
            getGrease == null || initialize == null || addEntity == null)
            throw new MissingMethodException(
                "GreaseTrail probe targets are incomplete.");

        GreaseTrailProbe.GreaseFieldType = greaseFieldType;
        HarmonyInstance harmony = HarmonyInstance.Create(HarmonyOwner);
        harmony.Patch(
            getNetworkManager,
            new HarmonyMethod(
                typeof(GreaseTrailProbe).GetMethod("NetworkManagerPrefix")
                    .MakeGenericMethod(new Type[] { networkManagerType })),
            null,
            null);
        harmony.Patch(
            getNetworkState,
            new HarmonyMethod(
                typeof(GreaseTrailProbe).GetMethod("NetworkStatePrefix")
                    .MakeGenericMethod(new Type[] { getNetworkState.ReturnType })),
            null,
            null);
        harmony.Patch(
            addEffect,
            new HarmonyMethod(
                typeof(GreaseTrailProbe).GetMethod("EffectPrefix")),
            null,
            null);
        harmony.Patch(
            getGrease,
            new HarmonyMethod(
                typeof(GreaseTrailProbe).GetMethod("GetInstancePrefix")
                    .MakeGenericMethod(new Type[] { greaseFieldType })),
            null,
            null);
        harmony.Patch(
            initialize,
            new HarmonyMethod(
                typeof(GreaseTrailProbe).GetMethod("InitializePrefix")),
            null,
            null);
        harmony.Patch(
            addEntity,
            new HarmonyMethod(
                typeof(GreaseTrailProbe).GetMethod("AddEntityPrefix")),
            null,
            null);
    }

    private void ConfigureNetwork(string stateName)
    {
        GreaseTrailProbe.NetworkManager = NewUninitialized(
            RuntimeReflection.FindLoadedType("Magicka.Network.NetworkManager"));
        Type stateType = GreaseTrailProbe.NetworkManager.GetType()
            .GetProperty("State").PropertyType;
        GreaseTrailProbe.NetworkState = Enum.Parse(stateType, stateName);
    }

    private object NewOwner(object playState)
    {
        object owner = NewUninitialized(ownerType);
        RuntimeReflection.WriteField(owner, "mPlayState", playState);
        RuntimeReflection.WriteField(owner, "mBody", NewUninitialized(bodyType));
        return owner;
    }

    private object NewPlayState(object manager)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        return playState;
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

public static class GreaseTrailProbe
{
    public static bool Enabled;
    public static Type GreaseFieldType;
    public static object NetworkManager;
    public static object NetworkState;
    public static object GreaseField;
    public static object ObservedPlayState;
    public static object ObservedManager;
    public static object AddedEntity;
    public static object Effect;
    public static int GetInstanceCalls;
    public static int InitializeCalls;
    public static int AddEntityCalls;
    public static int EffectCalls;

    public static void Reset()
    {
        Enabled = false;
        GreaseField = null;
        ObservedPlayState = null;
        ObservedManager = null;
        AddedEntity = null;
        Effect = null;
        GetInstanceCalls = 0;
        InitializeCalls = 0;
        AddEntityCalls = 0;
        EffectCalls = 0;
    }

    public static object NewGreaseField()
    {
        object value = FormatterServices.GetUninitializedObject(GreaseFieldType);
        GC.SuppressFinalize(value);
        return value;
    }

    public static bool NetworkManagerPrefix<TManager>(ref TManager __result)
    {
        if (!Enabled)
            return true;
        __result = (TManager)NetworkManager;
        return false;
    }

    public static bool NetworkStatePrefix<TState>(ref TState __result)
    {
        if (!Enabled)
            return true;
        __result = (TState)NetworkState;
        return false;
    }

    public static bool EffectPrefix(object iSpellEffect)
    {
        if (!Enabled)
            return true;
        EffectCalls++;
        Effect = iSpellEffect;
        return false;
    }

    public static bool GetInstancePrefix<TGrease>(
        object iPlayState,
        ref TGrease __result)
    {
        if (!Enabled)
            return true;
        GetInstanceCalls++;
        ObservedPlayState = iPlayState;
        __result = (TGrease)GreaseField;
        return false;
    }

    public static bool InitializePrefix()
    {
        if (!Enabled)
            return true;
        InitializeCalls++;
        return false;
    }

    public static bool AddEntityPrefix(object __instance, object iEntity)
    {
        if (!Enabled)
            return true;
        AddEntityCalls++;
        ObservedManager = __instance;
        AddedEntity = iEntity;
        return false;
    }
}
