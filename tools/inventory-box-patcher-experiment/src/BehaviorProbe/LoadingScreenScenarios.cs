using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;

internal static class LoadingScreenScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        LoadingScreenHarness harness = new LoadingScreenHarness(magicka);
        try
        {
            report.Add(
                "loading_screen.managed_restore_order",
                harness.EndDraw(true));
            report.Add(
                "loading_screen.unmanaged_no_restore",
                harness.EndDraw(false));
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class LoadingScreenHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-loading-screen";

    private readonly Type loadingScreenType;
    private readonly Type graphicsDeviceType;
    private readonly Type renderStateType;
    private readonly MethodInfo endDraw;
    private readonly FieldInfo deviceField;
    private readonly FieldInfo managedModeField;
    private readonly FieldInfo renderStateField;
    private readonly HarmonyInstance harmony;

    internal LoadingScreenHarness(Assembly magicka)
    {
        loadingScreenType = magicka.GetType(
            "Magicka.GameLogic.GameStates.LoadingScreen",
            true);
        deviceField = RuntimeReflection.RequireField(loadingScreenType, "mDevice");
        managedModeField = RuntimeReflection.RequireField(
            loadingScreenType,
            "mManagedMode");
        graphicsDeviceType = deviceField.FieldType;
        PropertyInfo renderStateProperty = graphicsDeviceType.GetProperty(
            "RenderState",
            BindingFlags.Instance | BindingFlags.Public);
        if (renderStateProperty == null)
            throw new MissingMemberException(graphicsDeviceType.FullName, "RenderState");
        renderStateType = renderStateProperty.PropertyType;
        renderStateField = RuntimeReflection.RequireField(
            graphicsDeviceType,
            "pRenderState");
        endDraw = loadingScreenType.GetMethod(
            "EndDraw",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (endDraw == null || endDraw.ReturnType != typeof(void))
            throw new MissingMethodException(loadingScreenType.FullName, "EndDraw");

        harmony = HarmonyInstance.Create(HarmonyOwner);
        harmony.Patch(
            endDraw,
            null,
            null,
            new HarmonyMethod(typeof(LoadingScreenProbe).GetMethod("Transpiler")));
    }

    internal ScenarioResult EndDraw(bool managedMode)
    {
        object device = NewUninitialized(graphicsDeviceType);
        object renderState = NewUninitialized(renderStateType);
        renderStateField.SetValue(device, renderState);

        object loadingScreen = NewUninitialized(loadingScreenType);
        deviceField.SetValue(loadingScreen, device);
        managedModeField.SetValue(loadingScreen, managedMode);
        LoadingScreenProbe.Operations.Clear();

        string failure = "none";
        try
        {
            endDraw.Invoke(loadingScreen, null);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            failure = inner.GetType().FullName;
        }

        string operations = string.Join(
            ",",
            LoadingScreenProbe.Operations.ToArray());
        string actual = operations + ";failure:" + failure;
        string expected = managedMode
            ? "depth,clear;failure:none"
            : ";failure:none";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(HarmonyOwner);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class LoadingScreenProbe
{
    public static readonly List<string> Operations = new List<string>();

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = new List<CodeInstruction>();
        int depthCalls = 0;
        int clearCalls = 0;
        int renderTargetCalls = 0;
        int renderStateCalls = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            MethodInfo method = instruction.operand as MethodInfo;
            string declaringType = method == null
                ? null
                : method.DeclaringType.FullName;
            if (declaringType == "Microsoft.Xna.Framework.Graphics.GraphicsDevice" &&
                method.Name == "set_DepthStencilBuffer")
            {
                ReplaceCall(result, instruction, 2, "RecordDepth");
                depthCalls++;
            }
            else if (declaringType ==
                    "Microsoft.Xna.Framework.Graphics.GraphicsDevice" &&
                method.Name == "Clear" && method.GetParameters().Length == 4)
            {
                ReplaceCall(result, instruction, 5, "RecordClear");
                clearCalls++;
            }
            else if (declaringType ==
                    "Microsoft.Xna.Framework.Graphics.GraphicsDevice" &&
                method.Name == "SetRenderTarget" &&
                method.GetParameters().Length == 2)
            {
                ReplaceCall(result, instruction, 3, null);
                renderTargetCalls++;
            }
            else if (declaringType ==
                    "Microsoft.Xna.Framework.Graphics.RenderState" &&
                method.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                ReplaceCall(result, instruction, 2, null);
                renderStateCalls++;
            }
            else
            {
                result.Add(instruction);
            }
        }
        if (depthCalls != 1 || clearCalls != 1 || renderTargetCalls != 1 ||
            renderStateCalls != 5)
            throw new InvalidOperationException(
                "Unexpected LoadingScreen.EndDraw device operation shape.");
        return result;
    }

    private static void ReplaceCall(
        List<CodeInstruction> result,
        CodeInstruction source,
        int popCount,
        string recorder)
    {
        for (int index = 0; index < popCount; index++)
        {
            CodeInstruction pop = new CodeInstruction(OpCodes.Pop);
            if (index == 0)
            {
                pop.labels.AddRange(source.labels);
                pop.blocks.AddRange(source.blocks);
            }
            result.Add(pop);
        }
        if (recorder != null)
        {
            result.Add(new CodeInstruction(
                OpCodes.Call,
                typeof(LoadingScreenProbe).GetMethod(recorder)));
        }
    }

    public static void RecordDepth()
    {
        Operations.Add("depth");
    }

    public static void RecordClear()
    {
        Operations.Add("clear");
    }
}
