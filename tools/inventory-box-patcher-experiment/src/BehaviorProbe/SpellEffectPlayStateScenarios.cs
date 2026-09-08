using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class SpellEffectPlayStateScenarios
{
    internal static void Prepare(Assembly magicka)
    {
        SpellEffectPlayStateHarness.InstallProbesEarly(magicka);
    }

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        SpellEffectPlayStateHarness harness =
            new SpellEffectPlayStateHarness(magicka);
        try
        {
            report.Add(
                "spell_effect.initialize_release",
                harness.InitializeRelease());
            report.Add(
                "lightning_spell.cached_current_play_state",
                harness.CachedCurrentPlayState());
            report.Add(
                "lightning_spell.empty_cache_current_play_state",
                harness.EmptyCacheCurrentPlayState());
            report.Add(
                "lightning_spell.cast_current_play_state",
                harness.CastCurrentPlayState());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class SpellEffectPlayStateHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-spell-effect-play-state";

    private readonly Type bodyType;
    private readonly Type casterType;
    private readonly Type lightningSpellType;
    private readonly Type ownerType;
    private readonly Type playStateType;
    private readonly Type spellEffectType;
    private readonly MethodInfo castUpdate;
    private readonly MethodInfo getFromCache;
    private readonly MethodInfo initializeCaches;
    private readonly ConstructorInfo lightningConstructor;
    private readonly FieldInfo cacheField;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo spellEffectsField;
    private readonly HarmonyInstance harmony;

    internal SpellEffectPlayStateHarness(Assembly magicka)
    {
        spellEffectType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellEffects.SpellEffect",
            true);
        lightningSpellType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellEffects.LightningSpell",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        casterType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        Type contentManagerType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Content.ContentManager");

        initializeCaches = spellEffectType.GetMethod(
            "IntializeCaches",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playStateType, contentManagerType },
            null);
        getFromCache = lightningSpellType.GetMethod(
            "GetFromCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        castUpdate = lightningSpellType.GetMethod(
            "CastUpdate",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[]
            {
                typeof(float),
                casterType,
                typeof(float).MakeByRefType()
            },
            null);
        lightningConstructor = lightningSpellType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        cacheField = RuntimeReflection.RequireField(
            lightningSpellType,
            "mCache");
        legacyPlayStateField = spellEffectType.GetField(
            "mPlayState",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        spellEffectsField = RuntimeReflection.RequireField(
            playStateType,
            "mSpellEffects");
        if (initializeCaches == null || getFromCache == null ||
            castUpdate == null || lightningConstructor == null)
            throw new MissingMemberException(
                "SpellEffect behavior contract is incomplete.");
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        SpellEffectPlayStateProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult InitializeRelease()
    {
        IList unusedEffects;
        object supplied = NewPlayState(out unusedEffects);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(null, null);
        SpellEffectPlayStateProbe.Reset();
        SpellEffectPlayStateProbe.Enabled = true;
        Invoke(
            initializeCaches,
            null,
            new object[] { supplied, null });
        SpellEffectPlayStateProbe.Enabled = false;

        bool retained = legacyPlayStateField != null &&
            ReferenceEquals(legacyPlayStateField.GetValue(null), supplied);
        bool passed = !retained &&
            SpellEffectPlayStateProbe.CacheInitializerCalls == 6;
        string actual = "play_state:" +
            (retained ? "retained" : "released") + ",cache_calls:" +
            SpellEffectPlayStateProbe.CacheInitializerCalls;
        return new ScenarioResult(
            passed,
            actual,
            "play_state:released,cache_calls:6");
    }

    internal ScenarioResult CachedCurrentPlayState()
    {
        IList currentEffects;
        object current = NewPlayState(out currentEffects);
        IList staleEffects;
        object stale = NewPlayState(out staleEffects);
        recentPlayStateField.SetValue(null, current);
        SetLegacyPlayState(stale);

        IList cache = NewCache();
        object cached = NewUninitialized(lightningSpellType);
        cache.Add(cached);
        object result = Invoke(getFromCache, null, new object[0]);
        bool currentAdded = currentEffects.Count == 1 &&
            ReferenceEquals(currentEffects[0], cached);
        bool passed = ReferenceEquals(result, cached) && cache.Count == 0 &&
            currentAdded && staleEffects.Count == 0;
        string actual = "same:" + ReferenceEquals(result, cached) +
            ",cache:" + cache.Count + ",current:" + currentEffects.Count +
            ",stale:" + staleEffects.Count;
        return new ScenarioResult(
            passed,
            actual,
            "same:True,cache:0,current:1,stale:0");
    }

    internal ScenarioResult EmptyCacheCurrentPlayState()
    {
        IList currentEffects;
        object current = NewPlayState(out currentEffects);
        IList staleEffects;
        object stale = NewPlayState(out staleEffects);
        recentPlayStateField.SetValue(null, current);
        SetLegacyPlayState(stale);
        IList cache = NewCache();

        object result = Invoke(getFromCache, null, new object[0]);
        bool currentAdded = currentEffects.Count == 1 &&
            ReferenceEquals(currentEffects[0], result);
        bool passed = result != null && cache.Count == 0 && currentAdded &&
            staleEffects.Count == 0;
        string actual = "created:" + (result != null) + ",cache:" +
            cache.Count + ",current:" + currentEffects.Count + ",stale:" +
            staleEffects.Count;
        return new ScenarioResult(
            passed,
            actual,
            "created:True,cache:0,current:1,stale:0");
    }

    internal ScenarioResult CastCurrentPlayState()
    {
        IList currentEffects;
        object current = NewPlayState(out currentEffects);
        IList staleEffects;
        object stale = NewPlayState(out staleEffects);
        object owner = NewUninitialized(ownerType);
        RuntimeReflection.WriteField(owner, "mPlayState", current);
        RuntimeReflection.WriteField(owner, "mBody", NewUninitialized(bodyType));
        object spell = lightningConstructor.Invoke(new object[0]);
        RuntimeReflection.WriteField(spell, "mTTL", 0f);
        RuntimeReflection.WriteField(spell, "mLightningsToCast", 1);
        RuntimeReflection.WriteField(spell, "mRange", 2f);
        RuntimeReflection.WriteField(spell, "mAllAround", false);
        RuntimeReflection.WriteField(spell, "mFromStaff", false);
        recentPlayStateField.SetValue(null, current);
        SetLegacyPlayState(stale);

        SpellEffectPlayStateProbe.Reset();
        SpellEffectPlayStateProbe.Lightning =
            SpellEffectPlayStateProbe.NewLightning();
        SpellEffectPlayStateProbe.Enabled = true;
        object[] arguments = new object[] { 0.1f, owner, 0f };
        Invoke(castUpdate, spell, arguments);
        SpellEffectPlayStateProbe.Enabled = false;

        bool usedCurrent = ReferenceEquals(
            SpellEffectPlayStateProbe.CastPlayState,
            current);
        bool passed = SpellEffectPlayStateProbe.GetLightningCalls == 1 &&
            SpellEffectPlayStateProbe.CastCalls == 1 && usedCurrent;
        string actual = "get_calls:" +
            SpellEffectPlayStateProbe.GetLightningCalls + ",cast_calls:" +
            SpellEffectPlayStateProbe.CastCalls + ",play_state:" +
            (usedCurrent ? "current" : "stale");
        return new ScenarioResult(
            passed,
            actual,
            "get_calls:1,cast_calls:1,play_state:current");
    }

    internal static void InstallProbesEarly(Assembly magicka)
    {
        string spellNamespace =
            "Magicka.GameLogic.Spells.SpellEffects.";
        string[][] cacheContracts = new string[][]
        {
            new string[] { "PushSpell", "IntializeCache" },
            new string[] { "SpraySpell", "IntializeCache" },
            new string[] { "ProjectileSpell", "InitializeCache" },
            new string[] { "RailGunSpell", "InitializeCache" },
            new string[] { "LightningSpell", "InitializeCache" },
            new string[] { "ShieldSpell", "InitializeCache" }
        };
        HarmonyInstance harmony = HarmonyInstance.Create(HarmonyOwner);
        for (int index = 0; index < cacheContracts.Length; index++)
        {
            Type type = magicka.GetType(
                spellNamespace + cacheContracts[index][0],
                true);
            MethodInfo method = type.GetMethod(
                cacheContracts[index][1],
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(int) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    type.FullName,
                    cacheContracts[index][1]);
            harmony.Patch(
                method,
                new HarmonyMethod(
                    typeof(SpellEffectPlayStateProbe).GetMethod(
                        "CacheInitializerPrefix")),
                null,
                null);
        }

        Type lightningBoltType = magicka.GetType(
            "Magicka.GameLogic.Spells.LightningBolt",
            true);
        Type playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        MethodInfo getLightning = lightningBoltType.GetMethod(
            "GetLightning",
            BindingFlags.Static | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        MethodInfo cast = null;
        MethodInfo[] methods = lightningBoltType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name != "Cast" ||
                methods[index].ReturnType != typeof(void) ||
                parameters.Length != 9 ||
                parameters[2].ParameterType != vectorType ||
                parameters[8].ParameterType != playStateType)
                continue;
            if (cast != null)
                throw new InvalidOperationException(
                    "Multiple LightningBolt cast targets matched.");
            cast = methods[index];
        }
        if (getLightning == null || cast == null)
            throw new MissingMethodException(
                "LightningSpell cast probe targets are incomplete.");

        SpellEffectPlayStateProbe.LightningType = lightningBoltType;
        harmony.Patch(
            getLightning,
            new HarmonyMethod(
                typeof(SpellEffectPlayStateProbe).GetMethod(
                    "GetLightningPrefix").MakeGenericMethod(
                        new Type[] { lightningBoltType })),
            null,
            null);
        harmony.Patch(
            cast,
            new HarmonyMethod(
                typeof(SpellEffectPlayStateProbe).GetMethod("CastPrefix")),
            null,
            null);
    }

    private IList NewCache()
    {
        IList cache = (IList)Activator.CreateInstance(cacheField.FieldType);
        cacheField.SetValue(null, cache);
        return cache;
    }

    private object NewPlayState(out IList effects)
    {
        object playState = NewUninitialized(playStateType);
        effects = (IList)Activator.CreateInstance(spellEffectsField.FieldType);
        spellEffectsField.SetValue(playState, effects);
        return playState;
    }

    private void SetLegacyPlayState(object playState)
    {
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(null, playState);
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

public static class SpellEffectPlayStateProbe
{
    public static bool Enabled;
    public static Type LightningType;
    public static object Lightning;
    public static object CastPlayState;
    public static int CacheInitializerCalls;
    public static int GetLightningCalls;
    public static int CastCalls;

    public static void Reset()
    {
        Enabled = false;
        Lightning = null;
        CastPlayState = null;
        CacheInitializerCalls = 0;
        GetLightningCalls = 0;
        CastCalls = 0;
    }

    public static object NewLightning()
    {
        object value = FormatterServices.GetUninitializedObject(LightningType);
        GC.SuppressFinalize(value);
        return value;
    }

    public static bool CacheInitializerPrefix()
    {
        if (!Enabled)
            return true;
        CacheInitializerCalls++;
        return false;
    }

    public static bool GetLightningPrefix<TLightning>(
        ref TLightning __result)
    {
        if (!Enabled)
            return true;
        GetLightningCalls++;
        __result = (TLightning)Lightning;
        return false;
    }

    public static bool CastPrefix(object iState)
    {
        if (!Enabled)
            return true;
        CastCalls++;
        CastPlayState = iState;
        return false;
    }
}
