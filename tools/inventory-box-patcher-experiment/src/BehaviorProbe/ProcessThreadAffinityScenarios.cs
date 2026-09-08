using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ProcessThreadAffinityScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ProcessThreadAffinityHarness harness =
            new ProcessThreadAffinityHarness(magicka, runtimePatchEnabled);
        report.Add("process_thread.null", harness.NullEntry());
        report.Add("process_thread.matching", harness.ValidEntry(7, 7));
        report.Add("process_thread.nonmatching", harness.ValidEntry(7, 8));
    }
}

internal sealed class ProcessThreadAffinityHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly bool manualNullGuard;

    internal ProcessThreadAffinityHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type gameType = magicka.GetType("Magicka.Game", true);
        ConstructorInfo constructor = gameType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        if (constructor == null)
            throw new MissingMethodException(gameType.FullName, ".ctor");
        manualNullGuard = HasNullGuard(constructor);
    }

    internal ScenarioResult NullEntry()
    {
        bool actual = runtimePatchEnabled
            ? HasRuntimePolicy() && !InvokePolicy(false, 0, 0)
            : manualNullGuard;
        return Result(actual, true);
    }

    internal ScenarioResult ValidEntry(int threadId, int currentThreadId)
    {
        bool actual = runtimePatchEnabled
            ? InvokePolicy(true, threadId, currentThreadId)
            : threadId == currentThreadId;
        return Result(actual, threadId == currentThreadId);
    }

    private static bool HasNullGuard(ConstructorInfo constructor)
    {
        List<CodeInstruction> instructions = ReadInstructions(constructor);
        for (int index = 2; index < instructions.Count; index++)
        {
            MethodInfo called = instructions[index].operand as MethodInfo;
            if (called == null || called.DeclaringType != typeof(ProcessThread) ||
                called.Name != "get_Id")
                continue;
            OpCode branch = instructions[index - 2].opcode;
            return branch == OpCodes.Brfalse ||
                branch == OpCodes.Brfalse_S;
        }
        throw new MissingMethodException("ProcessThread.get_Id");
    }

    private static bool InvokePolicy(
        bool available,
        int threadId,
        int currentThreadId)
    {
        Type type = FindLoadedType(
            "Magicka.CommunityPatch.Runtime.ProcessThreadAffinityPatch");
        MethodInfo method = type == null
            ? null
            : type.GetMethod(
                "ShouldConfigureThread",
                BindingFlags.Static | BindingFlags.Public);
        return method != null && (bool)method.Invoke(
            null,
            new object[] { available, threadId, currentThreadId });
    }

    private static bool HasRuntimePolicy()
    {
        Type type = FindLoadedType(
            "Magicka.CommunityPatch.Runtime.ProcessThreadAffinityPatch");
        return type != null && type.GetMethod(
            "ShouldConfigureThread",
            BindingFlags.Static | BindingFlags.Public) != null;
    }

    private static List<CodeInstruction> ReadInstructions(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "InspectProcessThreadAffinity",
            typeof(void),
            Type.EmptyTypes,
            typeof(ProcessThreadAffinityHarness),
            true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static ScenarioResult Result(bool actual, bool expected)
    {
        return new ScenarioResult(
            actual == expected,
            actual.ToString(),
            expected.ToString());
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
