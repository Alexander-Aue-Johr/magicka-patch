using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;
using Magicka.CommunityPatch.Runtime;

internal static class ParadoxPopupScenarios
{
    private const string ClearScenario = "paradox_popup.plain_clears_extra";
    private const string ControlScenario = "paradox_popup.with_extra_unchanged";

    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        string reason;
        ParadoxPopupShape shape = ParadoxPopupShape.TryCreate(
            magicka,
            runtimePatchEnabled,
            out reason);
        if (shape == null)
        {
            report.AddNotApplicable(ClearScenario, reason);
            report.AddNotApplicable(ControlScenario, reason);
            return;
        }
        report.Add(ClearScenario, shape.PlainClearsExtra());
        report.Add(ControlScenario, shape.WithExtraUnchanged());
    }
}

internal sealed class ParadoxPopupShape
{
    private readonly List<CodeInstruction> plainInstructions;
    private readonly List<CodeInstruction> extraInstructions;
    private readonly FieldInfo popupField;
    private readonly MethodInfo titleGetter;
    private readonly MethodInfo messageGetter;
    private readonly MethodInfo extraGetter;
    private readonly MethodInfo textSetter;
    private readonly MethodInfo show;

    private ParadoxPopupShape(
        Assembly magicka,
        Type popupUtilsType,
        bool runtimePatchEnabled)
    {
        popupField = popupUtilsType.GetField(
            "sPopup",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (popupField == null)
            throw new MissingFieldException(popupUtilsType.FullName, "sPopup");
        titleGetter = RequireGetter(popupField.FieldType, "Title");
        messageGetter = RequireGetter(popupField.FieldType, "Message");
        extraGetter = RequireGetter(popupField.FieldType, "ExtraMessage");
        show = popupField.FieldType.GetMethod(
            "Show",
            BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo text = extraGetter.ReturnType.GetProperty(
            "Text",
            BindingFlags.Instance | BindingFlags.Public);
        textSetter = text == null ? null : text.GetSetMethod();
        if (show == null || textSetter == null)
            throw new MissingMethodException(
                "Paradox popup display members are incomplete.");

        MethodInfo plain = FindMethod(
            popupUtilsType,
            "ShowErrorPopup",
            new Type[] { typeof(string), typeof(string) });
        MethodInfo withExtra = FindMethod(
            popupUtilsType,
            "ShowErrorPopupWithExtra",
            new Type[] { typeof(int), typeof(string) });
        plainInstructions = Decode(plain);
        extraInstructions = Decode(withExtra);
        if (runtimePatchEnabled)
            plainInstructions = ApplyRuntimeTranspiler(plainInstructions);
    }

    internal static ParadoxPopupShape TryCreate(
        Assembly magicka,
        bool runtimePatchEnabled,
        out string reason)
    {
        Type popupUtilsType = magicka.GetType(
            "Magicka.WebTools.Paradox.ParadoxPopupUtils",
            false);
        if (popupUtilsType == null)
        {
            reason = "The Paradox popup system is not present.";
            return null;
        }
        FieldInfo popup = popupUtilsType.GetField(
            "sPopup",
            BindingFlags.Static | BindingFlags.NonPublic);
        PropertyInfo extra = popup == null
            ? null
            : popup.FieldType.GetProperty(
                "ExtraMessage",
                BindingFlags.Instance | BindingFlags.Public);
        if (extra == null)
        {
            reason = "This version uses the older Paradox popup API.";
            return null;
        }
        reason = null;
        return new ParadoxPopupShape(
            magicka,
            popupUtilsType,
            runtimePatchEnabled);
    }

    internal ScenarioResult PlainClearsExtra()
    {
        bool found = false;
        for (int index = 0; index + 3 < plainInstructions.Count; index++)
        {
            if (plainInstructions[index].opcode == OpCodes.Ldsfld &&
                Object.Equals(plainInstructions[index].operand, popupField) &&
                IsCall(plainInstructions[index + 1], "get_ExtraMessage") &&
                plainInstructions[index + 2].opcode == OpCodes.Ldstr &&
                Object.Equals(plainInstructions[index + 2].operand, "") &&
                IsCall(plainInstructions[index + 3], "set_Text"))
            {
                found = true;
                break;
            }
        }
        string actual = "clear_extra:" + found +
            ",title:" + CountCalls(plainInstructions, "get_Title") +
            ",message:" + CountCalls(plainInstructions, "get_Message") +
            ",show:" + CountCalls(plainInstructions, "Show");
        const string expected = "clear_extra:True,title:1,message:1,show:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult WithExtraUnchanged()
    {
        string actual = "title:" + CountCalls(extraInstructions, "get_Title") +
            ",message:" + CountCalls(extraInstructions, "get_Message") +
            ",extra:" + CountCalls(extraInstructions, "get_ExtraMessage") +
            ",show:" + CountCalls(extraInstructions, "Show");
        const string expected = "title:1,message:1,extra:1,show:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static List<CodeInstruction> Decode(MethodInfo method)
    {
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(null, method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static int CountCalls(
        List<CodeInstruction> instructions,
        string methodName)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (IsCall(instructions[index], methodName))
                count++;
        }
        return count;
    }

    private static bool IsCall(CodeInstruction instruction, string methodName)
    {
        MethodBase method = instruction.operand as MethodBase;
        return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
            method != null && method.Name == methodName;
    }

    private static MethodInfo RequireGetter(Type type, string name)
    {
        PropertyInfo property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo getter = property == null ? null : property.GetGetMethod();
        if (getter == null)
            throw new MissingMemberException(type.FullName, name);
        return getter;
    }

    private static MethodInfo FindMethod(Type type, string name, Type[] parameters)
    {
        MethodInfo method = type.GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != typeof(void))
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static List<CodeInstruction> ApplyRuntimeTranspiler(
        List<CodeInstruction> source)
    {
        Type patchType = typeof(Bootstrap).Assembly.GetType(
            "Magicka.CommunityPatch.Runtime.ParadoxPopupPatch",
            false);
        if (patchType == null)
            return source;
        MethodInfo transpiler = patchType.GetMethod(
            "Transpiler",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (transpiler == null)
            throw new MissingMethodException(patchType.FullName, "Transpiler");
        object result = transpiler.Invoke(null, new object[] { source });
        return new List<CodeInstruction>((IEnumerable<CodeInstruction>)result);
    }
}
