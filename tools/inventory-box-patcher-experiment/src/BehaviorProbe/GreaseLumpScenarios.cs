using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class GreaseLumpScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        GreaseLumpHarness harness = new GreaseLumpHarness(magicka);
        try
        {
            report.Add(
                "grease_lump.current_play_state",
                harness.CurrentPlayState());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class GreaseLumpHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-grease-lump";

    private readonly Type lumpType;
    private readonly Type playStateType;
    private readonly Type entityManagerType;
    private readonly Type missileType;
    private readonly Type bodyType;
    private readonly Type vectorType;
    private readonly Type dataChannelType;
    private readonly MethodInfo update;
    private readonly FieldInfo playStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo missileField;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo ttlField;
    private readonly FieldInfo intervalField;
    private readonly object originalRecentPlayState;
    private readonly HarmonyInstance harmony;

    internal GreaseLumpHarness(Assembly magicka)
    {
        lumpType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                "GreaseLump",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        missileType = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity",
            true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        update = lumpType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (update == null || update.ReturnType != typeof(void))
            throw new MissingMethodException(lumpType.FullName, "Update");

        playStateField = lumpType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        recentPlayStateField = RequireField(
            playStateType,
            "sRecentPlayState");
        missileField = RequireField(lumpType, "mMissile");
        ownerField = RequireField(lumpType, "mOwner");
        ttlField = RequireField(lumpType, "mTTL");
        intervalField = RequireField(lumpType, "mInterval");
        originalRecentPlayState = recentPlayStateField.GetValue(null);
        harmony = InstallStubs(magicka);
    }

    internal void Dispose()
    {
        GreaseLumpProbe.Reset();
        harmony.UnpatchAll(HarmonyOwner);
        recentPlayStateField.SetValue(null, originalRecentPlayState);
    }

    internal ScenarioResult CurrentPlayState()
    {
        object currentManager = NewUninitialized(entityManagerType);
        object staleManager = NewUninitialized(entityManagerType);
        object current = NewPlayState(currentManager);
        object stale = NewPlayState(staleManager);
        object field = GreaseLumpProbe.NewField();
        object lump = NewUninitialized(lumpType);
        object missile = NewMissile();

        recentPlayStateField.SetValue(null, current);
        if (playStateField != null)
            playStateField.SetValue(lump, stale);
        missileField.SetValue(lump, missile);
        ownerField.SetValue(lump, null);
        ttlField.SetValue(lump, 1f);
        intervalField.SetValue(lump, -0.01f);
        GreaseLumpProbe.Reset();
        GreaseLumpProbe.Field = field;
        GreaseLumpProbe.NetworkManager = NewUninitialized(
            RuntimeReflection.FindLoadedType("Magicka.Network.NetworkManager"));
        Type stateType = GreaseLumpProbe.NetworkManager.GetType()
            .GetProperty("State").PropertyType;
        GreaseLumpProbe.NetworkState = Enum.Parse(stateType, "Offline");
        GreaseLumpProbe.Enabled = true;

        Exception failure = null;
        try
        {
            Invoke(
                update,
                lump,
                new object[]
                {
                    Enum.ToObject(dataChannelType, 0),
                    0.01f
                });
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        GreaseLumpProbe.Enabled = false;

        bool currentState = ReferenceEquals(
            GreaseLumpProbe.ObservedPlayState,
            current);
        bool currentManagerUsed = ReferenceEquals(
            GreaseLumpProbe.ObservedManager,
            currentManager);
        bool fieldPreserved = ReferenceEquals(
            GreaseLumpProbe.AddedEntity,
            field);
        bool passed = failure == null &&
            GreaseLumpProbe.GetInstanceCalls == 1 &&
            GreaseLumpProbe.InitializeCalls == 1 &&
            GreaseLumpProbe.AddEntityCalls == 1 &&
            currentState && currentManagerUsed && fieldPreserved;
        return new ScenarioResult(
            passed,
            "exception:" +
                (failure == null ? "none" : failure.GetType().FullName) +
                ",play_state:" + (currentState ? "current" : "stale") +
                ",manager:" +
                (currentManagerUsed ? "current" : "stale") +
                ",field:" + (fieldPreserved ? "same" : "changed"),
            "exception:none,play_state:current,manager:current,field:same");
    }

    private object NewPlayState(object manager)
    {
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        return playState;
    }

    private object NewMissile()
    {
        object missile = NewUninitialized(missileType);
        object body = NewUninitialized(bodyType);
        PropertyInfo position = bodyType.GetProperty(
            "Position",
            BindingFlags.Instance | BindingFlags.Public);
        if (position == null || !position.CanWrite)
            throw new MissingMemberException(bodyType.FullName, "Position");
        position.SetValue(
            body,
            Activator.CreateInstance(vectorType),
            null);
        RuntimeReflection.WriteField(missile, "mBody", body);
        return missile;
    }

    private HarmonyInstance InstallStubs(Assembly magicka)
    {
        Type grease = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease",
            true);
        Type field = grease.GetNestedType(
            "GreaseField",
            BindingFlags.Public | BindingFlags.NonPublic);
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type animatedPart = magicka.GetType(
            "Magicka.Levels.AnimatedLevelPart",
            true);
        Type entity = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        Type networkManager = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);

        MethodInfo getInstance = field.GetMethod(
            "GetInstance",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playStateType },
            null);
        MethodInfo initialize = field.GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[]
            {
                owner,
                animatedPart,
                vectorType.MakeByRefType(),
                vectorType.MakeByRefType()
            },
            null);
        MethodInfo addEntity = entityManagerType.GetMethod(
            "AddEntity",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { entity },
            null);
        MethodInfo managerGetter = networkManager.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetGetMethod();
        MethodInfo stateGetter = networkManager.GetProperty(
            "State",
            BindingFlags.Instance | BindingFlags.Public).GetGetMethod();
        if (field == null || getInstance == null || initialize == null ||
            addEntity == null || managerGetter == null || stateGetter == null)
            throw new MissingMethodException(
                "GreaseLump dependency probes are incomplete.");

        GreaseLumpProbe.FieldType = field;
        HarmonyInstance instance = HarmonyInstance.Create(HarmonyOwner);
        instance.Patch(
            getInstance,
            new HarmonyMethod(
                typeof(GreaseLumpProbe).GetMethod(
                    "GetInstancePrefix").MakeGenericMethod(
                        new Type[] { field })),
            null,
            null);
        instance.Patch(
            initialize,
            new HarmonyMethod(
                typeof(GreaseLumpProbe).GetMethod("InitializePrefix")),
            null,
            null);
        instance.Patch(
            addEntity,
            new HarmonyMethod(
                typeof(GreaseLumpProbe).GetMethod("AddEntityPrefix")),
            null,
            null);
        instance.Patch(
            managerGetter,
            new HarmonyMethod(
                typeof(GreaseLumpProbe).GetMethod(
                    "NetworkManagerPrefix").MakeGenericMethod(
                        new Type[] { networkManager })),
            null,
            null);
        instance.Patch(
            stateGetter,
            new HarmonyMethod(
                typeof(GreaseLumpProbe).GetMethod(
                    "NetworkStatePrefix").MakeGenericMethod(
                        new Type[] { stateGetter.ReturnType })),
            null,
            null);
        return instance;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
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

public static class GreaseLumpProbe
{
    public static bool Enabled;
    public static Type FieldType;
    public static object Field;
    public static object NetworkManager;
    public static object NetworkState;
    public static object ObservedPlayState;
    public static object ObservedManager;
    public static object AddedEntity;
    public static int GetInstanceCalls;
    public static int InitializeCalls;
    public static int AddEntityCalls;

    public static void Reset()
    {
        Enabled = false;
        Field = null;
        NetworkManager = null;
        NetworkState = null;
        ObservedPlayState = null;
        ObservedManager = null;
        AddedEntity = null;
        GetInstanceCalls = 0;
        InitializeCalls = 0;
        AddEntityCalls = 0;
    }

    public static object NewField()
    {
        object value = FormatterServices.GetUninitializedObject(FieldType);
        GC.SuppressFinalize(value);
        return value;
    }

    public static bool GetInstancePrefix<TField>(
        object iPlayState,
        ref TField __result)
    {
        if (!Enabled)
            return true;
        GetInstanceCalls++;
        ObservedPlayState = iPlayState;
        __result = (TField)Field;
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
}
