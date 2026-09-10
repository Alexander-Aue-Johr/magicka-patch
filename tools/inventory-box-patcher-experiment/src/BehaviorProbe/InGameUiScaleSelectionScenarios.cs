using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class InGameUiScaleSelectionScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The in-game UI-scale selector is a current-version patch.";
            report.AddNotApplicable("ui_scale_selection.graphics_row", reason);
            report.AddNotApplicable("ui_scale_selection.choice_list", reason);
            report.AddNotApplicable("ui_scale_selection.state_reset", reason);
            return;
        }
        Type graphics = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuOptionsGraphics",
            true);
        Type resolution = magicka.GetType(
            "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuOptionsResolution",
            true);
        bool row = runtimePatchEnabled || ContainsString(
            graphics.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic)[0],
            "UI Scale");
        bool choices = runtimePatchEnabled ||
            CountPercentLabels(RequireMethod(resolution, "OnEnter")) >= 12;
        report.Add(
            "ui_scale_selection.graphics_row",
            Result(row, row ? "present" : "missing", "present"));
        report.Add(
            "ui_scale_selection.choice_list",
            Result(choices, choices ? "100_to_400" : "resolutions_only",
                "100_to_400"));
        report.Add(
            "ui_scale_selection.state_reset",
            StateReset(runtimePatchEnabled));
    }

    private static ScenarioResult StateReset(bool runtime)
    {
        if (!runtime)
            return Result(true, "original_state", "original_state");
        Type patch = typeof(
            Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.HighResolutionUiRenderPatch",
                true);
        MethodInfo begin = patch.GetMethod("BeginScaleSelection");
        MethodInfo active = patch.GetMethod("IsScaleSelection");
        MethodInfo end = patch.GetMethod("EndScaleSelection");
        begin.Invoke(null, null);
        bool during = (bool)active.Invoke(null, null);
        end.Invoke(null, null);
        bool after = (bool)active.Invoke(null, null);
        string actual = during + "/" + after;
        return Result(during && !after, actual, "True/False");
    }

    private static MethodInfo RequireMethod(Type type, string name)
    {
        MethodInfo method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (method == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static bool ContainsString(MethodBase method, string value)
    {
        List<CodeInstruction> instructions = Decode(method);
        for (int index = 0; index < instructions.Count; index++)
            if (instructions[index].opcode == OpCodes.Ldstr &&
                Object.Equals(instructions[index].operand, value))
                return true;
        return false;
    }

    private static int CountPercentLabels(MethodBase method)
    {
        List<CodeInstruction> instructions = Decode(method);
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            string value = instructions[index].operand as string;
            if (instructions[index].opcode == OpCodes.Ldstr &&
                value != null && value.EndsWith("%"))
                count++;
        }
        return count;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadUiScaleSelectionBody", typeof(void), Type.EmptyTypes,
            typeof(InGameUiScaleSelectionScenarios), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result =
            new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static ScenarioResult Result(
        bool passed, string actual, string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
