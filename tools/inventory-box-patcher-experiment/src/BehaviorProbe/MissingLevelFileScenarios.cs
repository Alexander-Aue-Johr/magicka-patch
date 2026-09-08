using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class MissingLevelFileScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        MissingLevelFileHarness harness =
            new MissingLevelFileHarness(magicka, runtimePatchEnabled);
        report.Add("level_hash.missing_file", harness.MissingFile());
        report.Add("level_hash.other_io", harness.OtherIoFailure());
    }
}

internal sealed class MissingLevelFileHarness
{
    private const string MessageStart =
        "Magicka could not load this required level file:";
    private readonly bool runtimePatchEnabled;
    private readonly bool manualHandlerPresent;

    internal MissingLevelFileHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type managerType = magicka.GetType(
            "Magicka.Levels.Campaign.LevelManager",
            true);
        MethodInfo computeHashes = managerType.GetMethod(
            "ComputeHashes",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (computeHashes == null)
            throw new MissingMethodException(managerType.FullName, "ComputeHashes");
        manualHandlerPresent = ContainsString(computeHashes, MessageStart) &&
            Calls(computeHashes, "System.Environment", "Exit") &&
            !Calls(computeHashes, "Magicka.CommunityPatch.PatchTelemetry", null);
    }

    internal ScenarioResult MissingFile()
    {
        string actual = Classify(
            new FileNotFoundException("missing", "Tsar\\Missing.xnb"));
        const string expected = "handled:file=True,exit=1,telemetry=False";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult OtherIoFailure()
    {
        string actual = Classify(new IOException("unrelated"));
        const string expected = "propagate";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private string Classify(Exception exception)
    {
        Type patchType = runtimePatchEnabled
            ? FindLoadedType(
                "Magicka.CommunityPatch.Runtime.MissingLevelFilePatch")
            : null;
        MethodInfo classifier = patchType == null
            ? null
            : patchType.GetMethod(
                "ClassifyException",
                BindingFlags.Static | BindingFlags.Public);
        if (classifier != null)
        {
            string result = (string)classifier.Invoke(
                null,
                new object[] { exception });
            if (result != "missing_file")
                return "propagate";
            MethodInfo buildMessage = patchType.GetMethod(
                "BuildMessage",
                BindingFlags.Static | BindingFlags.Public);
            FieldInfo exitCode = patchType.GetField(
                "ExitCode",
                BindingFlags.Static | BindingFlags.Public);
            if (buildMessage == null || exitCode == null)
                return "invalid-runtime-contract";
            string message = (string)buildMessage.Invoke(
                null,
                new object[] { exception });
            return "handled:file=" + message.Contains("Tsar\\Missing.xnb") +
                ",exit=" + exitCode.GetValue(null) + ",telemetry=False";
        }
        if (manualHandlerPresent && exception is FileNotFoundException)
            return "handled:file=True,exit=1,telemetry=False";
        return "propagate";
    }

    private static bool ContainsString(MethodInfo method, string prefix)
    {
        List<CodeInstruction> instructions = ReadInstructions(method);
        for (int index = 0; index < instructions.Count; index++)
        {
            string value = instructions[index].operand as string;
            if (instructions[index].opcode == OpCodes.Ldstr && value != null &&
                value.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool Calls(
        MethodInfo method,
        string declaringType,
        string methodName)
    {
        List<CodeInstruction> instructions = ReadInstructions(method);
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodBase called = instructions[index].operand as MethodBase;
            if (called != null && called.DeclaringType != null &&
                called.DeclaringType.FullName == declaringType &&
                (methodName == null || called.Name == methodName))
                return true;
        }
        return false;
    }

    private static List<CodeInstruction> ReadInstructions(MethodInfo method)
    {
        DynamicMethod target = new DynamicMethod(
            "InspectMissingLevelHandler",
            typeof(void),
            Type.EmptyTypes,
            typeof(MissingLevelFileHarness),
            true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
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
