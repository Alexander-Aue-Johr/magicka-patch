using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class RuntimeTelemetryContextScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type context = runtimePatchEnabled
            ? typeof(Magicka.CommunityPatch.Runtime.RuntimeTelemetryContext)
            : magicka.GetType(
                "Magicka.CommunityPatch.TelemetryRuntimeContext",
                false);
        if (context == null)
        {
            report.Add("telemetry_context.navigation", Missing());
            report.Add("telemetry_context.display", Missing());
            report.Add("telemetry_context.language", Missing());
            report.Add("telemetry_context.navigation_bound", Missing());
            report.Add("telemetry_context.payload", Missing());
            report.Add("telemetry_context.resolution_setter", Missing());
            return;
        }

        RuntimeTelemetryContextHarness harness =
            new RuntimeTelemetryContextHarness(context);
        report.Add("telemetry_context.navigation", harness.Navigation());
        report.Add("telemetry_context.display", harness.Display());
        report.Add("telemetry_context.language", harness.Language());
        report.Add(
            "telemetry_context.navigation_bound",
            harness.NavigationBound());
        report.Add("telemetry_context.payload", harness.Payload());
        report.Add(
            "telemetry_context.resolution_setter",
            ResolutionSetter(magicka));
    }

    private static ScenarioResult ResolutionSetter(Assembly magicka)
    {
        try
        {
            Type settingsType = magicka.GetType("Magicka.GlobalSettings", true);
            PropertyInfo resolutionProperty = settingsType.GetProperty(
                "Resolution",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Type resolutionType = resolutionProperty.PropertyType;
            object resolution = Activator.CreateInstance(resolutionType);
            resolutionType.GetField("Width").SetValue(resolution, 2560);
            resolutionType.GetField("Height").SetValue(resolution, 1440);
            object settings = FormatterServices.GetUninitializedObject(
                settingsType);
            resolutionProperty.SetValue(settings, resolution, null);
            object stored = resolutionProperty.GetValue(settings, null);
            bool passed = (int)resolutionType.GetField("Width").GetValue(stored)
                    == 2560 &&
                (int)resolutionType.GetField("Height").GetValue(stored) == 1440;
            return new ScenarioResult(
                passed,
                passed ? "setter:completed" : "setter:value_mismatch",
                "setter:completed");
        }
        catch (Exception exception)
        {
            Exception failure = exception is TargetInvocationException &&
                exception.InnerException != null
                    ? exception.InnerException
                    : exception;
            return new ScenarioResult(
                false,
                "setter:" + failure.GetType().Name,
                "setter:completed");
        }
    }

    private static ScenarioResult Missing()
    {
        return new ScenarioResult(false, "context:missing", "context:available");
    }
}

internal sealed class RuntimeTelemetryContextHarness
{
    private readonly MethodInfo recordPlayState;
    private readonly MethodInfo recordScene;
    private readonly MethodInfo recordMenu;
    private readonly MethodInfo recordLanguage;
    private readonly MethodInfo recordResolution;
    private readonly MethodInfo recordUiScale;
    private readonly MethodInfo addProperties;
    private readonly MethodInfo reset;

    internal RuntimeTelemetryContextHarness(Type context)
    {
        recordPlayState = Require(context, "RecordPlayState", 2);
        recordScene = Require(context, "RecordScene", 1);
        recordMenu = Require(context, "RecordMenu", 0);
        recordLanguage = Require(context, "RecordLanguage", 1);
        recordResolution = Require(context, "RecordResolution", 2);
        recordUiScale = Require(context, "RecordUiScale", 1);
        addProperties = Require(context, "AddProperties", 1);
        reset = context.GetMethod(
            "ResetForValidation",
            BindingFlags.Static | BindingFlags.NonPublic);
    }

    internal ScenarioResult Navigation()
    {
        Reset();
        recordPlayState.Invoke(null, new object[]
        {
            @"C:\Games\Magicka\content\Levels\Tsar\Tsar_Mountaindale.lvl",
            "Mountaindale"
        });
        recordScene.Invoke(null, new object[] { "scene_one" });
        recordMenu.Invoke(null, null);
        recordMenu.Invoke(null, null);
        Dictionary<string, string> values = Snapshot();
        string expected = "Tsar\\Tsar_Mountaindale.lvl -> " +
            "Mountaindale -> scene_one -> Menu";
        bool passed = values["navigation_history"] == expected &&
            values["playstate_count"] == "1" &&
            values["scene_transition_count"] == "1" &&
            values["navigation_history_truncated"] == "false";
        return new ScenarioResult(
            passed,
            "history:" + values["navigation_history"] +
                ",playstates:" + values["playstate_count"] +
                ",scenes:" + values["scene_transition_count"],
            "history:" + expected + ",playstates:1,scenes:1");
    }

    internal ScenarioResult Display()
    {
        recordResolution.Invoke(null, new object[] { 3840, 2160 });
        recordUiScale.Invoke(null, new object[] { 2f });
        Dictionary<string, string> values = Snapshot();
        bool passed = values["resolution_width"] == "3840" &&
            values["resolution_height"] == "2160" &&
            values["ui_scale_percent"] == "200";
        return new ScenarioResult(
            passed,
            "resolution:" + values["resolution_width"] + "x" +
                values["resolution_height"] + ",scale:" +
                values["ui_scale_percent"],
            "resolution:3840x2160,scale:200");
    }

    internal ScenarioResult Language()
    {
        recordLanguage.Invoke(null, new object[]
        {
            "missing_validation_language"
        });
        Dictionary<string, string> values = Snapshot();
        long bytes;
        int files;
        bool passed = values["language"] ==
                "missing_validation_language" &&
            values["glyph_font_source"] == "eng" &&
            Int32.TryParse(values["glyph_file_count"], out files) &&
            Int64.TryParse(values["glyph_total_bytes"], out bytes) &&
            (values["glyph_fingerprint_status"] == "ok" ||
                values["glyph_fingerprint_status"] == "missing");
        return new ScenarioResult(
            passed,
            "language:" + values["language"] + ",source:" +
                values["glyph_font_source"] + ",status:" +
                values["glyph_fingerprint_status"],
            "language:missing_validation_language,source:eng," +
                "status:ok_or_missing");
    }

    internal ScenarioResult NavigationBound()
    {
        Reset();
        string label = new string('x', 160);
        for (int index = 0; index < 40; index++)
            recordPlayState.Invoke(null, new object[] { label, label });
        Dictionary<string, string> values = Snapshot();
        bool passed = values["navigation_history"].Length <= 4096 &&
            values["navigation_history_truncated"] == "true";
        return new ScenarioResult(
            passed,
            "length:" + values["navigation_history"].Length +
                ",truncated:" + values["navigation_history_truncated"],
            "length_at_most:4096,truncated:true");
    }

    internal ScenarioResult Payload()
    {
        Reset();
        recordPlayState.Invoke(null, new object[]
        {
            @"D:\Games\Magicka\content\Levels\Test\Payload.lvl",
            "Payload"
        });
        recordScene.Invoke(null, new object[] { "payload_scene" });
        Dictionary<string, string> values = Snapshot();
        string payload =
            Magicka.CommunityPatch.Runtime.RuntimePatchTelemetry
                .BuildPayloadForValidation(
                    "validation_event",
                    values);
        bool passed = payload.Contains("payload_scene") &&
            payload.Contains("\"navigation_history\":") &&
            payload.Contains("\"playstate_count\":") &&
            payload.Contains("\"scene_transition_count\":") &&
            payload.Contains("\"ui_scale_percent\":") &&
            !payload.Contains("D:\\\\Games");
        return new ScenarioResult(
            passed,
            "payload_context:" + (passed ? "present" : "invalid"),
            "payload_context:present");
    }

    private Dictionary<string, string> Snapshot()
    {
        Dictionary<string, string> values =
            new Dictionary<string, string>();
        addProperties.Invoke(null, new object[] { values });
        return values;
    }

    private void Reset()
    {
        if (reset != null)
            reset.Invoke(null, null);
    }

    private static MethodInfo Require(Type type, string name, int parameters)
    {
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic);
        MethodInfo match = null;
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != name ||
                methods[index].GetParameters().Length != parameters)
                continue;
            if (match != null)
                throw new InvalidOperationException(
                    "Multiple telemetry context methods matched " + name + ".");
            match = methods[index];
        }
        if (match == null)
            throw new MissingMethodException(type.FullName, name);
        return match;
    }
}
