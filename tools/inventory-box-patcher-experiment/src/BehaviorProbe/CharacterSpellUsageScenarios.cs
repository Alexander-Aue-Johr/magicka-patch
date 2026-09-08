using System;
using System.Reflection;

internal static class CharacterSpellUsageScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        CharacterSpellUsageHarness harness =
            new CharacterSpellUsageHarness(magicka, runtimePatchEnabled);
        report.Add("character_spell_usage.detached_gamer", harness.DetachedGamer());
        report.Add("character_spell_usage.local_gamer", harness.LocalGamer());
        report.Add("character_spell_usage.network_gamer", harness.NetworkGamer());
    }
}

internal sealed class CharacterSpellUsageHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly Type gamerType;
    private readonly Type networkGamerType;
    private readonly MethodInfo manualGuard;

    internal CharacterSpellUsageHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        gamerType = magicka.GetType("Magicka.Gamers.Gamer", true);
        networkGamerType = magicka.GetType("Magicka.Gamers.NetworkGamer", true);
        Type guardType = magicka.GetType(
            "Magicka.CommunityPatch.RuntimeCompatibilityGuards",
            false);
        if (guardType != null)
        {
            manualGuard = guardType.GetMethod(
                "CanRecordLocalSpellUsage",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
        }
    }

    internal ScenarioResult DetachedGamer()
    {
        return Evaluate(null, "skip");
    }

    internal ScenarioResult LocalGamer()
    {
        return EvaluateType(gamerType, "record");
    }

    internal ScenarioResult NetworkGamer()
    {
        return EvaluateType(networkGamerType, "skip");
    }

    private ScenarioResult Evaluate(object gamer, string expected)
    {
        bool record;
        if (runtimePatchEnabled)
        {
            Type patchType = FindLoadedType(
                "Magicka.CommunityPatch.Runtime.CharacterSpellUsagePatch");
            MethodInfo guard = patchType == null
                ? null
                : patchType.GetMethod(
                    "CanRecordLocalSpellUsage",
                    BindingFlags.Static | BindingFlags.Public);
            record = guard == null
                ? !networkGamerType.IsInstanceOfType(gamer)
                : (bool)guard.Invoke(null, new object[] { gamer });
        }
        else if (manualGuard != null)
        {
            record = (bool)manualGuard.Invoke(null, new object[] { gamer });
        }
        else
        {
            record = !networkGamerType.IsInstanceOfType(gamer);
        }

        string actual = record ? "record" : "skip";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private ScenarioResult EvaluateType(Type runtimeType, string expected)
    {
        bool record;
        Type patchType = runtimePatchEnabled
            ? FindLoadedType(
                "Magicka.CommunityPatch.Runtime.CharacterSpellUsagePatch")
            : null;
        MethodInfo guard = patchType == null
            ? null
            : patchType.GetMethod(
                "CanRecordLocalSpellUsageType",
                BindingFlags.Static | BindingFlags.Public);
        record = guard == null
            ? !networkGamerType.IsAssignableFrom(runtimeType)
            : (bool)guard.Invoke(null, new object[] { runtimeType });
        string actual = record ? "record" : "skip";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static Type FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < assemblies.Length; index++)
        {
            Type type = assemblies[index].GetType(fullName, false);
            if (type != null)
                return type;
        }
        return null;
    }
}
