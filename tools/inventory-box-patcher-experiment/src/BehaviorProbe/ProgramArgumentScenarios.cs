using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ProgramArgumentScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ProgramArgumentHarness harness =
            new ProgramArgumentHarness(magicka, runtimePatchEnabled);
        report.Add(
            "program_arguments.truncated_connect_lobby",
            harness.Truncated("+connect_lobby"));
        report.Add(
            "program_arguments.truncated_connect",
            harness.Truncated("+connect"));
        report.Add(
            "program_arguments.truncated_password",
            harness.Truncated("+password"));
        report.Add(
            "program_arguments.valid_values",
            harness.ValidValues());
        report.Add(
            "program_arguments.unrelated",
            harness.Unrelated());
    }
}

internal sealed class ProgramArgumentHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly MethodInfo main;

    internal ProgramArgumentHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type programType = magicka.GetType("Magicka.Program", true);
        main = programType.GetMethod(
            "Main",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new Type[] { typeof(string[]) },
            null);
        if (main == null)
            throw new MissingMethodException(programType.FullName, "Main");
    }

    internal ScenarioResult Truncated(string option)
    {
        bool actual;
        if (runtimePatchEnabled)
        {
            string[] arguments = new string[] { option };
            actual = Sanitize(arguments) && arguments[0] == string.Empty;
        }
        else
        {
            actual = HasFollowingValueBoundsCheck(option);
        }
        return Result(actual, true);
    }

    internal ScenarioResult ValidValues()
    {
        if (!runtimePatchEnabled)
            return Result(true, true);

        string[] arguments = new string[]
        {
            "+connect_lobby", "123", "+connect", "127.0.0.1",
            "+password", "secret"
        };
        string[] expected = (string[])arguments.Clone();
        bool actual = Sanitize(arguments) && ArrayEquals(arguments, expected);
        return Result(actual, true);
    }

    internal ScenarioResult Unrelated()
    {
        if (!runtimePatchEnabled)
            return Result(true, true);

        string[] arguments = new string[] { "+fullscreen", "custom" };
        string[] expected = (string[])arguments.Clone();
        bool actual = Sanitize(arguments) && ArrayEquals(arguments, expected);
        return Result(actual, true);
    }

    private bool HasFollowingValueBoundsCheck(string option)
    {
        List<CodeInstruction> instructions = ReadInstructions(main);
        for (int index = 0; index < instructions.Count; index++)
        {
            if (!Equals(instructions[index].operand, option))
                continue;

            int lengthIndex = FindOpCode(instructions, index, OpCodes.Ldlen, 20);
            if (lengthIndex < 0)
                return false;
            int branchIndex = FindConditionalBranch(
                instructions,
                lengthIndex + 1,
                12);
            if (branchIndex < 0)
                return false;
            for (int current = lengthIndex + 1; current < branchIndex; current++)
            {
                if (instructions[current].opcode == OpCodes.Ldc_I4_1 &&
                    current + 1 < branchIndex &&
                    instructions[current + 1].opcode == OpCodes.Add)
                {
                    return true;
                }
            }
            return false;
        }
        return false;
    }

    private static bool Sanitize(string[] arguments)
    {
        Type type = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
            .GetType(
                "Magicka.CommunityPatch.Runtime.ProgramArgumentSanitizer",
                false);
        MethodInfo method = type == null
            ? null
            : type.GetMethod(
                "Sanitize",
                BindingFlags.Static | BindingFlags.Public);
        if (method == null)
            return false;
        method.Invoke(null, new object[] { arguments });
        return true;
    }

    private static List<CodeInstruction> ReadInstructions(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "InspectProgramArguments",
            typeof(void),
            Type.EmptyTypes,
            typeof(ProgramArgumentHarness),
            true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static int FindOpCode(
        IList<CodeInstruction> instructions,
        int start,
        OpCode opcode,
        int limit)
    {
        int end = Math.Min(instructions.Count, start + limit);
        for (int index = start; index < end; index++)
        {
            if (instructions[index].opcode == opcode)
                return index;
        }
        return -1;
    }

    private static int FindConditionalBranch(
        IList<CodeInstruction> instructions,
        int start,
        int limit)
    {
        int end = Math.Min(instructions.Count, start + limit);
        for (int index = start; index < end; index++)
        {
            if (instructions[index].opcode.FlowControl ==
                FlowControl.Cond_Branch)
            {
                return index;
            }
        }
        return -1;
    }

    private static bool ArrayEquals(string[] left, string[] right)
    {
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
                return false;
        }
        return true;
    }

    private static ScenarioResult Result(bool actual, bool expected)
    {
        return new ScenarioResult(
            actual == expected,
            actual.ToString(),
            expected.ToString());
    }
}
