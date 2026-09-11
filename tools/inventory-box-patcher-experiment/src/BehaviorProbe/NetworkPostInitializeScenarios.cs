using System;
using System.Reflection;

internal static class NetworkPostInitializeScenarios
{
    private sealed class State { public object EntityManager { get; set; } }
    private sealed class Entity { public State PlayState { get; set; } }

    internal static void Run(Assembly magicka, bool runtimePatchEnabled,
        BehaviorReport report)
    {
        bool manual = magicka.GetType(
            "Magicka.CommunityPatch.NetworkLifecycleCompatibility", false) != null;
        bool invalidRejected = manual;
        bool validAccepted = true;
        if (runtimePatchEnabled)
        {
            Type helper = typeof(Magicka.CommunityPatch.Runtime
                .NetworkPostInitializePatch);
            helper.GetMethod("Enter").Invoke(null, new object[] { "client" });
            try
            {
                helper.GetMethod("Validate").Invoke(null, new object[] {
                    new Entity { PlayState = new State { EntityManager = new object() } },
                    "test" });
            }
            catch { validAccepted = false; }
            try
            {
                helper.GetMethod("Validate").Invoke(null, new object[] {
                    new Entity(), "test" });
            }
            catch (TargetInvocationException exception)
            {
                invalidRejected = exception.InnerException != null &&
                    exception.InnerException.GetType().Name ==
                    "NetworkPacketRejectedException";
            }
            helper.GetMethod("Leave").Invoke(null, null);
        }
        report.Add("network_spawn_post_initialize.valid", new ScenarioResult(
            validAccepted, "accepted:" + validAccepted, "accepted:True"));
        report.Add("network_spawn_post_initialize.invalid", new ScenarioResult(
            invalidRejected, "rejected:" + invalidRejected, "rejected:True"));
    }
}
