using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class BorderlessPresentationScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        BorderlessPresentationHarness harness =
            new BorderlessPresentationHarness(magicka, runtimePatchEnabled);
        report.Add("borderless.initial_handler_order", harness.HandlerOrder());
        report.Add("borderless.preserve_backbuffer", harness.PreserveBackbuffer());
        report.Add("borderless.logical_fullscreen", harness.Presentation(true));
        report.Add("borderless.windowed", harness.Presentation(false));
        report.Add("borderless.clear_topmost", harness.ClearTopMost(true, false));
        report.Add("borderless.keep_topmost", harness.ClearTopMost(false, false));
    }
}

internal sealed class BorderlessPresentationHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly bool manualHandlerOrder;
    private readonly bool manualPreserve;
    private readonly bool manualWindowedPresentation;
    private readonly bool manualTopMost;

    internal BorderlessPresentationHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type gameType = magicka.GetType("Magicka.Game", true);
        ConstructorInfo constructor = gameType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        Type preparingType = FindLoadedType(
            "Microsoft.Xna.Framework.PreparingDeviceSettingsEventArgs");
        MethodInfo preparing = gameType.GetMethod(
            "mGraphics_PreparingDeviceSettings",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(object), preparingType },
            null);
        MethodInfo update = FindUpdateWithTopMost(gameType);
        if (constructor == null || preparing == null || update == null)
            throw new MissingMethodException("Magicka.Game graphics contract");
        manualHandlerOrder = IsApplyAfterPreparingSubscription(constructor);
        manualPreserve = CallsSetter(preparing, "RenderTargetUsage");
        manualWindowedPresentation = CallsSetter(preparing, "IsFullScreen") &&
            CallsSetter(preparing, "FullScreenRefreshRateInHz");
        manualTopMost = CallsSetter(update, "TopMost");
    }

    private static MethodInfo FindUpdateWithTopMost(Type gameType)
    {
        MethodInfo[] methods = gameType.GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "Update" &&
                CallsSetter(methods[index], "TopMost"))
                return methods[index];
        }
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "Update" &&
                methods[index].GetParameters().Length == 1 &&
                methods[index].GetParameters()[0].ParameterType.FullName ==
                    "Microsoft.Xna.Framework.GameTime")
                return methods[index];
        }
        return null;
    }

    internal ScenarioResult HandlerOrder()
    {
        bool actual = runtimePatchEnabled
            ? GetRuntimeFlag("MovesInitialApplyAfterHandlers")
            : manualHandlerOrder;
        return Result(actual, true);
    }

    internal ScenarioResult PreserveBackbuffer()
    {
        bool actual = runtimePatchEnabled
            ? GetRuntimeFlag("PreservesBackbuffer")
            : manualPreserve;
        return Result(actual, true);
    }

    internal ScenarioResult Presentation(bool logicalFullscreen)
    {
        bool actual;
        if (runtimePatchEnabled)
        {
            actual = InvokeRuntimePolicy(
                "UseWindowedPresentation",
                logicalFullscreen,
                false);
        }
        else
        {
            actual = logicalFullscreen && manualWindowedPresentation;
        }
        return Result(actual, logicalFullscreen);
    }

    internal ScenarioResult ClearTopMost(
        bool logicalFullscreen,
        bool actualFullscreen)
    {
        bool actual;
        if (runtimePatchEnabled)
        {
            actual = InvokeRuntimePolicy(
                "ShouldClearTopMost",
                logicalFullscreen,
                actualFullscreen);
        }
        else
        {
            actual = manualTopMost && logicalFullscreen && !actualFullscreen;
        }
        bool expected = logicalFullscreen && !actualFullscreen;
        return Result(actual, expected);
    }

    private bool GetRuntimeFlag(string fieldName)
    {
        Type type = FindLoadedType(
            "Magicka.CommunityPatch.Runtime.BorderlessPresentationPatch");
        FieldInfo field = type == null
            ? null
            : type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public);
        return field != null && (bool)field.GetValue(null);
    }

    private bool InvokeRuntimePolicy(
        string name,
        bool first,
        bool second)
    {
        Type type = FindLoadedType(
            "Magicka.CommunityPatch.Runtime.BorderlessPresentationPatch");
        MethodInfo method = type == null
            ? null
            : type.GetMethod(name, BindingFlags.Static | BindingFlags.Public);
        return method != null && (bool)method.Invoke(
            null,
            name == "UseWindowedPresentation"
                ? new object[] { first }
                : new object[] { first, second });
    }

    private static bool IsApplyAfterPreparingSubscription(
        ConstructorInfo constructor)
    {
        List<CodeInstruction> instructions = ReadInstructions(constructor);
        int apply = -1;
        int subscribe = -1;
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodBase called = instructions[index].operand as MethodBase;
            if (called == null)
                continue;
            if (called.Name == "ApplyChanges")
                apply = index;
            else if (called.Name == "add_PreparingDeviceSettings")
                subscribe = index;
        }
        return apply > subscribe && subscribe >= 0;
    }

    private static bool CallsSetter(MethodBase method, string property)
    {
        List<CodeInstruction> instructions = ReadInstructions(method);
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodBase called = instructions[index].operand as MethodBase;
            if (called != null && called.Name == "set_" + property)
                return true;
        }
        return false;
    }

    private static List<CodeInstruction> ReadInstructions(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "InspectBorderlessPresentation",
            typeof(void),
            Type.EmptyTypes,
            typeof(BorderlessPresentationHarness),
            true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static ScenarioResult Result(bool actual, bool expected)
    {
        return new ScenarioResult(
            actual == expected,
            actual.ToString(),
            expected.ToString());
    }

    private static Type FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < assemblies.Length; index++)
        {
            Type type = assemblies[index].GetType(fullName, false);
            if (type != null)
                return type;
        }
        return null;
    }
}
