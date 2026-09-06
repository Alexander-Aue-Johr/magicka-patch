using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class DrainLifeScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        DrainLifeHarness harness = new DrainLifeHarness(magicka);
        report.Add("drain_life.play_state_release", harness.PlayStateRelease());
        report.Add("drain_life.execute_behavior", harness.ExecuteBehavior());
    }
}

internal sealed class DrainLifeHarness
{
    private readonly Type avatarType;
    private readonly Type bodyType;
    private readonly Type drainLifeType;
    private readonly Type entityManagerType;
    private readonly Type entityType;
    private readonly Type playStateType;
    private readonly Type vectorType;
    private readonly MethodInfo execute;
    private readonly FieldInfo lifeStealField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo ttlField;

    internal DrainLifeHarness(Assembly magicka)
    {
        avatarType = magicka.GetType("Magicka.GameLogic.Entities.Avatar", true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        drainLifeType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.DrainLife",
            true);
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        playStateType = magicka.GetType("Magicka.GameLogic.GameStates.PlayState", true);
        vectorType = RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Vector3");
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        execute = drainLifeType.GetMethod(
            "Execute",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { ownerType, playStateType },
            null);
        if (execute == null)
            throw new MissingMethodException(drainLifeType.FullName, "Execute");
        lifeStealField = RuntimeReflection.RequireField(drainLifeType, "LifeStealAmount");
        ownerField = RuntimeReflection.RequireField(drainLifeType, "mOwner");
        playStateField = drainLifeType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        ttlField = RuntimeReflection.RequireField(drainLifeType, "mTTL");
        InstallDependencyStubs(magicka);
    }

    internal ScenarioResult PlayStateRelease()
    {
        DrainLifeExecution execution = CreateExecution();
        bool result = Invoke(execution);
        bool retained = playStateField != null &&
            ReferenceEquals(playStateField.GetValue(execution.Effect), execution.PlayState);
        return new ScenarioResult(
            result && !retained,
            "result:" + result + ",state:" + (retained ? "retained" : "released"),
            "result:True,state:released");
    }

    internal ScenarioResult ExecuteBehavior()
    {
        DrainLifeExecution execution = CreateExecution();
        DrainLifeProbe.DamageCalls = 0;
        bool result = Invoke(execution);
        bool owner = ReferenceEquals(ownerField.GetValue(execution.Effect), execution.Owner);
        float ttl = (float)ttlField.GetValue(execution.Effect);
        float stolen = (float)lifeStealField.GetValue(execution.Effect);
        int returned = (int)execution.QueryLists.GetType().GetProperty("Count").GetValue(
            execution.QueryLists,
            null);
        bool passed = result && owner && Math.Abs(ttl - 1f) < 0.0001f &&
            Math.Abs(stolen - 50f) < 0.0001f &&
            DrainLifeProbe.DamageCalls == 1 && returned == 1;
        string actual = "result:" + result + ",owner:" + owner +
            ",ttl:" + ttl + ",stolen:" + stolen +
            ",damage:" + DrainLifeProbe.DamageCalls + ",returned:" + returned;
        return new ScenarioResult(
            passed,
            actual,
            "result:True,owner:True,ttl:1,stolen:50,damage:1,returned:1");
    }

    private DrainLifeExecution CreateExecution()
    {
        object effect = NewUninitialized(drainLifeType);
        object owner = NewUninitialized(avatarType);
        object target = NewUninitialized(avatarType);
        object playState = NewUninitialized(playStateType);
        object manager = NewUninitialized(entityManagerType);
        SetCharacterState(owner, playState, 0f, 50f, 100f);
        SetCharacterState(target, playState, 1f, 100f, 100f);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        object queryLists = CreateQueryListCache();
        RuntimeReflection.WriteField(manager, "mQuaryLists", queryLists);
        DrainLifeProbe.EntitiesResult = CreateEntityList(target);
        ttlField.SetValue(effect, 0.5f);
        return new DrainLifeExecution(
            effect,
            owner,
            playState,
            queryLists);
    }

    private void SetCharacterState(
        object character,
        object playState,
        float x,
        float hitPoints,
        float maxHitPoints)
    {
        object body = NewUninitialized(bodyType);
        object position = Activator.CreateInstance(vectorType);
        vectorType.GetField("X").SetValue(position, x);
        bodyType.GetProperty("Position").SetValue(body, position, null);
        RuntimeReflection.WriteField(character, "mBody", body);
        RuntimeReflection.WriteField(character, "mPlayState", playState);
        RuntimeReflection.WriteField(character, "mRadius", 1f);
        RuntimeReflection.WriteField(character, "mHitPoints", hitPoints);
        RuntimeReflection.WriteField(character, "mMaxHitPoints", maxHitPoints);
    }

    private object CreateEntityList(object target)
    {
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(
            new Type[] { entityType });
        object list = Activator.CreateInstance(listType);
        listType.GetMethod("Add").Invoke(list, new object[] { target });
        return list;
    }

    private object CreateQueryListCache()
    {
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(
            new Type[] { entityType });
        Type queueType = typeof(System.Collections.Generic.Queue<>).MakeGenericType(
            new Type[] { listType });
        return Activator.CreateInstance(queueType);
    }

    private bool Invoke(DrainLifeExecution execution)
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

    private void InstallDependencyStubs(Assembly magicka)
    {
        MethodInfo getEntities = null;
        MethodInfo[] managerMethods = entityManagerType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        for (int index = 0; index < managerMethods.Length; index++)
        {
            ParameterInfo[] parameters = managerMethods[index].GetParameters();
            if (managerMethods[index].Name == "GetEntities" && parameters.Length == 4)
                getEntities = managerMethods[index];
        }
        Type characterType = magicka.GetType("Magicka.GameLogic.Entities.Character", true);
        MethodInfo damage = null;
        MethodInfo[] characterMethods = characterType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        for (int index = 0; index < characterMethods.Length; index++)
        {
            ParameterInfo[] parameters = characterMethods[index].GetParameters();
            if (characterMethods[index].Name == "Damage" &&
                parameters.Length == 2 && parameters[0].ParameterType == typeof(float))
                damage = characterMethods[index];
        }
        if (getEntities == null || damage == null)
            throw new MissingMethodException("DrainLife test dependencies are incomplete.");

        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-drain-life");
        harmony.Patch(
            getEntities,
            new HarmonyMethod(
                typeof(DrainLifeProbe).GetMethod("GetEntitiesPrefix").MakeGenericMethod(
                    new Type[] { getEntities.ReturnType })),
            null,
            null);
        harmony.Patch(
            damage,
            new HarmonyMethod(typeof(DrainLifeProbe).GetMethod("DamagePrefix")),
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

internal sealed class DrainLifeExecution
{
    internal object Effect { get; private set; }
    internal object Owner { get; private set; }
    internal object PlayState { get; private set; }
    internal object QueryLists { get; private set; }

    internal DrainLifeExecution(
        object effect,
        object owner,
        object playState,
        object queryLists)
    {
        Effect = effect;
        Owner = owner;
        PlayState = playState;
        QueryLists = queryLists;
    }
}

public static class DrainLifeProbe
{
    public static object EntitiesResult;
    public static int DamageCalls;

    public static bool GetEntitiesPrefix<TList>(ref TList __result)
    {
        __result = (TList)EntitiesResult;
        return false;
    }

    public static bool DamagePrefix()
    {
        DamageCalls++;
        return false;
    }
}
