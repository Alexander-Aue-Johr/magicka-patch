using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class BreakBarriersScenarios
{
    internal static void Prepare(Assembly magicka)
    {
        BreakBarriersHarness.InstallProbesEarly(magicka);
    }

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        BreakBarriersHarness harness = new BreakBarriersHarness(magicka);
        try
        {
            report.Add(
                "break_barriers.play_state_release",
                harness.PlayStateRelease());
            report.Add(
                "break_barriers.current_entity_manager",
                harness.CurrentEntityManager());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class BreakBarriersHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-break-barriers";

    private readonly Type abilityType;
    private readonly Type bodyType;
    private readonly Type dataChannelType;
    private readonly Type entityManagerType;
    private readonly Type ownerType;
    private readonly Type playStateType;
    private readonly MethodInfo execute;
    private readonly MethodInfo queryEntities;
    private readonly MethodInfo update;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo timeField;
    private readonly FieldInfo ttlField;
    private readonly HarmonyInstance harmony;

    internal BreakBarriersHarness(Assembly magicka)
    {
        abilityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                "BreakBarriers",
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
        Type vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");

        execute = abilityType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { casterType, playStateType },
            null);
        update = abilityType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        queryEntities = entityManagerType.GetMethod(
            "GetEntities",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { vectorType, typeof(float), typeof(bool) },
            null);
        if (execute == null || update == null || queryEntities == null)
            throw new MissingMethodException(
                "BreakBarriers behavior targets are incomplete.");

        legacyPlayStateField = abilityType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        ownerField = RuntimeReflection.RequireField(abilityType, "mOwner");
        timeField = RuntimeReflection.RequireField(abilityType, "TIME");
        ttlField = RuntimeReflection.RequireField(abilityType, "mTTL");
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        BreakBarriersProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult PlayStateRelease()
    {
        object ability = NewAbility();
        object owner = NewUninitialized(ownerType);
        object supplied = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(owner, "mPlayState", supplied);
        BreakBarriersProbe.Reset();
        BreakBarriersProbe.Enabled = true;

        bool result = (bool)Invoke(
            execute,
            ability,
            new object[] { owner, supplied });

        bool retained = legacyPlayStateField != null &&
            ReferenceEquals(legacyPlayStateField.GetValue(ability), supplied);
        bool ownerAssigned = ReferenceEquals(ownerField.GetValue(ability), owner);
        float ttl = Convert.ToSingle(ttlField.GetValue(ability));
        bool passed = result && ownerAssigned && !retained &&
            BreakBarriersProbe.EffectCalls == 1 &&
            ReferenceEquals(BreakBarriersProbe.Effect, ability) &&
            Math.Abs(ttl - 5f) < 0.0001f;
        string actual = "result:" + result + ",owner:" + ownerAssigned +
            ",play_state:" + (retained ? "retained" : "released") +
            ",effect_calls:" + BreakBarriersProbe.EffectCalls +
            ",ttl:" + ttl;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,owner:True,play_state:released,effect_calls:1,ttl:5");
    }

    internal ScenarioResult CurrentEntityManager()
    {
        object ability = NewAbility();
        object owner = NewUninitialized(ownerType);
        RuntimeReflection.WriteField(owner, "mBody", NewUninitialized(bodyType));
        object currentManager = NewUninitialized(entityManagerType);
        object staleManager = NewUninitialized(entityManagerType);
        object current = NewPlayState(currentManager);
        object stale = NewPlayState(staleManager);
        RuntimeReflection.WriteField(owner, "mPlayState", current);
        ownerField.SetValue(ability, owner);
        ttlField.SetValue(ability, 5f);
        recentPlayStateField.SetValue(null, current);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(ability, stale);

        BreakBarriersProbe.Reset();
        BreakBarriersProbe.EntityList =
            Activator.CreateInstance(queryEntities.ReturnType);
        BreakBarriersProbe.Enabled = true;
        Invoke(
            update,
            ability,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.5f });

        bool queryCurrent = ReferenceEquals(
            BreakBarriersProbe.QueryManager,
            currentManager);
        bool returnCurrent = ReferenceEquals(
            BreakBarriersProbe.ReturnManager,
            currentManager);
        float ttl = Convert.ToSingle(ttlField.GetValue(ability));
        bool passed = queryCurrent && returnCurrent &&
            BreakBarriersProbe.QueryCalls == 1 &&
            BreakBarriersProbe.ReturnCalls == 1 &&
            Math.Abs(ttl - 4.5f) < 0.0001f;
        string actual = "query:" + (queryCurrent ? "current" : "stale") +
            ",return:" + (returnCurrent ? "current" : "stale") +
            ",query_calls:" + BreakBarriersProbe.QueryCalls +
            ",return_calls:" + BreakBarriersProbe.ReturnCalls +
            ",ttl:" + ttl;
        return new ScenarioResult(
            passed,
            actual,
            "query:current,return:current,query_calls:1,return_calls:1,ttl:4.5");
    }

    internal static void InstallProbesEarly(Assembly magicka)
    {
        Type abilityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                "BreakBarriers",
            true);
        Type entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        Type entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        Type vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(
            new Type[] { entityType });

        MethodInfo query = entityManagerType.GetMethod(
            "GetEntities",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { vectorType, typeof(float), typeof(bool) },
            null);
        MethodInfo returnList = entityManagerType.GetMethod(
            "ReturnEntityList",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { listType },
            null);
        MethodInfo addEffect = spellManagerType.GetMethod(
            "AddSpellEffect",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { abilityType.GetInterface("IAbilityEffect") },
            null);
        if (query == null || returnList == null || addEffect == null)
            throw new MissingMethodException(
                "BreakBarriers probe targets are incomplete.");

        HarmonyInstance harmony = HarmonyInstance.Create(HarmonyOwner);
        harmony.Patch(
            query,
            new HarmonyMethod(
                typeof(BreakBarriersProbe).GetMethod("QueryPrefix")
                    .MakeGenericMethod(new Type[] { listType })),
            null,
            null);
        harmony.Patch(
            returnList,
            new HarmonyMethod(
                typeof(BreakBarriersProbe).GetMethod("ReturnPrefix")),
            null,
            null);
        harmony.Patch(
            addEffect,
            new HarmonyMethod(
                typeof(BreakBarriersProbe).GetMethod("EffectPrefix")),
            null,
            null);
    }

    private object NewAbility()
    {
        object ability = NewUninitialized(abilityType);
        timeField.SetValue(ability, 5f);
        return ability;
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

public static class BreakBarriersProbe
{
    public static bool Enabled;
    public static object EntityList;
    public static object QueryManager;
    public static object ReturnManager;
    public static object Effect;
    public static int QueryCalls;
    public static int ReturnCalls;
    public static int EffectCalls;

    public static void Reset()
    {
        Enabled = false;
        EntityList = null;
        QueryManager = null;
        ReturnManager = null;
        Effect = null;
        QueryCalls = 0;
        ReturnCalls = 0;
        EffectCalls = 0;
    }

    public static bool QueryPrefix<TList>(
        object __instance,
        ref TList __result)
    {
        if (!Enabled)
            return true;
        QueryCalls++;
        QueryManager = __instance;
        __result = (TList)EntityList;
        return false;
    }

    public static bool ReturnPrefix(object __instance)
    {
        if (!Enabled)
            return true;
        ReturnCalls++;
        ReturnManager = __instance;
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
}
