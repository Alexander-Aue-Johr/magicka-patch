using System;
using System.Reflection;

internal static class WidescreenSafeAreaScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type helper = magicka.GetType(
            "Magicka.CommunityPatch.WidescreenSafeArea",
            false);
        if (helper == null && runtimePatchEnabled)
            helper = typeof(
                Magicka.CommunityPatch.Runtime.WidescreenSafeAreaPatch);
        report.Add(
            "widescreen.horizontal_ultrawide",
            Horizontal(helper, 5120, 1440, 1280f));
        report.Add(
            "widescreen.horizontal_16_9",
            Horizontal(helper, 1920, 1080, 0f));
        report.Add(
            "widescreen.right_ultrawide",
            Right(helper, 5120, 1440, 300f, 3562f));
        report.Add(
            "widescreen.right_16_9",
            Right(helper, 1920, 1080, 300f, 1674f));
    }

    private static ScenarioResult Horizontal(
        Type helper,
        int width,
        int height,
        float expected)
    {
        if (helper == null)
            return Missing(expected);
        MethodInfo method = helper.GetMethod(
            "GetHorizontalInset",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            new Type[] { typeof(int), typeof(int) },
            null);
        if (method == null)
            return Missing(expected);
        float actual = Convert.ToSingle(
            method.Invoke(null, new object[] { width, height }));
        return Result(actual, expected);
    }

    private static ScenarioResult Right(
        Type helper,
        int width,
        int height,
        float contentWidth,
        float expected)
    {
        if (helper == null)
            return Missing(expected);
        MethodInfo method = helper.GetMethod(
            "GetRightAlignedCentre",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            new Type[] { typeof(int), typeof(int), typeof(float) },
            null);
        if (method == null)
            return Missing(expected);
        float actual = Convert.ToSingle(
            method.Invoke(
                null,
                new object[] { width, height, contentWidth }));
        return Result(actual, expected);
    }

    private static ScenarioResult Result(float actual, float expected)
    {
        return new ScenarioResult(
            Math.Abs(actual - expected) < 0.001f,
            actual.ToString("R"),
            expected.ToString("R"));
    }

    private static ScenarioResult Missing(float expected)
    {
        return new ScenarioResult(false, "helper_missing", expected.ToString("R"));
    }
}
