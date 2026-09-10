using System;
using System.Reflection;
using System.Threading;

internal static class RuntimeTelemetryBackoffScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type helper = runtimePatchEnabled
            ? typeof(Magicka.CommunityPatch.Runtime.RuntimeTelemetryBackoff)
            : magicka.GetType(
                "Magicka.CommunityPatch.NetworkGuardTelemetryBackoff",
                false);
        if (helper == null)
        {
            report.Add("telemetry_backoff.repeat", Missing());
            report.Add("telemetry_backoff.independent_key", Missing());
            report.Add("telemetry_backoff.category_cap", Missing());
            return;
        }

        RuntimeTelemetryBackoffHarness harness =
            new RuntimeTelemetryBackoffHarness(helper);
        report.Add("telemetry_backoff.repeat", harness.Repeat());
        report.Add(
            "telemetry_backoff.independent_key",
            harness.IndependentKey());
        report.Add(
            "telemetry_backoff.category_cap",
            harness.CategoryCap());
    }

    private static ScenarioResult Missing()
    {
        return new ScenarioResult(false, "helper:missing", "helper:available");
    }
}

internal sealed class RuntimeTelemetryBackoffHarness
{
    private readonly Type helper;
    private readonly MethodInfo tryBeginSend;
    private readonly MethodInfo reset;
    private readonly PropertyInfo count;

    internal RuntimeTelemetryBackoffHarness(Type helperType)
    {
        helper = helperType;
        tryBeginSend = helper.GetMethod(
            "TryBeginSend",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[]
            {
                typeof(string), typeof(string), typeof(int).MakeByRefType()
            },
            null);
        if (tryBeginSend == null)
            throw new MissingMethodException(helper.FullName, "TryBeginSend");
        reset = helper.GetMethod(
            "ResetForValidation",
            BindingFlags.Static | BindingFlags.NonPublic);
        count = helper.GetProperty(
            "TrackedCategoryCount",
            BindingFlags.Static | BindingFlags.NonPublic);
    }

    internal ScenarioResult Repeat()
    {
        Reset();
        int firstSkipped;
        int repeatSkipped;
        int resumedSkipped;
        bool first = Invoke("repeat", "same", out firstSkipped);
        bool repeat = Invoke("repeat", "same", out repeatSkipped);
        Thread.Sleep(1100);
        bool resumed = Invoke("repeat", "same", out resumedSkipped);
        bool passed = first && firstSkipped == 0 && !repeat &&
            repeatSkipped == 0 && resumed && resumedSkipped == 1;
        string actual = "first:" + first + ",repeat:" + repeat +
            ",resumed:" + resumed + ",skipped:" + resumedSkipped;
        return new ScenarioResult(
            passed,
            actual,
            "first:True,repeat:False,resumed:True,skipped:1");
    }

    internal ScenarioResult IndependentKey()
    {
        Reset();
        int ignored;
        bool first = Invoke("independent", "a", out ignored);
        bool second = Invoke("independent", "b", out ignored);
        return new ScenarioResult(
            first && second,
            "first:" + first + ",second:" + second,
            "first:True,second:True");
    }

    internal ScenarioResult CategoryCap()
    {
        if (reset == null || count == null)
            return new ScenarioResult(false, "cap:unbounded", "cap:128");
        Reset();
        int ignored;
        for (int index = 0; index < 512; index++)
            Invoke("cap", index.ToString(), out ignored);
        int actual = (int)count.GetValue(null, null);
        return new ScenarioResult(
            actual <= 128,
            "cap:" + actual,
            "cap:128");
    }

    private bool Invoke(string reason, string key, out int skipped)
    {
        object[] arguments = new object[] { reason, key, 0 };
        bool result = (bool)tryBeginSend.Invoke(null, arguments);
        skipped = (int)arguments[2];
        return result;
    }

    private void Reset()
    {
        if (reset != null)
            reset.Invoke(null, null);
    }
}
