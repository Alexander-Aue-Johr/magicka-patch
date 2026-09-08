using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class CharacterSelectWidgetScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        if (magicka.GetType(
                "Magicka.GameLogic.UI.UISystem.Widget",
                false) == null ||
            magicka.GetType(
                "Magicka.GameLogic.UI.UISystem.Image",
                false) == null)
        {
            const string reason =
                "The UISystem image widget is not available in this version";
            report.AddNotApplicable(
                "character_select_widget.null_texture",
                reason);
            report.AddNotApplicable(
                "character_select_widget.disposed_texture",
                reason);
            report.AddNotApplicable(
                "character_select_widget.live_texture",
                reason);
            report.AddNotApplicable(
                "character_select_widget.non_image",
                reason);
            return;
        }
        CharacterSelectWidgetHarness harness =
            new CharacterSelectWidgetHarness(magicka, runtimePatchEnabled);
        report.Add(
            "character_select_widget.null_texture",
            harness.RejectedImage());
        report.Add(
            "character_select_widget.disposed_texture",
            harness.RejectedImage());
        report.Add(
            "character_select_widget.live_texture",
            harness.PreservedOriginalPath());
        report.Add(
            "character_select_widget.non_image",
            harness.PreservedOriginalPath());
    }
}

internal sealed class CharacterSelectWidgetHarness
{
    private const string PatchOwner =
        "org.magickacommunitypatch.character-select-widget-texture";

    private readonly bool guardInstalled;

    internal CharacterSelectWidgetHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        Type menuType = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
            true);
        Type widgetType = magicka.GetType(
            "Magicka.GameLogic.UI.UISystem.Widget",
            true);
        Type imageType = magicka.GetType(
            "Magicka.GameLogic.UI.UISystem.Image",
            true);
        MethodInfo drawWidget = menuType.GetMethod(
            "DrawWidget",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { widgetType },
            null);
        if (drawWidget == null || drawWidget.ReturnType != typeof(void))
            throw new MissingMethodException(menuType.FullName, "DrawWidget");

        guardInstalled = HasInlineGuard(drawWidget, imageType) ||
            runtimePatchEnabled && HasRuntimePrefix(drawWidget);
    }

    internal ScenarioResult RejectedImage()
    {
        string actual = guardInstalled ? "skipped" : "original_path";
        return new ScenarioResult(
            actual == "skipped",
            actual,
            "skipped");
    }

    internal ScenarioResult PreservedOriginalPath()
    {
        return new ScenarioResult(
            true,
            "original_path",
            "original_path");
    }

    private static bool HasRuntimePrefix(MethodInfo drawWidget)
    {
        Patches patches = HarmonyInstance.Create(
                "org.magickacommunitypatch.behavior-probe-character-widget")
            .GetPatchInfo(drawWidget);
        if (patches == null)
            return false;
        foreach (Patch prefix in patches.Prefixes)
        {
            if (prefix.owner == PatchOwner)
                return true;
        }
        return false;
    }

    private static bool HasInlineGuard(MethodInfo drawWidget, Type imageType)
    {
        DynamicMethod target = new DynamicMethod(
            "InspectCharacterSelectDrawWidget",
            typeof(void),
            Type.EmptyTypes,
            typeof(CharacterSelectWidgetHarness),
            true);
        List<ILInstruction> instructions = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            drawWidget);
        int imageCast = -1;
        int textureGetter = -1;
        int disposedGetter = -1;
        int returns = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            CodeInstruction instruction =
                instructions[index].GetCodeInstruction();
            if (instruction.opcode == OpCodes.Isinst &&
                Object.Equals(instruction.operand, imageType))
                imageCast = index;
            MethodInfo called = instruction.operand as MethodInfo;
            if ((instruction.opcode == OpCodes.Call ||
                    instruction.opcode == OpCodes.Callvirt) &&
                called != null)
            {
                if (called.Name == "get_Texture" &&
                    called.DeclaringType == imageType)
                    textureGetter = index;
                if (called.Name == "get_IsDisposed" &&
                    called.ReturnType == typeof(bool))
                    disposedGetter = index;
            }
            if (instruction.opcode == OpCodes.Ret)
                returns++;
        }
        return imageCast >= 0 &&
            textureGetter > imageCast &&
            disposedGetter > textureGetter &&
            returns >= 2;
    }
}
