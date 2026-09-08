using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;
using Harmony.ILCopying;

internal static class GraphicsStartupErrorScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        GraphicsStartupErrorHarness harness =
            new GraphicsStartupErrorHarness(magicka, runtimePatchEnabled);
        report.Add("graphics_error.xna_argument", harness.XnaArgument());
        report.Add("graphics_error.other_argument", harness.OtherArgument());
        report.Add(
            "graphics_error.no_suitable_device",
            harness.NoSuitableDevice());
        report.Add("graphics_error.other_exception", harness.OtherException());
    }
}

internal sealed class GraphicsStartupErrorHarness
{
    private const string AdapterMessageStart =
        "Magicka could not map the selected graphics adapter to a monitor.";
    private const string DeviceMessageStart =
        "Magicka could not find a suitable graphics device.";

    private readonly bool runtimePatchEnabled;
    private readonly bool manualMessagesPresent;
    private readonly Type noSuitableGraphicsDeviceType;

    internal GraphicsStartupErrorHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type programType = magicka.GetType("Magicka.Program", true);
        MethodInfo writeReport = programType.GetMethod(
            "WriteReport",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic);
        if (writeReport == null)
            throw new MissingMethodException(programType.FullName, "WriteReport");
        manualMessagesPresent = ContainsString(writeReport, AdapterMessageStart) &&
            ContainsString(writeReport, DeviceMessageStart);
        noSuitableGraphicsDeviceType = FindLoadedType(
            "Microsoft.Xna.Framework.NoSuitableGraphicsDeviceException");
        if (noSuitableGraphicsDeviceType == null)
            throw new TypeLoadException(
                "Microsoft.Xna.Framework.NoSuitableGraphicsDeviceException");
    }

    internal ScenarioResult XnaArgument()
    {
        return Evaluate(
            new ArgumentException(
                "Microsoft.Xna.Framework.GraphicsDeviceManager adapter failure"),
            "adapter");
    }

    internal ScenarioResult OtherArgument()
    {
        return Evaluate(new ArgumentException("unrelated"), "none");
    }

    internal ScenarioResult NoSuitableDevice()
    {
        object exception = FormatterServices.GetUninitializedObject(
            noSuitableGraphicsDeviceType);
        return Evaluate(exception, "device");
    }

    internal ScenarioResult OtherException()
    {
        return Evaluate(new InvalidOperationException("unrelated"), "none");
    }

    private ScenarioResult Evaluate(object exception, string expected)
    {
        string actual;
        Type patchType = runtimePatchEnabled
            ? FindLoadedType(
                "Magicka.CommunityPatch.Runtime.GraphicsStartupErrorPatch")
            : null;
        MethodInfo classifier = patchType == null
            ? null
            : patchType.GetMethod(
                "ClassifyGraphicsStartupException",
                BindingFlags.Static | BindingFlags.Public);
        if (classifier != null)
        {
            actual = (string)classifier.Invoke(null, new object[] { exception });
        }
        else if (manualMessagesPresent)
        {
            actual = ClassifyManual(exception);
        }
        else
        {
            actual = "none";
        }
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private string ClassifyManual(object exception)
    {
        ArgumentException argument = exception as ArgumentException;
        if (argument != null &&
            argument.ToString().Contains("Microsoft.Xna.Framework"))
            return "adapter";
        return exception != null &&
            exception.GetType().FullName ==
                "Microsoft.Xna.Framework.NoSuitableGraphicsDeviceException"
            ? "device"
            : "none";
    }

    private static bool ContainsString(MethodInfo method, string prefix)
    {
        DynamicMethod target = new DynamicMethod(
            "InspectGraphicsStartupMessages",
            typeof(void),
            Type.EmptyTypes,
            typeof(GraphicsStartupErrorHarness),
            true);
        List<ILInstruction> instructions = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        for (int index = 0; index < instructions.Count; index++)
        {
            CodeInstruction instruction =
                instructions[index].GetCodeInstruction();
            string value = instruction.operand as string;
            if (instruction.opcode == OpCodes.Ldstr && value != null &&
                value.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
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
