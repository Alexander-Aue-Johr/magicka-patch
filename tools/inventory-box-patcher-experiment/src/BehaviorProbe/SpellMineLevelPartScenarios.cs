using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class SpellMineLevelPartScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type spellMine = magicka.GetType(
            "Magicka.GameLogic.Entities.SpellMine",
            true);
        if (spellMine.GetField(
            "mAnimatedLevelPart",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly) == null)
        {
            report.AddNotApplicable(
                "spell_mine.animated_part_release",
                "SpellMine has no animated-level-part field in this Magicka version");
            return;
        }
        SpellMineLevelPartHarness harness =
            new SpellMineLevelPartHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "spell_mine.animated_part_release",
            harness.Deinitialize());
    }
}

internal sealed class SpellMineLevelPartHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo animatedLevelPartField;
    private readonly MethodInfo deinitialize;

    internal SpellMineLevelPartHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type spellMine = magicka.GetType(
            "Magicka.GameLogic.Entities.SpellMine",
            true);
        Type animatedLevelPart = magicka.GetType(
            "Magicka.Levels.AnimatedLevelPart",
            true);
        animatedLevelPartField = spellMine.GetField(
            "mAnimatedLevelPart",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (animatedLevelPartField == null ||
            animatedLevelPartField.FieldType != animatedLevelPart)
            throw new MissingFieldException(
                spellMine.FullName,
                "mAnimatedLevelPart");
        deinitialize = spellMine.GetMethod(
            "Deinitialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (deinitialize == null || deinitialize.ReturnType != typeof(void))
            throw new MissingMethodException(
                spellMine.FullName,
                "Deinitialize");
    }

    internal ScenarioResult Deinitialize()
    {
        List<CodeInstruction> instructions = Decode(deinitialize);
        if (runtimePatchEnabled)
            instructions = Transform(instructions);
        int nullAssignments = 0;
        int finalAssignment = -1;
        int returnIndex = -1;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode == OpCodes.Ret)
                returnIndex = index;
            if (index < 2 ||
                instructions[index - 2].opcode != OpCodes.Ldarg_0 ||
                instructions[index - 1].opcode != OpCodes.Ldnull ||
                instructions[index].opcode != OpCodes.Stfld)
                continue;
            FieldInfo field = instructions[index].operand as FieldInfo;
            if (SameMember(field, animatedLevelPartField))
            {
                nullAssignments++;
                finalAssignment = index;
            }
        }
        bool final = nullAssignments == 1 &&
            finalAssignment >= 0 &&
            returnIndex > finalAssignment &&
            OnlyNopsBetween(instructions, finalAssignment + 1, returnIndex);
        string actual = "null_assignments:" + nullAssignments +
            ",final:" + final;
        return new ScenarioResult(
            actual == "null_assignments:1,final:True",
            actual,
            "null_assignments:1,final:True");
    }

    private static List<CodeInstruction> Transform(
        List<CodeInstruction> instructions)
    {
        Type patch = Type.GetType(
            "Magicka.CommunityPatch.Runtime.SpellMineLevelPartPatch, " +
                "Magicka.CommunityPatch.Runtime",
            true);
        MethodInfo transpiler = patch.GetMethod(
            "Transpiler",
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patch.FullName, "Transpiler");
        object transformed = transpiler.Invoke(
            null,
            new object[] { instructions });
        return new List<CodeInstruction>(
            (IEnumerable<CodeInstruction>)transformed);
    }

    private static bool OnlyNopsBetween(
        List<CodeInstruction> instructions,
        int start,
        int end)
    {
        for (int index = start; index < end; index++)
        {
            if (instructions[index].opcode != OpCodes.Nop)
                return false;
        }
        return true;
    }

    private static bool SameMember(MemberInfo left, MemberInfo right)
    {
        return left != null && right != null &&
            left.Module == right.Module &&
            left.MetadataToken == right.MetadataToken;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadSpellMineDeinitialize",
            typeof(void),
            Type.EmptyTypes,
            typeof(SpellMineLevelPartHarness),
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
