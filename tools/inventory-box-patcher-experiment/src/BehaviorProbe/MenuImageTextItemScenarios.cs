using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;
using Magicka.CommunityPatch.Runtime;

internal static class MenuImageTextItemScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        MenuImageTextItemShape shape = new MenuImageTextItemShape(
            magicka,
            runtimePatchEnabled);
        report.Add(
            "menu_image_text.literal_font_change",
            shape.LiteralFontChange());
        report.Add(
            "menu_image_text.localized_font_change",
            shape.LocalizedFontChange());
        report.Add(
            "menu_image_text.localized_unchanged_font",
            shape.LocalizedUnchangedFont());
    }
}

internal sealed class MenuImageTextItemShape
{
    private readonly List<CodeInstruction> instructions;
    private readonly FieldInfo textIdField;
    private readonly FieldInfo titleField;
    private readonly FieldInfo fontField;
    private readonly FieldInfo lineHeightField;
    private readonly MethodInfo lineHeightGetter;
    private readonly MethodInfo markAsDirty;
    private readonly MethodInfo getString;
    private readonly MethodInfo setText;

    internal MenuImageTextItemShape(Assembly magicka, bool runtimePatchEnabled)
    {
        Type itemType = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.MenuImageTextItem",
            true);
        Type fontType = RuntimeReflection.FindLoadedType("PolygonHead.BitmapFont");
        Type textType = RuntimeReflection.FindLoadedType("PolygonHead.Text");
        Type languageManagerType = magicka.GetType(
            "Magicka.Localization.LanguageManager",
            true);

        MethodInfo languageChanged = itemType.GetMethod(
            "LanguageChanged",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (languageChanged == null || languageChanged.ReturnType != typeof(void))
            throw new MissingMethodException(itemType.FullName, "LanguageChanged");

        textIdField = RuntimeReflection.RequireField(itemType, "mText");
        titleField = RuntimeReflection.RequireField(itemType, "mTitle");
        fontField = RuntimeReflection.RequireField(itemType, "mFont");
        lineHeightField = RuntimeReflection.RequireField(itemType, "mLineHeight");
        PropertyInfo lineHeight = fontType.GetProperty(
            "LineHeight",
            BindingFlags.Instance | BindingFlags.Public);
        lineHeightGetter = lineHeight == null ? null : lineHeight.GetGetMethod();
        markAsDirty = textType.GetMethod(
            "MarkAsDirty",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        getString = languageManagerType.GetMethod(
            "GetString",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(int) },
            null);
        setText = textType.GetMethod(
            "SetText",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(string) },
            null);
        if (lineHeightGetter == null || markAsDirty == null || getString == null ||
            setText == null || FindUpdateBoundingBox(itemType) == null)
            throw new MissingMethodException(
                "MenuImageTextItem language dependencies are incomplete.");

        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            null,
            languageChanged);
        instructions = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            instructions.Add(decoded[index].GetCodeInstruction());

        if (runtimePatchEnabled)
            instructions = ApplyRuntimeTranspiler(instructions);
    }

    internal ScenarioResult LiteralFontChange()
    {
        bool lineRefresh = HasLineHeightRefresh();
        bool literalDirty = HasLiteralDirtyPath();
        string actual = "line_refresh:" + lineRefresh +
            ",literal_dirty:" + literalDirty;
        const string expected = "line_refresh:True,literal_dirty:True";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult LocalizedFontChange()
    {
        bool lineRefresh = HasLineHeightRefresh();
        bool localizedRefresh = HasLocalizedRefreshPath();
        string actual = "line_refresh:" + lineRefresh +
            ",localized_refresh:" + localizedRefresh;
        const string expected = "line_refresh:True,localized_refresh:True";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult LocalizedUnchangedFont()
    {
        bool localizedRefresh = HasLocalizedRefreshPath();
        int setTextCalls = CountCalls(setText);
        int boundingBoxCalls = CountCalls("UpdateBoundingBox");
        string actual = "localized_refresh:" + localizedRefresh +
            ",set_calls:" + setTextCalls +
            ",bounds_calls:" + boundingBoxCalls;
        const string expected =
            "localized_refresh:True,set_calls:1,bounds_calls:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private bool HasLineHeightRefresh()
    {
        for (int index = 5; index < instructions.Count; index++)
        {
            if (instructions[index].opcode != OpCodes.Stfld ||
                !Object.Equals(instructions[index].operand, lineHeightField))
                continue;
            return instructions[index - 5].opcode == OpCodes.Ldarg_0 &&
                instructions[index - 4].opcode == OpCodes.Ldarg_0 &&
                instructions[index - 3].opcode == OpCodes.Ldfld &&
                Object.Equals(instructions[index - 3].operand, fontField) &&
                IsCall(instructions[index - 2], lineHeightGetter) &&
                instructions[index - 1].opcode == OpCodes.Conv_R4;
        }
        return false;
    }

    private bool HasLiteralDirtyPath()
    {
        for (int index = 0; index + 6 < instructions.Count; index++)
        {
            if (instructions[index].opcode != OpCodes.Ldarg_0 ||
                instructions[index + 1].opcode != OpCodes.Ldfld ||
                !Object.Equals(instructions[index + 1].operand, textIdField) ||
                !IsBranchWhenTrue(instructions[index + 2].opcode) ||
                instructions[index + 3].opcode != OpCodes.Ldarg_0 ||
                instructions[index + 4].opcode != OpCodes.Ldfld ||
                !Object.Equals(instructions[index + 4].operand, titleField) ||
                !IsCall(instructions[index + 5], markAsDirty) ||
                instructions[index + 6].opcode != OpCodes.Ret)
                continue;
            return true;
        }
        return false;
    }

    private bool HasLocalizedRefreshPath()
    {
        return CountCalls(getString) == 1 &&
            CountCalls(setText) == 1 &&
            CountCalls("UpdateBoundingBox") == 1;
    }

    private int CountCalls(MethodInfo method)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (IsCall(instructions[index], method))
                count++;
        }
        return count;
    }

    private int CountCalls(string methodName)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodBase method = instructions[index].operand as MethodBase;
            if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                method != null && method.Name == methodName)
                count++;
        }
        return count;
    }

    private static bool IsCall(CodeInstruction instruction, MethodInfo method)
    {
        return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
            Object.Equals(instruction.operand, method);
    }

    private static bool IsBranchWhenTrue(OpCode opcode)
    {
        return opcode == OpCodes.Brtrue || opcode == OpCodes.Brtrue_S;
    }

    private static MethodInfo FindUpdateBoundingBox(Type itemType)
    {
        for (Type current = itemType; current != null; current = current.BaseType)
        {
            MethodInfo method = current.GetMethod(
                "UpdateBoundingBox",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method != null)
                return method;
        }
        return null;
    }

    private static List<CodeInstruction> ApplyRuntimeTranspiler(
        List<CodeInstruction> source)
    {
        Type patchType = typeof(Bootstrap).Assembly.GetType(
            "Magicka.CommunityPatch.Runtime.MenuImageTextItemPatch",
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
