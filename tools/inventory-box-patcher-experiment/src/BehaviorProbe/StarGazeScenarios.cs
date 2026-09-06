using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class StarGazeScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        StarGazeHarness harness = new StarGazeHarness(magicka);
        try
        {
            report.Add("star_gaze.detached_victim", harness.DetachedVictim());
            report.Add("star_gaze.empty", harness.Empty());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class StarGazeHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-star-gaze";

    private readonly Type dataChannelType;
    private readonly Type factionsType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type starGazeType;
    private readonly Type victimInfoType;
    private readonly FieldInfo factionField;
    private readonly FieldInfo templateField;
    private readonly FieldInfo victimsField;
    private readonly FieldInfo victimEffectField;
    private readonly FieldInfo victimCharacterField;
    private readonly FieldInfo victimTtlField;
    private readonly MethodInfo update;
    private readonly HarmonyInstance harmony;

    internal StarGazeHarness(Assembly magicka)
    {
        factionsType = magicka.GetType("Magicka.Factions", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        starGazeType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.StarGaze",
            true);
        victimInfoType = starGazeType.GetNestedType(
            "VictimInfo",
            BindingFlags.NonPublic);
        if (victimInfoType == null)
            throw new TypeLoadException(starGazeType.FullName + "+VictimInfo");

        victimsField = RuntimeReflection.RequireField(starGazeType, "sVictims");
        victimEffectField = RuntimeReflection.RequireField(victimInfoType, "Effect");
        victimCharacterField = RuntimeReflection.RequireField(victimInfoType, "Victim");
        victimTtlField = RuntimeReflection.RequireField(victimInfoType, "TTL");
        factionField = RuntimeReflection.RequireField(
            magicka.GetType("Magicka.GameLogic.Entities.Character", true),
            "mFaction");
        templateField = RuntimeReflection.RequireField(
            magicka.GetType("Magicka.GameLogic.Entities.Character", true),
            "mTemplate");
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

        StarGazeProbe.Reset();
        Exception failure = InvokeUpdate();
        bool factionRestored = Object.Equals(
            StarGazeProbe.ObservedFaction,
            expectedFaction);
        bool removed = victims.Count == 0;
        bool passed = failure == null && factionRestored && removed &&
            StarGazeProbe.StopCalls == 1 &&
            StarGazeProbe.ConfuseCalls == 1;
        string actual = "exception:" +
            (failure == null ? "none" : failure.GetType().FullName) +
            ",faction:" + (factionRestored ? "current" : "missing") +
            ",removed:" + removed +
            ",stop:" + StarGazeProbe.StopCalls +
            ",confuse:" + StarGazeProbe.ConfuseCalls;
        return new ScenarioResult(
            passed,
            actual,
            "exception:none,faction:current,removed:True,stop:1,confuse:1");
    }

    internal ScenarioResult Empty()
    {
        IList victims = NewVictimList();
        victimsField.SetValue(null, victims);
        StarGazeProbe.Reset();
        Exception failure = InvokeUpdate();
        bool passed = failure == null && victims.Count == 0 &&
            StarGazeProbe.StopCalls == 0 &&
            StarGazeProbe.ConfuseCalls == 0;
        return new ScenarioResult(
            passed,
            "exception:" + (failure == null ? "none" : failure.GetType().FullName) +
                ",count:" + victims.Count +
                ",stop:" + StarGazeProbe.StopCalls +
                ",confuse:" + StarGazeProbe.ConfuseCalls,
            "exception:none,count:0,stop:0,confuse:0");
    }

    private IList NewVictimList()
    {
        return (IList)Activator.CreateInstance(
            typeof(System.Collections.Generic.List<>).MakeGenericType(victimInfoType));
    }

    private Exception InvokeUpdate()
    {
        try
        {
            update.Invoke(
                FormatterServices.GetUninitializedObject(starGazeType),
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
        MethodInfo[] methods = starGazeType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "Update" ||
                methods[index].GetParameters().Length != 2 ||
                methods[index].ReturnType != typeof(void))
                continue;
            if (result != null)
                throw new InvalidOperationException("Multiple StarGaze.Update methods matched.");
            result = methods[index];
        }
        if (result == null)
            throw new MissingMethodException(starGazeType.FullName, "Update");
        return result;
    }

    private void InstallDependencyStubs(Assembly magicka)
    {
        Type effectManagerType = magicka.GetType("Magicka.Graphics.EffectManager", true);
        RuntimeReflection.RequireField(effectManagerType, "mSingelton").SetValue(
            null,
            NewUninitialized(effectManagerType));
        MethodInfo stop = FindMethod(effectManagerType, "Stop", victimEffectField.FieldType);
        MethodInfo confuse = nonPlayerCharacterType.GetMethod(
            "Confuse",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new Type[] { factionsType },
            null);
        if (confuse == null || confuse.ReturnType != typeof(void))
            throw new MissingMethodException(nonPlayerCharacterType.FullName, "Confuse");

        harmony.Patch(
            stop,
            new HarmonyMethod(typeof(StarGazeProbe).GetMethod("StopPrefix")),
            null,
            null);
        harmony.Patch(
            confuse,
            new HarmonyMethod(
                typeof(StarGazeProbe).GetMethod("ConfusePrefix")
                    .MakeGenericMethod(new Type[] { factionsType })),
            null,
            null);
    }

    private static MethodInfo FindMethod(Type type, string name, Type byRefElementType)
    {
        MethodInfo result = null;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name != name || parameters.Length != 1 ||
                !parameters[0].ParameterType.IsByRef ||
                parameters[0].ParameterType.GetElementType() != byRefElementType)
                continue;
            if (result != null)
                throw new InvalidOperationException(
                    "Multiple " + type.FullName + "." + name + " methods matched.");
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

public static class StarGazeProbe
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
