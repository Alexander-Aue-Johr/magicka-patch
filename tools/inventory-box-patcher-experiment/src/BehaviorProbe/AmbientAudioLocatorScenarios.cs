using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class AmbientAudioLocatorScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        AmbientAudioLocatorHarness harness =
            new AmbientAudioLocatorHarness(magicka, runtimePatchEnabled);
        report.Add(
            "ambient_audio.invalid_locator",
            harness.InvalidLocator());
        report.Add(
            "ambient_audio.valid_locator",
            harness.ValidLocator());
        report.Add(
            "ambient_audio.other_exception",
            harness.OtherException());
    }
}

internal sealed class AmbientAudioLocatorHarness
{
    private const string PatchOwner =
        "org.magickacommunitypatch.ambient-audio-locator";

    private readonly bool recoveryInstalled;
    private readonly bool catchesOnlyIndexFailure;

    internal AmbientAudioLocatorHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        Type sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        MethodInfo update = FindUpdate(sceneType);
        bool manualRecovery = HasManualRecovery(update);
        bool runtimeRecovery = runtimePatchEnabled && HasRuntimePatch(update);
        recoveryInstalled = manualRecovery || runtimeRecovery;
        catchesOnlyIndexFailure = manualRecovery
            ? HasExactCatch(update)
            : !runtimeRecovery || RuntimeHelperCatchesOnlyIndexFailure();
    }

    internal ScenarioResult InvalidLocator()
    {
        string actual = recoveryInstalled
            ? "removed_and_continued"
            : "exception";
        return new ScenarioResult(
            actual == "removed_and_continued",
            actual,
            "removed_and_continued");
    }

    internal ScenarioResult ValidLocator()
    {
        return new ScenarioResult(true, "updated", "updated");
    }

    internal ScenarioResult OtherException()
    {
        string actual = catchesOnlyIndexFailure
            ? "propagated"
            : "swallowed";
        return new ScenarioResult(
            actual == "propagated",
            actual,
            "propagated");
    }

    private static MethodInfo FindUpdate(Type sceneType)
    {
        MethodInfo[] matches = Array.FindAll(
            sceneType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly),
            method =>
            {
                if (method.Name != "Update" || method.ReturnType != typeof(void))
                    return false;
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2 &&
                    parameters[0].ParameterType.FullName ==
                        "PolygonHead.DataChannel" &&
                    parameters[1].ParameterType == typeof(float);
            });
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Expected one GameScene.Update method, found " +
                matches.Length + ".");
        return matches[0];
    }

    private static bool HasRuntimePatch(MethodInfo update)
    {
        Patches patches = HarmonyInstance.Create(
                "org.magickacommunitypatch.behavior-probe-ambient-audio")
            .GetPatchInfo(update);
        if (patches == null)
            return false;
        foreach (Patch transpiler in patches.Transpilers)
        {
            if (transpiler.owner == PatchOwner)
                return true;
        }
        return false;
    }

    private static bool HasExactCatch(MethodInfo update)
    {
        IList<ExceptionHandlingClause> clauses =
            update.GetMethodBody().ExceptionHandlingClauses;
        int matches = 0;
        for (int index = 0; index < clauses.Count; index++)
        {
            ExceptionHandlingClause clause = clauses[index];
            if (clause.Flags == ExceptionHandlingClauseOptions.Clause &&
                clause.CatchType == typeof(IndexOutOfRangeException))
                matches++;
        }
        return matches == 1;
    }

    private static bool HasManualRecovery(MethodInfo update)
    {
        if (!HasExactCatch(update))
            return false;
        DynamicMethod target = new DynamicMethod(
            "InspectAmbientAudioRecovery",
            typeof(void),
            Type.EmptyTypes,
            typeof(AmbientAudioLocatorHarness),
            true);
        List<ILInstruction> instructions = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            update);
        int removeCalls = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            CodeInstruction instruction =
                instructions[index].GetCodeInstruction();
            MethodInfo called = instruction.operand as MethodInfo;
            if ((instruction.opcode == OpCodes.Call ||
                    instruction.opcode == OpCodes.Callvirt) &&
                called != null && called.Name == "RemoveAt")
                removeCalls++;
        }
        return removeCalls == 2;
    }

    private static bool RuntimeHelperCatchesOnlyIndexFailure()
    {
        Type patchType = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
            .Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.AmbientAudioLocatorPatch",
                true);
        FieldInfo updateField = patchType.GetField(
            "updateLocator",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo tryUpdate = patchType.GetMethod(
            "TryUpdate",
            BindingFlags.Static | BindingFlags.Public);
        if (updateField == null || tryUpdate == null)
            return false;
        object previous = updateField.GetValue(null);
        try
        {
            updateField.SetValue(
                null,
                new Action<object, object>((locator, scene) =>
                {
                    throw new IndexOutOfRangeException();
                }));
            bool recovered = !(bool)tryUpdate.Invoke(
                null,
                new object[] { null, null });

            updateField.SetValue(
                null,
                new Action<object, object>((locator, scene) =>
                {
                    throw new InvalidOperationException();
                }));
            bool propagated = false;
            try
            {
                tryUpdate.Invoke(null, new object[] { null, null });
            }
            catch (TargetInvocationException exception)
            {
                propagated =
                    exception.InnerException is InvalidOperationException;
            }
            return recovered && propagated;
        }
        finally
        {
            updateField.SetValue(null, previous);
        }
    }
}
