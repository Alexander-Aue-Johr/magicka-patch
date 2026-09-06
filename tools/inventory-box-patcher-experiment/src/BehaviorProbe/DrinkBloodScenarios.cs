using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class DrinkBloodScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        DrinkBloodHarness harness = new DrinkBloodHarness(magicka);
        report.Add("drink_blood.play_state_release", harness.PlayStateRelease());
        report.Add("drink_blood.execute_behavior", harness.ExecuteBehavior());
    }
}

internal sealed class DrinkBloodHarness
{
    private readonly Type ownerImplementationType;
    private readonly Type drinkBloodType;
    private readonly Type hasteType;
    private readonly Type playStateType;
    private readonly MethodInfo execute;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo targetField;
    private readonly FieldInfo ttlField;

    internal DrinkBloodHarness(Assembly magicka)
    {
        ownerImplementationType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        drinkBloodType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.DrinkBlood",
            true);
        hasteType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Haste",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        execute = drinkBloodType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { ownerType, playStateType },
            null);
        if (execute == null)
            throw new MissingMethodException(drinkBloodType.FullName, "Execute");

        ownerField = RuntimeReflection.RequireField(drinkBloodType, "mOwner");
        playStateField = drinkBloodType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        targetField = RuntimeReflection.RequireField(drinkBloodType, "mFoundTarget");
        ttlField = RuntimeReflection.RequireField(drinkBloodType, "mTTL");
        InstallDependencyStubs(magicka, ownerType);
    }

    internal ScenarioResult PlayStateRelease()
    {
        object effect = NewUninitialized(drinkBloodType);
        object owner = NewUninitialized(ownerImplementationType);
        object playState = NewUninitialized(playStateType);
        Invoke(effect, owner, playState);

        bool retained = playStateField != null &&
            ReferenceEquals(playStateField.GetValue(effect), playState);
        return new ScenarioResult(
            !retained,
            retained ? "retained" : "released",
            "released");
    }

    internal ScenarioResult ExecuteBehavior()
    {
        object effect = NewUninitialized(drinkBloodType);
        object owner = NewUninitialized(ownerImplementationType);
        object playState = NewUninitialized(playStateType);
        DrinkBloodProbe.Reset();
        bool result = Invoke(effect, owner, playState);

        bool ownerPreserved = ReferenceEquals(ownerField.GetValue(effect), owner);
        float ttl = (float)ttlField.GetValue(effect);
        bool foundTarget = (bool)targetField.GetValue(effect);
        bool passed = result && ownerPreserved && Math.Abs(ttl - 0.78f) < 0.0001f &&
            !foundTarget && DrinkBloodProbe.BaseExecuteCalls == 1 &&
            DrinkBloodProbe.AddEffectCalls == 1 &&
            DrinkBloodProbe.HasteGetCalls == 1 &&
            DrinkBloodProbe.HasteExecuteCalls == 1;
        string actual = "result:" + result +
            ",owner:" + ownerPreserved +
            ",ttl:" + ttl +
            ",target:" + foundTarget +
            ",base:" + DrinkBloodProbe.BaseExecuteCalls +
            ",add:" + DrinkBloodProbe.AddEffectCalls +
            ",haste_get:" + DrinkBloodProbe.HasteGetCalls +
            ",haste_execute:" + DrinkBloodProbe.HasteExecuteCalls;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,owner:True,ttl:0.78,target:False,base:1,add:1," +
                "haste_get:1,haste_execute:1");
    }

    private bool Invoke(object effect, object owner, object playState)
    {
        try
        {
            return (bool)execute.Invoke(effect, new object[] { owner, playState });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private void InstallDependencyStubs(Assembly magicka, Type ownerType)
    {
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        RuntimeReflection.RequireField(spellManagerType, "mSingelton").SetValue(
            null,
            NewUninitialized(spellManagerType));
        DrinkBloodProbe.HasteResult = NewUninitialized(hasteType);

        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-drink-blood");
        MethodInfo baseExecute = drinkBloodType.BaseType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { ownerType, playStateType },
            null);
        MethodInfo addEffect = spellManagerType.GetMethod(
            "AddSpellEffect",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo getHaste = hasteType.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public);
        MethodInfo hasteExecute = hasteType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { ownerType, playStateType, typeof(bool) },
            null);
        if (baseExecute == null || addEffect == null || getHaste == null ||
            hasteExecute == null)
            throw new MissingMethodException("DrinkBlood test dependencies are incomplete.");

        harmony.Patch(
            baseExecute,
            new HarmonyMethod(typeof(DrinkBloodProbe).GetMethod("BaseExecutePrefix")),
            null,
            null);
        harmony.Patch(
            addEffect,
            new HarmonyMethod(typeof(DrinkBloodProbe).GetMethod("AddEffectPrefix")),
            null,
            null);
        harmony.Patch(
            getHaste,
            new HarmonyMethod(
                typeof(DrinkBloodProbe).GetMethod("GetHastePrefix").MakeGenericMethod(
                    new Type[] { hasteType })),
            null,
            null);
        harmony.Patch(
            hasteExecute,
            new HarmonyMethod(typeof(DrinkBloodProbe).GetMethod("HasteExecutePrefix")),
            null,
            null);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class DrinkBloodProbe
{
    public static object HasteResult;
    public static int BaseExecuteCalls;
    public static int AddEffectCalls;
    public static int HasteGetCalls;
    public static int HasteExecuteCalls;

    public static void Reset()
    {
        BaseExecuteCalls = 0;
        AddEffectCalls = 0;
        HasteGetCalls = 0;
        HasteExecuteCalls = 0;
    }

    public static bool BaseExecutePrefix(ref bool __result)
    {
        BaseExecuteCalls++;
        __result = true;
        return false;
    }

    public static bool AddEffectPrefix()
    {
        AddEffectCalls++;
        return false;
    }

    public static bool GetHastePrefix<THaste>(ref THaste __result)
    {
        HasteGetCalls++;
        __result = (THaste)HasteResult;
        return false;
    }

    public static bool HasteExecutePrefix(ref bool __result)
    {
        HasteExecuteCalls++;
        __result = true;
        return false;
    }
}
