using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class TomeShadowMapClearScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        TomeShadowMapClearHarness harness =
            new TomeShadowMapClearHarness(magicka, runtimePatchEnabled);
        report.Add("tome_shadow.depth_clear", harness.DepthClear());
        report.Add("tome_shadow.single_clear", harness.SingleClear());
    }
}

internal sealed class TomeShadowMapClearHarness
{
    private readonly List<CodeInstruction> body;
    private readonly MethodInfo colorOnlyClear;
    private readonly MethodInfo targetAndDepthClear;

    internal TomeShadowMapClearHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        Type tome = magicka.GetType("Magicka.GameLogic.UI.Tome", true);
        Type renderData = tome.GetNestedType(
            "RenderData",
            BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo draw = renderData.GetMethod(
            "DrawShadows",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (draw == null)
            throw new MissingMethodException(renderData.FullName, "DrawShadows");

        Type device = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Graphics.GraphicsDevice");
        Type color = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Graphics.Color");
        Type options = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Graphics.ClearOptions");
        colorOnlyClear = RequireMethod(
            device,
            "Clear",
            new Type[] { color });
        targetAndDepthClear = RequireMethod(
            device,
            "Clear",
            new Type[] { options, color, typeof(float), typeof(int) });

        body = Decode(draw);
        if (runtimePatchEnabled)
        {
            Type patch = typeof(
                Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.TomeShadowMapClearPatch",
                    true);
            object transformed = patch.GetMethod("Transpiler").Invoke(
                null,
                new object[] { body });
            body = new List<CodeInstruction>(
                (IEnumerable<CodeInstruction>)transformed);
        }
    }

    internal ScenarioResult DepthClear()
    {
        int index = FindCall(targetAndDepthClear);
        bool flags = index >= 4 &&
            ContainsInt(index - 4, index, 3) &&
            ContainsFloat(index - 4, index, 1f) &&
            ContainsInt(index - 4, index, 0);
        string actual = "full:" + (index >= 0 ? "1" : "0") +
            ",arguments:" + flags.ToString().ToLowerInvariant();
        return new ScenarioResult(
            index >= 0 && flags,
            actual,
            "full:1,arguments:true");
    }

    internal ScenarioResult SingleClear()
    {
        int count = CountCalls(colorOnlyClear) +
            CountCalls(targetAndDepthClear);
        return new ScenarioResult(
            count == 1,
            "clears:" + count,
            "clears:1");
    }

    private int FindCall(MethodInfo method)
    {
        for (int index = 0; index < body.Count; index++)
        {
            if (body[index].opcode == OpCodes.Callvirt &&
                Object.Equals(body[index].operand, method))
                return index;
        }
        return -1;
    }

    private int CountCalls(MethodInfo method)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            if (body[index].opcode == OpCodes.Callvirt &&
                Object.Equals(body[index].operand, method))
                count++;
        }
        return count;
    }

    private bool ContainsInt(int start, int end, int expected)
    {
        for (int index = start; index < end; index++)
        {
            int value;
            if (TryReadInt(body[index], out value) && value == expected)
                return true;
        }
        return false;
    }

    private bool ContainsFloat(int start, int end, float expected)
    {
        for (int index = start; index < end; index++)
        {
            if (body[index].opcode == OpCodes.Ldc_R4 &&
                Math.Abs((float)body[index].operand - expected) < 0.0001f)
                return true;
        }
        return false;
    }

    private static bool TryReadInt(CodeInstruction instruction, out int value)
    {
        if (instruction.opcode == OpCodes.Ldc_I4)
        {
            value = (int)instruction.operand;
            return true;
        }
        if (instruction.opcode == OpCodes.Ldc_I4_S)
        {
            value = Convert.ToInt32(instruction.operand);
            return true;
        }
        OpCode[] shortForms = new OpCode[]
        {
            OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2,
            OpCodes.Ldc_I4_3, OpCodes.Ldc_I4_4, OpCodes.Ldc_I4_5,
            OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7, OpCodes.Ldc_I4_8
        };
        for (int index = 0; index < shortForms.Length; index++)
        {
            if (instruction.opcode == shortForms[index])
            {
                value = index;
                return true;
            }
        }
        value = 0;
        return false;
    }

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        Type[] parameters)
    {
        MethodInfo method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            parameters,
            null);
        if (method == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadTomeShadowBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(TomeShadowMapClearHarness),
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
