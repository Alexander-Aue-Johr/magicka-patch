using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class MissileConditionInitializationScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        Type missile = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity", true);
        FieldInfo field = missile.GetField(
            "mConditionCollection",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        Type conditions = field.FieldType;
        MethodInfo target = FindTarget(missile, conditions);
        List<CodeInstruction> body = Decode(target);
        if (runtimePatchEnabled)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
                .Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.MissileConditionInitializationPatch",
                    true);
            MethodInfo transpiler = patch.GetMethod("Transpiler");
            body = new List<CodeInstruction>(
                (IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null, new object[] { body }));
        }
        int calls = 0;
        bool internalReceiver = false;
        for (int index = 4; index < body.Count; index++)
        {
            MethodInfo called = body[index].operand as MethodInfo;
            if (called == null || called.Name != "ExecuteAll" ||
                called.DeclaringType != conditions ||
                called.GetParameters().Length != 3)
                continue;
            calls++;
            internalReceiver =
                body[index - 5].opcode == OpCodes.Ldarg_0 &&
                body[index - 4].opcode == OpCodes.Ldfld &&
                Object.Equals(body[index - 4].operand, field);
        }
        report.Add(
            "missile_condition_init.null_input",
            Result(
                internalReceiver,
                internalReceiver ? "internal_collection" : "input_parameter",
                "internal_collection"));
        report.Add(
            "missile_condition_init.single_default_call",
            Result(calls == 1, "calls:" + calls, "calls:1"));
    }

    private static MethodInfo FindTarget(Type missile, Type conditions)
    {
        MethodInfo[] methods = missile.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name == "Initialize" &&
                parameters.Length == 7 &&
                parameters[5].ParameterType == conditions)
                return methods[index];
        }
        throw new MissingMethodException(missile.FullName, "Initialize");
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadMissileInitialize", typeof(void), Type.EmptyTypes,
            typeof(MissileConditionInitializationScenarios), true);
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
