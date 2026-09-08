using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GiveOrderKhanScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        GiveOrderKhanHarness harness =
            new GiveOrderKhanHarness(magicka, runtimePatchEnabled);
        report.Add("give_order_khan.terminated", harness.TerminatedKhan());
        report.Add("give_order_khan.live", harness.LiveKhan());
        report.Add("give_order_khan.other_order", harness.OtherOrder());
        report.Add("give_order_khan.zero_trigger", harness.ZeroTrigger());
    }
}

internal sealed class GiveOrderKhanHarness
{
    private const string RuntimePatchTypeName =
        "Magicka.CommunityPatch.Runtime.GiveOrderKhanFallbackPatch";

    private readonly bool runtimePatchEnabled;
    private readonly Type giveOrderType;
    private readonly Type warlordType;
    private readonly Type gameSceneType;
    private readonly Type networkManagerType;
    private readonly Type aiEventType;
    private readonly FieldInfo networkSingleton;
    private readonly FieldInfo eventTypeField;
    private readonly FieldInfo animationEventField;
    private readonly FieldInfo animationTriggerField;
    private readonly object animationEventType;
    private readonly MethodInfo manualFallback;
    private readonly MethodInfo runtimeFallback;

    internal GiveOrderKhanHarness(Assembly magicka, bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        giveOrderType = magicka.GetType(
            "Magicka.Levels.Triggers.Actions.GiveOrder",
            true);
        warlordType = magicka.GetType(
            "Magicka.GameLogic.Entities.Bosses.WarlordCharacter",
            true);
        gameSceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        networkManagerType = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        aiEventType = magicka.GetType("Magicka.AI.AIEvent", true);
        networkSingleton = RuntimeReflection.RequireField(
            networkManagerType,
            "sSingelton");
        eventTypeField = RuntimeReflection.RequireField(aiEventType, "EventType");
        animationEventField = RuntimeReflection.RequireField(
            aiEventType,
            "AnimationEvent");
        animationTriggerField = RuntimeReflection.RequireField(
            animationEventField.FieldType,
            "Trigger");
        animationEventType = Enum.Parse(
            eventTypeField.FieldType,
            "Animation");

        manualFallback = giveOrderType.GetMethod(
            "CommunityPatchHandleTerminatedKhan",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Type patchType = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
            RuntimePatchTypeName,
            false);
        runtimeFallback = patchType == null
            ? null
            : patchType.GetMethod(
                "Handle",
                BindingFlags.Static | BindingFlags.Public);
    }

    internal ScenarioResult TerminatedKhan()
    {
        return RunScenario("#boss_n06", true, 726441, true);
    }

    internal ScenarioResult LiveKhan()
    {
        return RunScenario("#boss_n06", false, 726441, false);
    }

    internal ScenarioResult OtherOrder()
    {
        return RunScenario("#other_boss", true, 726441, false);
    }

    internal ScenarioResult ZeroTrigger()
    {
        return RunScenario("#boss_n06", true, 0, false);
    }

    private ScenarioResult RunScenario(
        string orderId,
        bool terminated,
        int trigger,
        bool expectedInvocation)
    {
        object previousNetwork = networkSingleton.GetValue(null);
        string settingsPath = DisableManualTelemetry();
        try
        {
            networkSingleton.SetValue(
                null,
                FormatterServices.GetUninitializedObject(networkManagerType));

            object order = FormatterServices.GetUninitializedObject(giveOrderType);
            object khan = FormatterServices.GetUninitializedObject(warlordType);
            GC.SuppressFinalize(khan);
            RuntimeReflection.WriteField(order, "mID", orderId);
            RuntimeReflection.WriteField(order, "mEvents", NewEvents(trigger));
            RuntimeReflection.WriteField(order, "mScene", NewSceneWithoutTriggers());
            RuntimeReflection.WriteField(khan, "mDead", terminated);

            bool invoked = InvokeFallback(order, khan);
            return new ScenarioResult(
                invoked == expectedInvocation,
                invoked ? "trigger_invoked" : "trigger_not_invoked",
                expectedInvocation ? "trigger_invoked" : "trigger_not_invoked");
        }
        finally
        {
            RestoreManualTelemetry(settingsPath);
            networkSingleton.SetValue(null, previousNetwork);
        }
    }

    private Array NewEvents(int trigger)
    {
        object animation = Activator.CreateInstance(animationEventField.FieldType);
        animationTriggerField.SetValue(animation, trigger);

        object aiEvent = Activator.CreateInstance(aiEventType);
        eventTypeField.SetValue(aiEvent, animationEventType);
        animationEventField.SetValue(aiEvent, animation);

        Array events = Array.CreateInstance(aiEventType, 1);
        events.SetValue(aiEvent, 0);
        return events;
    }

    private object NewSceneWithoutTriggers()
    {
        object scene = FormatterServices.GetUninitializedObject(gameSceneType);
        GC.SuppressFinalize(scene);
        FieldInfo triggers = RuntimeReflection.RequireField(gameSceneType, "mTriggers");
        triggers.SetValue(scene, Activator.CreateInstance(triggers.FieldType));
        return scene;
    }

    private bool InvokeFallback(object order, object khan)
    {
        MethodInfo fallback = manualFallback;
        if (fallback == null && runtimePatchEnabled)
            fallback = runtimeFallback;
        if (fallback == null)
            return false;

        try
        {
            fallback.Invoke(
                fallback.IsStatic ? null : order,
                fallback.IsStatic
                    ? new object[] { order, khan }
                    : new object[] { khan });
            return false;
        }
        catch (TargetInvocationException exception)
        {
            Exception current = exception;
            while (current is TargetInvocationException && current.InnerException != null)
                current = current.InnerException;
            return current is System.Collections.Generic.KeyNotFoundException;
        }
    }

    private string DisableManualTelemetry()
    {
        if (manualFallback == null)
            return null;

        string directory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "CommunityPatch");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "patch-settings.ini");
        File.WriteAllText(path, "usage_sharing=false");
        return path;
    }

    private static void RestoreManualTelemetry(string path)
    {
        if (path != null && File.Exists(path))
            File.Delete(path);
    }
}
