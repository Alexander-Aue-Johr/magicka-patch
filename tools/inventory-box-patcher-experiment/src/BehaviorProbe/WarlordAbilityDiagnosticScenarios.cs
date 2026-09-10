using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class WarlordAbilityDiagnosticScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        try
        {
            Type warlord = magicka.GetType(
                "Magicka.GameLogic.Entities.Bosses.WarlordCharacter",
                true);
            Type template = magicka.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            MethodInfo apply = warlord.GetMethod(
                "ApplyTemplate",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { template, typeof(int).MakeByRefType() },
                null);
            List<CodeInstruction> instructions = Decode(apply);
            if (runtimePatchEnabled)
            {
                object transformed = typeof(Magicka.CommunityPatch.Runtime
                    .WarlordAbilityDiagnosticPatch).GetMethod("Transpiler")
                    .Invoke(null, new object[] { instructions });
                instructions = new List<CodeInstruction>(
                    (IEnumerable<CodeInstruction>)transformed);
            }
            int diagnostic = -1;
            int meleeCast = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo call = instructions[index].operand as MethodInfo;
                if (call != null && call.Name == "Inspect" &&
                    call.DeclaringType != null &&
                    (call.DeclaringType.FullName ==
                        "Magicka.CommunityPatch.WarlordAbilityDiagnostic" ||
                     call.DeclaringType == typeof(Magicka.CommunityPatch.Runtime
                        .WarlordAbilityDiagnosticPatch)))
                    diagnostic = index;
                Type cast = instructions[index].operand as Type;
                if (instructions[index].opcode == OpCodes.Isinst &&
                    cast != null && cast.FullName ==
                        "Magicka.GameLogic.Entities.Abilities.Melee")
                    meleeCast = index;
            }
            bool passed = diagnostic >= 0 && meleeCast >= 0 &&
                diagnostic < meleeCast;
            report.Add(
                "warlord_ability.diagnostic_before_cast",
                new ScenarioResult(
                    passed,
                    "diagnostic:" + diagnostic + ",cast:" + meleeCast,
                    "diagnostic_before_cast:true"));
        }
        catch (Exception exception)
        {
            report.Add(
                "warlord_ability.diagnostic_before_cast",
                new ScenarioResult(
                    false,
                    "exception:" + exception.GetType().Name,
                    "diagnostic_before_cast:true"));
        }
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadWarlordApplyTemplate",
            typeof(void),
            Type.EmptyTypes,
            typeof(WarlordAbilityDiagnosticScenarios),
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
