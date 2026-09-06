using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;
using Magicka.CommunityPatch.Runtime;

internal static class DialogLayoutScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        DialogLayoutHarness harness = new DialogLayoutHarness(
            magicka,
            runtimePatchEnabled);
        report.Add("dialog_layout.list_breaks", harness.DialogListBreaks());
        report.Add("dialog_layout.dramatic_aside", harness.DramaticAside());
        report.Add(
            "dialog_layout.existing_list_break",
            harness.ExistingDialogListBreak());
        report.Add("dialog_layout.element_sections", harness.ElementSections());
        report.Add("dialog_layout.ordinary_hint", harness.OrdinaryHint());
        report.Add(
            "dialog_layout.existing_hint_breaks",
            harness.ExistingHintBreaks());
    }
}

internal sealed class DialogLayoutHarness
{
    private readonly MethodInfo dialogHelper;
    private readonly MethodInfo hintHelper;

    internal DialogLayoutHarness(Assembly magicka, bool runtimePatchEnabled)
    {
        MethodInfo messageInitialize = RequireInitialize(
            magicka,
            "Magicka.GameLogic.UI.Message");
        MethodInfo hintInitialize = RequireInitialize(
            magicka,
            "Magicka.Levels.Triggers.Actions.SetDialogHint");
        List<CodeInstruction> messageInstructions = Decode(
            messageInitialize,
            "DialogMessageDecode");
        List<CodeInstruction> hintInstructions = Decode(
            hintInitialize,
            "DialogHintDecode");

        if (runtimePatchEnabled)
        {
            Type patchType = typeof(Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.DialogLayoutPatch",
                true);
            ApplyTranspiler(patchType, "MessageTranspiler", messageInstructions);
            ApplyTranspiler(patchType, "HintTranspiler", hintInstructions);
        }

        dialogHelper = FindHelper(
            messageInstructions,
            "RestoreDialogListBreaks");
        hintHelper = FindHelper(
            hintInstructions,
            "RestoreElementHintBreaks");
    }

    internal ScenarioResult DialogListBreaks()
    {
        string input = "Intro[P=1]   - First[P=2]\t- Second";
        string expected = "Intro[P=1]\n- First[P=2]\n- Second";
        return Transform(dialogHelper, input, expected);
    }

    internal ScenarioResult DramaticAside()
    {
        string input = "Intro[P=1] -- aside";
        return Transform(dialogHelper, input, input);
    }

    internal ScenarioResult ExistingDialogListBreak()
    {
        string input = "Intro[P=1]\n- First";
        return Transform(dialogHelper, input, input);
    }

    internal ScenarioResult ElementSections()
    {
        string input =
            "Intro  #TYPE;Fire  Water  #PROP;Hot  Cold  #OPP;Cold  Fire";
        string expected =
            "Intro\n\n#TYPE;Fire\nWater\n\n#PROP;Hot\nCold\n\n#OPP;Cold\nFire";
        return Transform(hintHelper, input, expected);
    }

    internal ScenarioResult OrdinaryHint()
    {
        string input = "Ordinary hint  with intentional spacing";
        return Transform(hintHelper, input, input);
    }

    internal ScenarioResult ExistingHintBreaks()
    {
        string input =
            "Intro\n\n#TYPE;Fire\nWater\n\n#PROP;Hot\nCold\n\n#OPP;Cold\nFire";
        return Transform(hintHelper, input, input);
    }

    private static ScenarioResult Transform(
        MethodInfo helper,
        string input,
        string expected)
    {
        string actual = helper == null
            ? input
            : (string)helper.Invoke(null, new object[] { input });
        return new ScenarioResult(
            actual == expected,
            Escape(actual),
            Escape(expected));
    }

    private static string Escape(string value)
    {
        return value.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static MethodInfo RequireInitialize(Assembly magicka, string typeName)
    {
        Type type = magicka.GetType(typeName, true);
        MethodInfo method = type.GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (method == null || method.ReturnType != typeof(void))
            throw new MissingMethodException(typeName, "Initialize");
        return method;
    }

    private static List<CodeInstruction> Decode(
        MethodInfo method,
        string dynamicMethodName)
    {
        DynamicMethod target = new DynamicMethod(
            dynamicMethodName,
            typeof(void),
            Type.EmptyTypes,
            typeof(DialogLayoutHarness),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static void ApplyTranspiler(
        Type patchType,
        string name,
        List<CodeInstruction> instructions)
    {
        MethodInfo transpiler = patchType.GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patchType.FullName, name);
        object transformed = transpiler.Invoke(null, new object[] { instructions });
        List<CodeInstruction> result = new List<CodeInstruction>(
            (IEnumerable<CodeInstruction>)transformed);
        instructions.Clear();
        instructions.AddRange(result);
    }

    private static MethodInfo FindHelper(
        List<CodeInstruction> instructions,
        string name)
    {
        MethodInfo match = null;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode != OpCodes.Call &&
                instructions[index].opcode != OpCodes.Callvirt)
                continue;
            MethodInfo method = instructions[index].operand as MethodInfo;
            if (!IsHelper(method, name))
                continue;
            if (match != null)
                throw new InvalidOperationException(
                    "Multiple " + name + " calls were found.");
            match = method;
        }
        return match;
    }

    private static bool IsHelper(MethodInfo method, string name)
    {
        if (method == null || method.Name != name ||
            method.IsStatic == false || method.ReturnType != typeof(string))
            return false;
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 1 &&
            parameters[0].ParameterType == typeof(string);
    }
}
