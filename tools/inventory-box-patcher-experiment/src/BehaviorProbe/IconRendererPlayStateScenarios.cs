using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class IconRendererPlayStateScenarios
{
    private static readonly string[] ScenarioNames = new string[]
    {
        "icon_renderer.constructor_state_release",
        "icon_renderer.initialize_state_release",
        "icon_renderer.current_game_type",
        "icon_renderer.selection_shape"
    };

    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type renderer = magicka.GetType(
            "Magicka.GameLogic.UI.IconRenderer",
            false);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            false);
        if (renderer == null || playState == null)
        {
            AddNotApplicable(report, "IconRenderer play-state contract is absent.");
            return;
        }

        IconRendererPlayStateHarness harness;
        try
        {
            harness = new IconRendererPlayStateHarness(
                renderer,
                playState,
                runtimePatchEnabled);
        }
        catch (MissingMemberException)
        {
            AddNotApplicable(report, "IconRenderer play-state contract is absent.");
            return;
        }

        report.Add(ScenarioNames[0], harness.ConstructorRelease());
        report.Add(ScenarioNames[1], harness.InitializeRelease());
        report.Add(ScenarioNames[2], harness.CurrentGameType());
        report.Add(ScenarioNames[3], harness.SelectionShape());
    }

    private static void AddNotApplicable(BehaviorReport report, string reason)
    {
        for (int index = 0; index < ScenarioNames.Length; index++)
            report.AddNotApplicable(ScenarioNames[index], reason);
    }
}

internal sealed class IconRendererPlayStateHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo legacyPlayState;
    private readonly FieldInfo displayedMagick;
    private readonly ConstructorInfo constructor;
    private readonly MethodInfo initialize;
    private readonly MethodInfo setter;
    private readonly MethodInfo recentPlayState;
    private readonly MethodInfo isMagickAllowed;

    internal IconRendererPlayStateHarness(
        Type renderer,
        Type playState,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        legacyPlayState = renderer.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        displayedMagick = RuntimeReflection.RequireField(
            renderer,
            "mDisplayedMagick");

        Type player = renderer.Assembly.GetType(
            "Magicka.GameLogic.Player",
            true);
        constructor = renderer.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { player, playState },
            null);
        initialize = renderer.GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playState },
            null);
        PropertyInfo tomeMagick = renderer.GetProperty(
            "TomeMagick",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        setter = tomeMagick == null ? null : tomeMagick.GetSetMethod();
        PropertyInfo recent = playState.GetProperty(
            "RecentPlayState",
            BindingFlags.Static | BindingFlags.Public);
        recentPlayState = recent == null ? null : recent.GetGetMethod();
        Type spellManager = renderer.Assembly.GetType(
            "Magicka.GameLogic.SpellManager",
            false);
        if (spellManager == null)
            spellManager = renderer.Assembly.GetType(
                "Magicka.GameLogic.Spells.SpellManager",
                false);
        isMagickAllowed = FindIsMagickAllowed(spellManager);

        if (constructor == null || initialize == null || setter == null ||
            recentPlayState == null || isMagickAllowed == null)
            throw new MissingMemberException(
                "IconRenderer play-state contract is incomplete.");
    }

    internal ScenarioResult ConstructorRelease()
    {
        List<CodeInstruction> instructions = Decode(constructor);
        ApplyIfRuntime("ConstructorTranspiler", instructions);
        return CountResult(
            "stores",
            Count(instructions, OpCodes.Stfld, legacyPlayState),
            0);
    }

    internal ScenarioResult InitializeRelease()
    {
        List<CodeInstruction> instructions = Decode(initialize);
        ApplyIfRuntime("InitializeTranspiler", instructions);
        return CountResult(
            "stores",
            Count(instructions, OpCodes.Stfld, legacyPlayState),
            0);
    }

    internal ScenarioResult CurrentGameType()
    {
        List<CodeInstruction> instructions = Decode(setter);
        ApplyIfRuntime("SetterTranspiler", instructions);
        int legacyReads = Count(
            instructions,
            OpCodes.Ldfld,
            legacyPlayState);
        int currentReads = Count(
            instructions,
            OpCodes.Call,
            recentPlayState);
        string actual = "legacy_reads:" + legacyReads +
            ",current_reads:" + currentReads;
        const string expected = "legacy_reads:0,current_reads:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult SelectionShape()
    {
        List<CodeInstruction> instructions = Decode(setter);
        ApplyIfRuntime("SetterTranspiler", instructions);
        int availabilityCalls = CountCalls(instructions, isMagickAllowed);
        int displayedStores = Count(
            instructions,
            OpCodes.Stfld,
            displayedMagick);
        string actual = "availability_calls:" + availabilityCalls +
            ",displayed_stores:" + displayedStores;
        const string expected = "availability_calls:1,displayed_stores:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private void ApplyIfRuntime(
        string methodName,
        List<CodeInstruction> instructions)
    {
        if (!runtimePatchEnabled)
            return;
        Type patch = Type.GetType(
            "Magicka.CommunityPatch.Runtime.IconRendererPlayStatePatch, " +
            "Magicka.CommunityPatch.Runtime",
            false);
        if (patch == null)
            return;
        MethodInfo transpiler = patch.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patch.FullName, methodName);
        object transformed = transpiler.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange((IEnumerable<CodeInstruction>)transformed);
    }

    private static MethodInfo FindIsMagickAllowed(Type spellManager)
    {
        if (spellManager == null)
            return null;
        MethodInfo[] methods = spellManager.GetMethods(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        MethodInfo found = null;
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "IsMagickAllowed" ||
                methods[index].ReturnType != typeof(bool) ||
                methods[index].GetParameters().Length != 3)
                continue;
            if (found != null)
                throw new InvalidOperationException(
                    "Multiple SpellManager.IsMagickAllowed methods matched.");
            found = methods[index];
        }
        return found;
    }

    private static int Count(
        List<CodeInstruction> instructions,
        OpCode opcode,
        object operand)
    {
        if (operand == null)
            return 0;
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode == opcode &&
                Object.Equals(instructions[index].operand, operand))
                count++;
        }
        return count;
    }

    private static int CountCalls(
        List<CodeInstruction> instructions,
        MethodInfo method)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                Object.Equals(instructions[index].operand, method))
                count++;
        }
        return count;
    }

    private static ScenarioResult CountResult(
        string name,
        int actualValue,
        int expectedValue)
    {
        string actual = name + ":" + actualValue;
        string expected = name + ":" + expectedValue;
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadIconRendererBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(IconRendererPlayStateHarness),
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
