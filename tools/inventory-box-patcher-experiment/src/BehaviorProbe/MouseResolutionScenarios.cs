using System;
using System.Reflection;

internal static class MouseResolutionScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        MouseResolutionHarness harness =
            new MouseResolutionHarness(magicka, runtimePatchEnabled);
        report.Add("mouse_resolution.lower", harness.Scale(960, 1920, 1280, 640));
        report.Add("mouse_resolution.higher", harness.Scale(960, 1920, 2560, 1280));
        report.Add("mouse_resolution.negative", harness.Scale(-10, 1920, 1280, 0));
        report.Add("mouse_resolution.upper_bound", harness.Scale(1920, 1920, 1280, 1279));
        report.Add("mouse_resolution.equal", harness.Scale(640, 1280, 1280, 640));
    }
}

internal sealed class MouseResolutionHarness
{
    private readonly MethodInfo scaleCoordinate;

    internal MouseResolutionHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        Type helper = runtimePatchEnabled
            ? FindLoadedType(
                "Magicka.CommunityPatch.Runtime.MouseResolutionPatch")
            : magicka.GetType(
                "Magicka.CommunityPatch.MouseInputCompatibility",
                false);
        scaleCoordinate = helper == null
            ? null
            : helper.GetMethod(
                "ScaleCoordinate",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
    }

    internal ScenarioResult Scale(
        int coordinate,
        int physicalSize,
        int logicalSize,
        int expected)
    {
        int actual = scaleCoordinate == null
            ? coordinate
            : (int)scaleCoordinate.Invoke(
                null,
                new object[] { coordinate, physicalSize, logicalSize });
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
