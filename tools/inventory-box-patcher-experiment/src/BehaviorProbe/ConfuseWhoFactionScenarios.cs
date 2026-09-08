using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class ConfuseWhoFactionScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ConfuseWhoFactionHarness harness =
            new ConfuseWhoFactionHarness(magicka);
        try
        {
            report.Add(
                "confuse_who.detached_victim",
                harness.DetachedVictim());
            report.Add(
                "confuse_who.empty",
                harness.Empty());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class ConfuseWhoFactionHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-confuse-who";

    private readonly Type dataChannelType;
    private readonly Type factionsType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type confuseWhoType;
    private readonly Type victimInfoType;
    private readonly FieldInfo factionField;
    private readonly FieldInfo templateField;
    private readonly FieldInfo victimsField;
    private readonly FieldInfo victimEffectField;
    private readonly FieldInfo victimCharacterField;
    private readonly FieldInfo victimTtlField;
    private readonly MethodInfo update;
    private readonly HarmonyInstance harmony;

    internal ConfuseWhoFactionHarness(Assembly magicka)
    {
        factionsType = magicka.GetType("Magicka.Factions", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        confuseWhoType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ConfuseWho",
            true);
        victimInfoType = confuseWhoType.GetNestedType(
            "VictimInfo",
            BindingFlags.NonPublic);
        if (victimInfoType == null)
            throw new TypeLoadException(
                confuseWhoType.FullName + "+VictimInfo");

        victimsField = RuntimeReflection.RequireField(
            confuseWhoType,
            "sVictims");
        victimEffectField = RuntimeReflection.RequireField(
            victimInfoType,
            "Effect");
        victimCharacterField = RuntimeReflection.RequireField(
            victimInfoType,
            "Victim");
        victimTtlField = RuntimeReflection.RequireField(
            victimInfoType,
            "TTL");
        Type character = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        factionField = RuntimeReflection.RequireField(character, "mFaction");
        templateField = RuntimeReflection.RequireField(character, "mTemplate");
        update = FindUpdate();
        dataChannelType = update.GetParameters()[0].ParameterType;

        harmony = HarmonyInstance.Create(HarmonyOwner);
        InstallDependencyStubs(magicka);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult DetachedVictim()
    {
        object expectedFaction = Enum.Parse(factionsType, "EVIL");
        object character = NewUninitialized(nonPlayerCharacterType);
        factionField.SetValue(character, expectedFaction);
        templateField.SetValue(character, null);

        object victim = Activator.CreateInstance(victimInfoType);
        victimTtlField.SetValue(victim, 0f);
        victimCharacterField.SetValue(victim, character);
        victimEffectField.SetValue(
            victim,
            Activator.CreateInstance(victimEffectField.FieldType));
        IList victims = NewVictimList();
        victims.Add(victim);
        victimsField.SetValue(null, victims);

        ConfuseWhoProbe.Reset();
        Exception failure = InvokeUpdate();
        bool factionRestored = Object.Equals(
            ConfuseWhoProbe.ObservedFaction,
            expectedFaction);
        bool removed = victims.Count == 0;
        bool passed = failure == null && factionRestored && removed &&
            ConfuseWhoProbe.StopCalls == 1 &&
            ConfuseWhoProbe.ConfuseCalls == 1;
        string actual = "exception:" +
            (failure == null ? "none" : failure.GetType().FullName) +
            ",faction:" + (factionRestored ? "current" : "missing") +
            ",removed:" + removed +
            ",stop:" + ConfuseWhoProbe.StopCalls +
            ",confuse:" + ConfuseWhoProbe.ConfuseCalls;
        return new ScenarioResult(
            passed,
            actual,
            "exception:none,faction:current,removed:True,stop:1,confuse:1");
    }

    internal ScenarioResult Empty()
    {
        IList victims = NewVictimList();
        victimsField.SetValue(null, victims);
        ConfuseWhoProbe.Reset();
        Exception failure = InvokeUpdate();
        bool passed = failure == null && victims.Count == 0 &&
            ConfuseWhoProbe.StopCalls == 0 &&
            ConfuseWhoProbe.ConfuseCalls == 0;
        return new ScenarioResult(
            passed,
            "exception:" +
                (failure == null ? "none" : failure.GetType().FullName) +
                ",count:" + victims.Count +
                ",stop:" + ConfuseWhoProbe.StopCalls +
                ",confuse:" + ConfuseWhoProbe.ConfuseCalls,
            "exception:none,count:0,stop:0,confuse:0");
    }

    private IList NewVictimList()
    {
        return (IList)Activator.CreateInstance(
            typeof(System.Collections.Generic.List<>).MakeGenericType(
                victimInfoType));
    }

    private Exception InvokeUpdate()
    {
        try
        {
            update.Invoke(
                NewUninitialized(confuseWhoType),
                new object[] { Enum.ToObject(dataChannelType, 0), 0f });
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException ?? exception;
        }
    }

    private MethodInfo FindUpdate()
    {
        MethodInfo result = null;
        MethodInfo[] methods = confuseWhoType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "Update" ||
                methods[index].GetParameters().Length != 2 ||
                methods[index].ReturnType != typeof(void))
                continue;
            if (result != null)
                throw new InvalidOperationException(
                    "Multiple ConfuseWho.Update methods matched.");
            result = methods[index];
        }
        if (result == null)
            throw new MissingMethodException(confuseWhoType.FullName, "Update");
        return result;
    }

    private void InstallDependencyStubs(Assembly magicka)
    {
        Type effectManager = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        RuntimeReflection.RequireField(effectManager, "mSingelton").SetValue(
            null,
            NewUninitialized(effectManager));
        MethodInfo stop = FindByRefMethod(
            effectManager,
            "Stop",
            victimEffectField.FieldType);
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
                typeof(ConfuseWhoProbe).GetMethod("StopPrefix")),
            null,
            null);
        harmony.Patch(
            confuse,
            new HarmonyMethod(
                typeof(ConfuseWhoProbe).GetMethod("ConfusePrefix")
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
}

public static class ConfuseWhoProbe
{
    public static int ConfuseCalls;
    public static int StopCalls;
    public static object ObservedFaction;

    public static void Reset()
    {
        ConfuseCalls = 0;
        StopCalls = 0;
        ObservedFaction = null;
    }

    public static bool StopPrefix()
    {
        StopCalls++;
        return false;
    }

    public static bool ConfusePrefix<TFaction>(TFaction iNewFaction)
    {
        ConfuseCalls++;
        ObservedFaction = iNewFaction;
        return false;
    }
}
