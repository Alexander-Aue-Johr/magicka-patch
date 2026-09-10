using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ElementSelectionTelemetryScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Check(magicka, runtimePatchEnabled, report,
            "KeyboardMouseController", "Update", "KeyboardTranspiler", 8,
            "input_telemetry.keyboard_hooks");
        Check(magicka, runtimePatchEnabled, report,
            "DirectInputController", "RightStickUpdate", "ControllerTranspiler", 1,
            "input_telemetry.directinput_hook");
        Check(magicka, runtimePatchEnabled, report,
            "XInputController", "RightStickUpdate", "ControllerTranspiler", 1,
            "input_telemetry.xinput_hook");
    }

    private static void Check(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report,
        string typeName,
        string methodName,
        string transpilerName,
        int expected,
        string scenario)
    {
        try
        {
            Type type = magicka.GetType(
                "Magicka.GameLogic.Controls." + typeName,
                true);
            MethodInfo method = null;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name == methodName)
                {
                    if (method != null)
                        throw new InvalidOperationException("ambiguous target");
                    method = methods[index];
                }
            }
            if (method == null)
                throw new MissingMethodException(type.FullName, methodName);
            List<CodeInstruction> instructions = Decode(method);
            bool applicable = magicka.GetName().Version >=
                new Version(1, 10, 0, 0);
            if (runtimePatchEnabled && applicable)
            {
                MethodInfo transpiler = typeof(Magicka.CommunityPatch.Runtime
                    .ElementSelectionTelemetryPatch).GetMethod(transpilerName);
                instructions = new List<CodeInstruction>(
                    (IEnumerable<CodeInstruction>)transpiler.Invoke(
                        null,
                        new object[] { instructions }));
            }
            int hooks = 0;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo call = instructions[index].operand as MethodInfo;
                if (call == null)
                    continue;
                if (call.Name == "RecordKeyboardElementSelection" ||
                    call.Name == "RecordControllerElementSelection" ||
                    call.Name == "CommunityPatchRecordKeyboardElementSelection" ||
                    call.Name == "CommunityPatchRecordControllerElementSelection")
                    hooks++;
            }
            int desired = applicable ? expected : 0;
            report.Add(
                scenario,
                new ScenarioResult(
                    hooks == desired,
                    "hooks:" + hooks,
                    "hooks:" + desired));
        }
        catch (Exception exception)
        {
            report.Add(
                scenario,
                new ScenarioResult(
                    false,
                    "exception:" + exception.GetType().Name,
                    "hooks:" + expected));
        }
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadElementSelectionTelemetry",
            typeof(void),
            Type.EmptyTypes,
            typeof(ElementSelectionTelemetryScenarios),
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
