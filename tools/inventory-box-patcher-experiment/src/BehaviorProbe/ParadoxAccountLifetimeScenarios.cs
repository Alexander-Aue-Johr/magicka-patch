using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ParadoxAccountLifetimeScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type accountSaveData = magicka.GetType(
            "Magicka.Storage.ParadoxAccountSaveData",
            false);
        Type scopedSingleton = magicka.GetType(
            "Magicka.Misc.ScopedSingleton`1",
            false);
        if (accountSaveData == null || scopedSingleton == null)
        {
            report.AddNotApplicable(
                "paradox_account_lifetime.cleanup_location",
                "The scoped ParadoxAccountSaveData lifetime is unavailable.");
            return;
        }

        ParadoxAccountLifetimeHarness harness =
            new ParadoxAccountLifetimeHarness(
                magicka,
                accountSaveData,
                runtimePatchEnabled);
        report.Add(
            "paradox_account_lifetime.cleanup_location",
            harness.CleanupLocation());
    }
}

internal sealed class ParadoxAccountLifetimeHarness
{
    private readonly Type accountSaveData;
    private readonly List<CodeInstruction> menuExit;
    private readonly List<CodeInstruction> endRun;

    internal ParadoxAccountLifetimeHarness(
        Assembly magicka,
        Type accountSaveDataType,
        bool runtimePatchEnabled)
    {
        accountSaveData = accountSaveDataType;
        MethodInfo menuExitMethod = magicka.GetType(
                "Magicka.GameLogic.GameStates.MenuState",
                true)
            .GetMethod(
                "OnExit",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
        MethodInfo endRunMethod = magicka.GetType("Magicka.Game", true)
            .GetMethod(
                "EndRun",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
        if (menuExitMethod == null || endRunMethod == null)
            throw new MissingMethodException(
                "Paradox account lifetime targets are incomplete.");

        menuExit = Decode(menuExitMethod, "ParadoxAccountMenuExitDecode");
        endRun = Decode(endRunMethod, "ParadoxAccountEndRunDecode");
        if (runtimePatchEnabled)
            ApplyRuntimeTranspilers();
    }

    internal ScenarioResult CleanupLocation()
    {
        int menuCount = CountDestroyCalls(menuExit);
        int endCount = CountDestroyCalls(endRun);
        string actual = "menu:" + menuCount + ",shutdown:" + endCount;
        const string expected = "menu:0,shutdown:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private void ApplyRuntimeTranspilers()
    {
        Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
            .GetType(
                "Magicka.CommunityPatch.Runtime.ParadoxAccountLifetimePatch",
                false);
        if (patch == null)
            return;
        ApplyTranspiler(patch, "MenuExitTranspiler", menuExit);
        ApplyTranspiler(patch, "EndRunTranspiler", endRun);
    }

    private static void ApplyTranspiler(
        Type patch,
        string name,
        List<CodeInstruction> instructions)
    {
        MethodInfo transpiler = patch.GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patch.FullName, name);
        object transformed = transpiler.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange((IEnumerable<CodeInstruction>)transformed);
    }

    private int CountDestroyCalls(List<CodeInstruction> instructions)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode != OpCodes.Call)
                continue;
            MethodInfo method = instructions[index].operand as MethodInfo;
            if (method == null || method.Name != "Destroy")
                continue;
            Type declaring = method.DeclaringType;
            if (declaring == null || !declaring.IsGenericType)
                continue;
            Type[] arguments = declaring.GetGenericArguments();
            if (arguments.Length == 1 && arguments[0] == accountSaveData)
                count++;
        }
        return count;
    }

    private static List<CodeInstruction> Decode(
        MethodInfo method,
        string name)
    {
        DynamicMethod target = new DynamicMethod(
            name,
            typeof(void),
            Type.EmptyTypes,
            typeof(ParadoxAccountLifetimeHarness),
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
