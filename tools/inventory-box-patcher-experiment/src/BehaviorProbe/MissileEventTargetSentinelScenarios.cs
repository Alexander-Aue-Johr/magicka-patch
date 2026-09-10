using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class MissileEventTargetSentinelScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The explicit missile target sentinel is a current-version patch.";
            report.AddNotApplicable(
                "missile_event_target.update_sentinel", reason);
            report.AddNotApplicable(
                "missile_event_target.collision_sentinel", reason);
            return;
        }
        Type missile = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity", true);
        Type message = magicka.GetType(
            "Magicka.Network.MissileEntityEventMessage", true);
        FieldInfo target = message.GetField(
            "TargetHandle",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        MethodInfo update = FindMethod(missile, "Update", 2);
        MethodInfo collision = FindMethod(missile, "OnCollision", 4);
        Check(
            "missile_event_target.update_sentinel",
            update, message, target, runtimePatchEnabled, report);
        Check(
            "missile_event_target.collision_sentinel",
            collision, message, target, runtimePatchEnabled, report);
    }

    private static void Check(
        string scenario,
        MethodInfo method,
        Type message,
        FieldInfo target,
        bool runtime,
        BehaviorReport report)
    {
        List<CodeInstruction> body = Decode(method);
        if (runtime)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
                .Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.MissileEventTargetSentinelPatch",
                    true);
            body = new List<CodeInstruction>(
                (IEnumerable<CodeInstruction>)patch.GetMethod("Transpiler")
                    .Invoke(null, new object[] { body }));
        }
        int initializers = 0;
        int sentinels = 0;
        for (int index = 0; index < body.Count; index++)
        {
            if (body[index].opcode == OpCodes.Initobj &&
                body[index].operand as Type == message)
                initializers++;
            if (index >= 1 && body[index].opcode == OpCodes.Stfld &&
                Object.Equals(body[index].operand, target) &&
                LoadsMaxUShort(body[index - 1]))
                sentinels++;
        }
        bool passed = initializers == 1 && sentinels == 1;
        report.Add(
            scenario,
            new ScenarioResult(
                passed,
                "initializers:" + initializers + ",sentinels:" + sentinels,
                "initializers:1,sentinels:1"));
    }

    private static bool LoadsMaxUShort(CodeInstruction instruction)
    {
        if (instruction.opcode == OpCodes.Ldc_I4)
            return Convert.ToInt32(instruction.operand) == ushort.MaxValue;
        return false;
    }

    private static MethodInfo FindMethod(Type type, string name, int parameters)
    {
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
            if (methods[index].Name == name &&
                methods[index].GetParameters().Length == parameters)
                return methods[index];
        throw new MissingMethodException(type.FullName, name);
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadMissileMessageProducer", typeof(void), Type.EmptyTypes,
            typeof(MissileEventTargetSentinelScenarios), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result =
            new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }
}
