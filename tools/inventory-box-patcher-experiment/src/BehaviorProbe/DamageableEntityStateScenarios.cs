using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class DamageableEntityStateScenarios
{
    private static readonly string[] ScenarioNames = new string[]
    {
        "damageable_deinitialize.gib_release",
        "damageable_deinitialize.resistance_release",
        "damageable_deinitialize.cache_order"
    };

    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        DamageableEntityStateHarness harness;
        try
        {
            harness = new DamageableEntityStateHarness(
                magicka,
                runtimePatchEnabled);
        }
        catch (MissingMemberException)
        {
            for (int index = 0; index < ScenarioNames.Length; index++)
            {
                report.AddNotApplicable(
                    ScenarioNames[index],
                    "This Magicka version has no DamageablePhysicsEntity reuse pool.");
            }
            return;
        }
        report.Add(
            ScenarioNames[0],
            harness.GibRelease());
        report.Add(
            ScenarioNames[1],
            harness.ResistanceRelease());
        report.Add(
            ScenarioNames[2],
            harness.CacheOrder());
    }
}

internal sealed class DamageableEntityStateHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo gibsField;
    private readonly FieldInfo resistancesField;
    private readonly MethodInfo clearGibs;
    private readonly MethodInfo returnToCache;
    private readonly MethodInfo deinitialize;

    internal DamageableEntityStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type damageable = magicka.GetType(
            "Magicka.GameLogic.Entities.DamageablePhysicsEntity",
            true);
        gibsField = RuntimeReflection.RequireField(damageable, "mGibs");
        resistancesField = RuntimeReflection.RequireField(
            damageable,
            "mResistances");
        clearGibs = gibsField.FieldType.GetMethod(
            "Clear",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        returnToCache = damageable.GetMethod(
            "ReturnToCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { damageable },
            null);
        deinitialize = damageable.GetMethod(
            "Deinitialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (clearGibs == null || returnToCache == null ||
            deinitialize == null || deinitialize.ReturnType != typeof(void))
            throw new MissingMemberException(
                "DamageablePhysicsEntity deinitialization contract is incomplete.");
    }

    internal ScenarioResult GibRelease()
    {
        List<CodeInstruction> instructions = TransformedBody();
        int clearCalls = CountCall(instructions, clearGibs);
        return CountResult("clear_calls", clearCalls, 1);
    }

    internal ScenarioResult ResistanceRelease()
    {
        List<CodeInstruction> instructions = TransformedBody();
        int nullStores = 0;
        for (int index = 1; index < instructions.Count; index++)
        {
            if (instructions[index].opcode == OpCodes.Stfld &&
                Object.Equals(
                    instructions[index].operand,
                    resistancesField) &&
                instructions[index - 1].opcode == OpCodes.Ldnull)
                nullStores++;
        }
        return CountResult("null_stores", nullStores, 1);
    }

    internal ScenarioResult CacheOrder()
    {
        List<CodeInstruction> instructions = TransformedBody();
        int clearIndex = FindCall(instructions, clearGibs);
        int resistanceIndex = Find(instructions, OpCodes.Stfld, resistancesField);
        int cacheIndex = FindCall(instructions, returnToCache);
        int cacheCalls = CountCall(instructions, returnToCache);
        string actual = "releases_before_cache:" +
            (clearIndex >= 0 && resistanceIndex >= 0 &&
                clearIndex < cacheIndex && resistanceIndex < cacheIndex) +
            ",cache_calls:" + cacheCalls;
        const string expected =
            "releases_before_cache:True,cache_calls:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private List<CodeInstruction> TransformedBody()
    {
        List<CodeInstruction> instructions = Decode(deinitialize);
        if (!runtimePatchEnabled)
            return instructions;
        Type patch = Type.GetType(
            "Magicka.CommunityPatch.Runtime." +
            "DamageableEntityStatePatch, Magicka.CommunityPatch.Runtime",
            false);
        if (patch == null)
            return instructions;
        MethodInfo transpiler = patch.GetMethod(
            "Transpiler",
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patch.FullName, "Transpiler");
        object transformed = transpiler.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange((IEnumerable<CodeInstruction>)transformed);
        return instructions;
    }

    private static int CountCall(
        List<CodeInstruction> instructions,
        MethodInfo method)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (IsCall(instructions[index], method))
                count++;
        }
        return count;
    }

    private static int FindCall(
        List<CodeInstruction> instructions,
        MethodInfo method)
    {
        for (int index = 0; index < instructions.Count; index++)
        {
            if (IsCall(instructions[index], method))
                return index;
        }
        return -1;
    }

    private static bool IsCall(CodeInstruction instruction, MethodInfo method)
    {
        return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
            Object.Equals(instruction.operand, method);
    }

    private static int Find(
        List<CodeInstruction> instructions,
        OpCode opcode,
        object operand)
    {
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode == opcode &&
                Object.Equals(instructions[index].operand, operand))
                return index;
        }
        return -1;
    }

    private static ScenarioResult CountResult(
        string name,
        int actualValue,
        int expectedValue)
    {
        string actual = name + ":" + actualValue;
        string expected = name + ":" + expectedValue;
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static List<CodeInstruction> Decode(MethodInfo method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadDamageableDeinitializeBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(DamageableEntityStateHarness),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result =
            new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }
}
