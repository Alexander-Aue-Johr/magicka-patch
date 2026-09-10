using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class TomeVersionTextScenarios
{
    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        TomeVersionTextHarness harness = new TomeVersionTextHarness(magicka, runtime);
        report.Add("tome_version.complete_label", harness.CompleteLabel());
        report.Add("tome_version.single_edit", harness.SingleEdit());
    }
}

internal sealed class TomeVersionTextHarness
{
    private List<CodeInstruction> body;
    private readonly ConstructorInfo textConstructor;
    private readonly MethodInfo setText;
    private readonly MethodInfo append;

    internal TomeVersionTextHarness(Assembly magicka, bool runtime)
    {
        Type tome = magicka.GetType("Magicka.GameLogic.UI.Tome", true);
        ConstructorInfo constructor = tome.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        FieldInfo versionField = tome.GetField("sVersionText",
            BindingFlags.Static | BindingFlags.NonPublic);
        Type text = versionField.FieldType;
        ConstructorInfo[] constructors = text.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int index = 0; index < constructors.Length; index++)
        {
            ParameterInfo[] parameters = constructors[index].GetParameters();
            if (parameters.Length == 4 && parameters[0].ParameterType == typeof(int))
                textConstructor = constructors[index];
        }
        setText = text.GetMethod("SetText",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new Type[] { typeof(string) }, null);
        append = text.GetMethod("Append",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new Type[] { typeof(string) }, null);
        body = Decode(constructor);
        if (runtime)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
                .Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.TomeVersionTextPatch", true);
            body = new List<CodeInstruction>((IEnumerable<CodeInstruction>)
                patch.GetMethod("Transpiler").Invoke(null, new object[] { body }));
        }
    }

    internal ScenarioResult CompleteLabel()
    {
        bool capacity = false;
        bool version = false;
        bool safeAppend = false;
        for (int index = 0; index < body.Count; index++)
        {
            if (body[index].opcode == OpCodes.Newobj &&
                SameMethod(body[index].operand as MethodBase, textConstructor))
            {
                for (int previous = Math.Max(0, index - 8);
                    previous < index; previous++)
                {
                    int value;
                    if (TryReadInt(body[previous], out value) && value == 512)
                        capacity = true;
                }
            }
            MethodInfo method = body[index].operand as MethodInfo;
            if (method == null)
                continue;
            if (method.Name == "BuildVersionText" ||
                method.Name == "get_FullVersionText")
                version = true;
            if (method.Name == "AppendTextSafely")
                safeAppend = true;
        }
        string actual = "capacity:" + capacity.ToString().ToLowerInvariant() +
            ",version:" + version.ToString().ToLowerInvariant() +
            ",safe_append:" + safeAppend.ToString().ToLowerInvariant();
        return new ScenarioResult(capacity && version && safeAppend, actual,
            "capacity:true,version:true,safe_append:true");
    }

    internal ScenarioResult SingleEdit()
    {
        int constructors = Count(textConstructor);
        int setters = Count(setText);
        int originalAppends = Count(append);
        int safeAppends = CountNamed("AppendTextSafely");
        bool passed = constructors == 1 && setters == 1 &&
            originalAppends == 0 && safeAppends == 1;
        string actual = "constructors:" + constructors + ",setters:" + setters +
            ",append:" + originalAppends + ",safe:" + safeAppends;
        return new ScenarioResult(passed, actual,
            "constructors:1,setters:1,append:0,safe:1");
    }

    private int Count(MethodBase method)
    {
        if (method == null)
            return 0;
        int count = 0;
        for (int index = 0; index < body.Count; index++)
            if (SameMethod(body[index].operand as MethodBase, method)) count++;
        return count;
    }

    private int CountNamed(string name)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodInfo method = body[index].operand as MethodInfo;
            if (method != null && method.Name == name) count++;
        }
        return count;
    }

    private static bool SameMethod(MethodBase left, MethodBase right)
    {
        if (left == null || right == null)
            return false;
        try
        {
            return left.MetadataToken == right.MetadataToken &&
                left.Module == right.Module;
        }
        catch
        {
            return left.Name == right.Name &&
                left.DeclaringType == right.DeclaringType;
        }
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
        value = 0;
        return false;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod("ReadTomeVersionBody",
            typeof(void), Type.EmptyTypes, typeof(TomeVersionTextHarness), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }
}
