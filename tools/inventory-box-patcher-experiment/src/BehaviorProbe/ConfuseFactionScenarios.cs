using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class ConfuseFactionScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ConfuseFactionHarness harness = new ConfuseFactionHarness(magicka);
        try
        {
            report.Add(
                "confuse.detached_target",
                harness.DetachedTarget());
            report.Add(
                "confuse.attached_target",
                harness.AttachedTarget());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class ConfuseFactionHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-confuse";

    private readonly Type confuseType;
    private readonly Type characterType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type templateType;
    private readonly Type factionsType;
    private readonly FieldInfo targetField;
    private readonly FieldInfo controllerField;
    private readonly FieldInfo characterFactionField;
    private readonly FieldInfo characterTemplateField;
    private readonly FieldInfo templateFactionField;
    private readonly FieldInfo cacheField;
    private readonly FieldInfo activeCacheField;
    private readonly FieldInfo effectField;
    private readonly FieldInfo effectManagerSingleton;
    private readonly FieldInfo networkManagerSingleton;
    private readonly MethodInfo onRemove;
    private readonly object originalCache;
    private readonly object originalActiveCache;
    private readonly object originalEffectManager;
    private readonly object originalNetworkManager;
    private readonly HarmonyInstance harmony;

    internal ConfuseFactionHarness(Assembly magicka)
    {
        confuseType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Confuse",
            true);
        characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        templateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        factionsType = magicka.GetType("Magicka.Factions", true);
        targetField = RequireField(confuseType, "mTarget");
        controllerField = RequireField(confuseType, "mControllerTarget");
        characterFactionField = RequireField(characterType, "mFaction");
        characterTemplateField = RequireField(characterType, "mTemplate");
        templateFactionField = RequireField(templateType, "mFaction");
        cacheField = RequireField(confuseType, "sCache");
        activeCacheField = RequireField(confuseType, "sActiveCaches");
        effectField = RequireField(confuseType, "mEffect");
        onRemove = confuseType.GetMethod(
            "OnRemove",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (onRemove == null || onRemove.ReturnType != typeof(void))
            throw new MissingMethodException(confuseType.FullName, "OnRemove");

        Type effectManager = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        Type networkManager = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        effectManagerSingleton = RequireField(effectManager, "mSingelton");
        networkManagerSingleton = RequireField(networkManager, "sSingelton");
        originalCache = cacheField.GetValue(null);
        originalActiveCache = activeCacheField.GetValue(null);
        originalEffectManager = effectManagerSingleton.GetValue(null);
        originalNetworkManager = networkManagerSingleton.GetValue(null);

        effectManagerSingleton.SetValue(
            null,
            NewUninitialized(effectManager));
        networkManagerSingleton.SetValue(
            null,
            NewUninitialized(networkManager));
        harmony = HarmonyInstance.Create(HarmonyOwner);
        InstallDependencyStubs(effectManager);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(HarmonyOwner);
        cacheField.SetValue(null, originalCache);
        activeCacheField.SetValue(null, originalActiveCache);
        effectManagerSingleton.SetValue(null, originalEffectManager);
        networkManagerSingleton.SetValue(null, originalNetworkManager);
    }

    internal ScenarioResult DetachedTarget()
    {
        object currentFaction = Enum.Parse(factionsType, "EVIL");
        Fixture fixture = CreateFixture(currentFaction, null);
        Exception failure = Invoke(fixture.Confuse);
        bool currentUsed = Object.Equals(
            ConfuseFactionProbe.ObservedFaction,
            currentFaction);
        bool released = targetField.GetValue(fixture.Confuse) == null;
        bool cached = fixture.Cache.Contains(fixture.Confuse) &&
            !fixture.Active.Contains(fixture.Confuse);
        string actual = "exception:" +
            (failure == null ? "none" : failure.GetType().Name) +
            ",faction:" + (currentUsed ? "current" : "missing") +
            ",released:" + released +
            ",cached:" + cached;
        const string expected =
            "exception:none,faction:current,released:True,cached:True";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult AttachedTarget()
    {
        object currentFaction = Enum.Parse(factionsType, "NONE");
        object templateFaction = Enum.Parse(factionsType, "EVIL");
        object template = NewUninitialized(templateType);
        templateFactionField.SetValue(template, templateFaction);
        Fixture fixture = CreateFixture(currentFaction, template);
        Exception failure = Invoke(fixture.Confuse);
        bool templateUsed = Object.Equals(
            ConfuseFactionProbe.ObservedFaction,
            templateFaction);
        bool released = targetField.GetValue(fixture.Confuse) == null;
        bool cached = fixture.Cache.Contains(fixture.Confuse) &&
            !fixture.Active.Contains(fixture.Confuse);
        string actual = "exception:" +
            (failure == null ? "none" : failure.GetType().Name) +
            ",faction:" + (templateUsed ? "template" : "current") +
            ",released:" + released +
            ",cached:" + cached;
        const string expected =
            "exception:none,faction:template,released:True,cached:True";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private Fixture CreateFixture(object faction, object template)
    {
        ConfuseFactionProbe.Reset();
        object target = NewUninitialized(nonPlayerCharacterType);
        characterFactionField.SetValue(target, faction);
        characterTemplateField.SetValue(target, template);
        object confuse = NewUninitialized(confuseType);
        targetField.SetValue(confuse, target);
        controllerField.SetValue(confuse, null);
        effectField.SetValue(
            confuse,
            Activator.CreateInstance(effectField.FieldType));
        IList cache = NewList();
        IList active = NewList();
        active.Add(confuse);
        cacheField.SetValue(null, cache);
        activeCacheField.SetValue(null, active);
        return new Fixture(confuse, cache, active);
    }

    private IList NewList()
    {
        return (IList)Activator.CreateInstance(
            typeof(System.Collections.Generic.List<>).MakeGenericType(
                confuseType));
    }

    private Exception Invoke(object confuse)
    {
        try
        {
            onRemove.Invoke(confuse, null);
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException ?? exception;
        }
    }

    private void InstallDependencyStubs(Type effectManager)
    {
        MethodInfo stop = FindByRefMethod(
            effectManager,
            "Stop",
            effectField.FieldType);
        MethodInfo confuse = nonPlayerCharacterType.GetMethod(
            "Confuse",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new Type[] { factionsType },
            null);
        if (confuse == null || confuse.ReturnType != typeof(void))
            throw new MissingMethodException(
                nonPlayerCharacterType.FullName,
                "Confuse");
        harmony.Patch(
            stop,
            new HarmonyMethod(
                typeof(ConfuseFactionProbe).GetMethod("StopPrefix")),
            null,
            null);
        harmony.Patch(
            confuse,
            new HarmonyMethod(
                typeof(ConfuseFactionProbe).GetMethod("ConfusePrefix")
                    .MakeGenericMethod(new Type[] { factionsType })),
            null,
            null);
    }

    private static MethodInfo FindByRefMethod(
        Type type,
        string name,
        Type elementType)
    {
        MethodInfo result = null;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name != name || parameters.Length != 1 ||
                !parameters[0].ParameterType.IsByRef ||
                parameters[0].ParameterType.GetElementType() != elementType)
                continue;
            if (result != null)
                throw new InvalidOperationException(
                    "Multiple " + type.FullName + "." + name +
                    " methods matched.");
            result = methods[index];
        }
        if (result == null)
            throw new MissingMethodException(type.FullName, name);
        return result;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
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

    private sealed class Fixture
    {
        internal object Confuse;
        internal IList Cache;
        internal IList Active;

        internal Fixture(object confuse, IList cache, IList active)
        {
            Confuse = confuse;
            Cache = cache;
            Active = active;
        }
    }
}

public static class ConfuseFactionProbe
{
    public static object ObservedFaction;

    public static void Reset()
    {
        ObservedFaction = null;
    }

    public static bool StopPrefix()
    {
        return false;
    }

    public static bool ConfusePrefix<TFaction>(TFaction iNewFaction)
    {
        ObservedFaction = iNewFaction;
        return false;
    }
}
