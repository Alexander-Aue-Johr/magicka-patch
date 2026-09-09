using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class DerivedSpellEffectPlayStateScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        DerivedSpellEffectPlayStateHarness harness =
            new DerivedSpellEffectPlayStateHarness(
                magicka,
                runtimePatchEnabled);
        harness.AddResults(report);
    }
}

internal sealed class DerivedSpellEffectPlayStateHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.DerivedSpellEffectPlayStatePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo legacyPlayState;
    private readonly MethodInfo recentPlayState;
    private readonly Type spellEffect;
    private readonly Type spell;
    private readonly Type caster;

    internal DerivedSpellEffectPlayStateHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        spellEffect = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellEffects.SpellEffect",
            true);
        spell = magicka.GetType(
            "Magicka.GameLogic.Spells.Spell",
            true);
        caster = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        legacyPlayState = spellEffect.GetField(
            "mPlayState",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayState = recent == null ? null : recent.GetGetMethod();
        if (recentPlayState == null)
            throw new MissingMemberException(
                "SpellEffect play-state contract is incomplete.");
    }

    internal void AddResults(BehaviorReport report)
    {
        AddStatic(report, "push_spell", "PushSpell", "GetFromCache", 1);
        AddReturn(report, "push_spell", "PushSpell", 1);
        AddStatic(report, "spray_spell", "SpraySpell", "GetFromCache", 1);
        AddReturn(report, "spray_spell", "SpraySpell", 1);
        AddCastUpdate(report, "spray_spell", "SpraySpell", 1);
        AddStatic(
            report,
            "projectile_spell",
            "ProjectileSpell",
            "GetFromCache",
            1);
        AddReturn(report, "projectile_spell", "ProjectileSpell", 1);
        AddStatic(report, "railgun_spell", "RailGunSpell", "GetFromCache", 1);
        AddReturn(report, "railgun_spell", "RailGunSpell", 1);
        AddCast(report, "railgun_spell", "RailGunSpell", "CastSelf", 2);
        AddCast(report, "railgun_spell", "RailGunSpell", "CastWeapon", 2);
        AddDeInitialize(report, "railgun_spell", "RailGunSpell", 1);
    }

    private void AddStatic(
        BehaviorReport report,
        string key,
        string typeName,
        string methodName,
        int expectedReads)
    {
        Type type = FindType(typeName);
        MethodInfo method = RequireMethod(
            type,
            methodName,
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            Type.EmptyTypes,
            spellEffect);
        Add(
            report,
            key,
            ToKey(methodName),
            typeName + methodName,
            method,
            expectedReads);
    }

    private void AddReturn(
        BehaviorReport report,
        string key,
        string typeName,
        int expectedReads)
    {
        Type type = FindType(typeName);
        MethodInfo method = RequireMethod(
            type,
            "ReturnToCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { type },
            typeof(void));
        Add(
            report,
            key,
            "return_to_cache",
            typeName + "ReturnToCache",
            method,
            expectedReads);
    }

    private void AddCast(
        BehaviorReport report,
        string key,
        string typeName,
        string methodName,
        int expectedReads)
    {
        Type type = FindType(typeName);
        MethodInfo method = RequireMethod(
            type,
            methodName,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { spell, caster, typeof(bool) },
            typeof(void));
        Add(report, key, ToKey(methodName), typeName + methodName, method,
            expectedReads);
    }

    private void AddCastUpdate(
        BehaviorReport report,
        string key,
        string typeName,
        int expectedReads)
    {
        Type type = FindType(typeName);
        MethodInfo method = RequireMethod(
            type,
            "CastUpdate",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[]
            {
                typeof(float),
                caster,
                typeof(float).MakeByRefType()
            },
            typeof(bool));
        Add(
            report,
            key,
            "cast_update",
            typeName + "CastUpdate",
            method,
            expectedReads);
    }

    private void AddDeInitialize(
        BehaviorReport report,
        string key,
        string typeName,
        int expectedReads)
    {
        Type type = FindType(typeName);
        MethodInfo method = RequireMethod(
            type,
            "DeInitialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            new Type[] { caster },
            typeof(void));
        Add(
            report,
            key,
            "deinitialize",
            typeName + "DeInitialize",
            method,
            expectedReads);
    }

    private void Add(
        BehaviorReport report,
        string key,
        string operation,
        string transpilerName,
        MethodInfo method,
        int expectedReads)
    {
        List<CodeInstruction> instructions = Body(method, transpilerName);
        int legacyReads = Count(
            instructions,
            OpCodes.Ldsfld,
            legacyPlayState);
        int currentReads = Count(
            instructions,
            OpCodes.Call,
            recentPlayState);
        string actual = "legacy_reads:" + legacyReads +
            ",current_reads:" + currentReads;
        string expected = "legacy_reads:0,current_reads:" + expectedReads;
        report.Add(
            key + "." + operation + "_current_play_state",
            new ScenarioResult(actual == expected, actual, expected));
    }

    private List<CodeInstruction> Body(
        MethodBase method,
        string transpilerName)
    {
        List<CodeInstruction> instructions = Decode(method);
        if (!runtimePatchEnabled)
            return instructions;
        Type patch = Type.GetType(PatchTypeName, false);
        if (patch == null)
            return instructions;
        MethodInfo transpiler = patch.GetMethod(
            transpilerName + "Transpiler",
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(
                patch.FullName,
                transpilerName + "Transpiler");
        object transformed = transpiler.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange((IEnumerable<CodeInstruction>)transformed);
        return instructions;
    }

    private Type FindType(string name)
    {
        return spellEffect.Assembly.GetType(
            "Magicka.GameLogic.Spells.SpellEffects." + name,
            true);
    }

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        BindingFlags flags,
        Type[] parameters,
        Type returnType)
    {
        MethodInfo method = type.GetMethod(
            name,
            flags,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != returnType)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static string ToKey(string methodName)
    {
        if (methodName == "CastSelf")
            return "cast_self";
        if (methodName == "CastWeapon")
            return "cast_weapon";
        if (methodName == "GetFromCache")
            return "get_from_cache";
        return methodName.ToLowerInvariant();
    }

    private static int Count(
        List<CodeInstruction> instructions,
        OpCode opcode,
        object operand)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode == opcode &&
                SameMember(instructions[index].operand, operand))
                count++;
        }
        return count;
    }

    private static bool SameMember(object left, object right)
    {
        MemberInfo leftMember = left as MemberInfo;
        MemberInfo rightMember = right as MemberInfo;
        if (leftMember == null || rightMember == null)
            return Object.Equals(left, right);
        return leftMember.Module == rightMember.Module &&
            leftMember.MetadataToken == rightMember.MetadataToken;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadDerivedSpellEffectBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(DerivedSpellEffectPlayStateHarness),
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
