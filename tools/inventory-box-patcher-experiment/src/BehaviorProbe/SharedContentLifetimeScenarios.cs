using System;
using System.Reflection;

internal static class SharedContentLifetimeScenarios
{
    internal static void Run(
        Assembly magicka, bool runtime, BehaviorReport report)
    {
        Type common = magicka.GetType(
            "Magicka.SharedContentManager+CommonContentManager", true);
        Type referenced = magicka.GetType(
            "Magicka.SharedContentManager+CommonContentManager+ReferencedAsset",
            true);
        Type disposableReference = magicka.GetType(
            "Magicka.SharedContentManager+CommonContentManager+DisposableReference",
            false);
        Type runtimeType = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
            .Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.SharedContentLifetimePatch",
                true);

        Add(report, "shared_content.disposable_callback",
            runtime || referenced.GetField("DisposableObjects",
                AllFields) != null, "callback_registered");
        Add(report, "shared_content.duplicate_children",
            runtime || referenced.GetField("IsTopLevel", AllFields) != null,
            "deduplicated");
        Add(report, "shared_content.release",
            runtimeType.GetMethod("ReleasePostfix") != null && runtime ||
                common.GetMethod("DisposeIfNotTrackedAsset", AllMethods) != null,
            "conditional_dispose");
        Add(report, "shared_content.final_release",
            runtimeType.GetMethod("ForceCurrentCommon") != null && runtime ||
                common.GetMethod("ForceDisposeAllDisposableAssets",
                    AllMethods) != null,
            "final_dispose");
        bool weak = runtime || disposableReference != null &&
            disposableReference.GetField("Reference", AllFields) != null &&
            disposableReference.GetField("Reference", AllFields).FieldType ==
                typeof(WeakReference);
        Add(report, "shared_content.weak_tracking", weak, "weak_reference");
    }

    private const BindingFlags AllFields =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const BindingFlags AllMethods =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static void Add(
        BehaviorReport report, string name, bool passed, string expected)
    {
        report.Add(name, new ScenarioResult(passed,
            passed ? expected : "original_lifetime", expected));
    }
}
