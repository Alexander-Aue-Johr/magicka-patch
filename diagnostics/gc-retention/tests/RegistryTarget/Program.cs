using System.Diagnostics;
using System.Runtime.CompilerServices;
using Magicka.GcDiagnostics;

string diagnosticsDirectory = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    Path.Combine("CommunityPatch", "diagnostics"));
if (Directory.Exists(diagnosticsDirectory))
{
    Directory.Delete(diagnosticsDirectory, recursive: true);
}

Environment.SetEnvironmentVariable(
    "MAGICKA_GC_DIAGNOSTICS_DIR",
    diagnosticsDirectory);

string manifestDirectory = RunLivePhase(diagnosticsDirectory);

TestRoots.Graph = null;
TestRoots.StateProbe = null;
TestRoots.NormalPoolPeer = null;
TestRoots.ResidentPoolPeer = null;
Thread.Sleep(500);
CollectAfterReturningFromHelper();
RetentionRegistry.Checkpoint("test.CollectedCheckpoint");
string collectedManifest = WaitForManifest(
    manifestDirectory,
    text => !text.Contains(typeof(TrackedTarget).FullName!)
            && !text.Contains(typeof(TrackedOwner).FullName!)
            && !text.Contains(typeof(StateTransitionTarget).FullName!)
            && !text.Contains(typeof(ResidentTransitionTarget).FullName!)
            && text.Contains("# checkpoint\t3"),
    "collected diagnostic candidates");
if (collectedManifest.Contains(typeof(TrackedTarget).FullName!)
    || collectedManifest.Contains(typeof(TrackedOwner).FullName!)
    || collectedManifest.Contains(typeof(StateTransitionTarget).FullName!)
    || collectedManifest.Contains(typeof(ResidentTransitionTarget).FullName!))
{
    throw new InvalidOperationException(
        "Collected weak targets remained in the manifest.");
}

Console.WriteLine("Retention registry integration test passed.");
return 0;

[MethodImpl(MethodImplOptions.NoInlining)]
static string RunLivePhase(string diagnosticsDirectory)
{
    RetentionRegistry.BeginEpoch(new object(), "test.BeginEpoch");
    TrackedGraph graph = CreateTrackedGraph();
    TestRoots.Graph = graph;
    RetentionRegistry.MarkMustCollect(graph.Target, "test.Dispose");
    RetentionRegistry.MarkMustDetach(graph.Owner, "test.Deinitialize");

    StateTransitionTarget stateProbe = new StateTransitionTarget();
    TestRoots.StateProbe = stateProbe;
    RetentionRegistry.Register(stateProbe, "test.StateProbeCtor");
    RetentionRegistry.MarkDeactivated(stateProbe, "test.Deactivated");
    RetentionWatch stateWatch = RetentionRegistry.Watches.Single(
        watch => watch.TypeName == typeof(StateTransitionTarget).FullName);
    AssertWatchState(
        stateWatch,
        RetentionRegistry.Deactivated,
        "test.Deactivated",
        "Deactivated objects must retain their intermediate registry state.");

    WaitForManifest(
        diagnosticsDirectory,
        text => ManifestContainsWatch(
            text,
            typeof(StateTransitionTarget).FullName!,
            "MustCollect",
            "test.Deactivated"),
        "Deactivated candidate serialized as MustCollect");

    RetentionRegistry.MarkMustDetach(stateProbe, "test.CacheInsert");
    AssertWatchState(
        stateWatch,
        RetentionRegistry.MustDetach,
        "test.CacheInsert",
        "A proven cache insertion must transition Deactivated to MustDetach.");

    RetentionRegistry.MarkDeactivated(stateProbe, "test.DeactivatedAgain");
    AssertWatchState(
        stateWatch,
        RetentionRegistry.MustDetach,
        "test.CacheInsert",
        "A later Deactivated event must not downgrade MustDetach.");

    RetentionRegistry.MarkMustCollect(stateProbe, "test.DisposeTerminal");
    AssertWatchState(
        stateWatch,
        RetentionRegistry.MustCollect,
        "test.DisposeTerminal",
        "MustCollect must supersede MustDetach.");

    RetentionRegistry.MarkMustDetach(stateProbe, "test.CacheAfterDispose");
    AssertWatchState(
        stateWatch,
        RetentionRegistry.MustCollect,
        "test.DisposeTerminal",
        "Terminal MustCollect must not transition back to MustDetach.");

    ResidentTransitionTarget normalPoolPeer =
        new ResidentTransitionTarget();
    TestRoots.NormalPoolPeer = normalPoolPeer;
    RetentionRegistry.Register(normalPoolPeer, "test.NormalPeerCtor");
    RetentionRegistry.MarkActive(normalPoolPeer, "test.NormalPeerActive");
    RetentionRegistry.MarkDeactivated(
        normalPoolPeer,
        "test.NormalPeerDeactivated");
    RetentionWatch normalPeerWatch = RetentionRegistry.Watches.Single(
        watch => watch.TypeName
                     == typeof(ResidentTransitionTarget).FullName
                 && watch.LastLifecycle == "test.NormalPeerDeactivated");
    AssertWatchState(
        normalPeerWatch,
        RetentionRegistry.Deactivated,
        "test.NormalPeerDeactivated",
        "A normal instance must not inherit resident-pool state by type.");
    if (normalPeerWatch.IsResidentPool)
    {
        throw new InvalidOperationException(
            "A normal instance was marked as a resident-pool object.");
    }

    ResidentTransitionTarget residentPoolPeer =
        new ResidentTransitionTarget();
    TestRoots.ResidentPoolPeer = residentPoolPeer;
    RetentionRegistry.Register(residentPoolPeer, "test.ResidentPeerCtor");
    RetentionRegistry.MarkResidentActive(
        residentPoolPeer,
        "test.ResidentPeerAcquired");
    RetentionRegistry.MarkActive(
        residentPoolPeer,
        "test.ResidentPeerInitialized");
    RetentionRegistry.MarkDeactivated(
        residentPoolPeer,
        "test.ResidentPeerDeactivated");
    RetentionWatch residentPeerWatch = RetentionRegistry.Watches.Single(
        watch => watch.TypeName
                     == typeof(ResidentTransitionTarget).FullName
                 && watch.LastLifecycle == "test.ResidentPeerDeactivated");
    AssertWatchState(
        residentPeerWatch,
        RetentionRegistry.MustDetach,
        "test.ResidentPeerDeactivated",
        "A resident-pool instance must detach when it is deactivated.");
    if (!residentPeerWatch.IsResidentPool)
    {
        throw new InvalidOperationException(
            "Normal initialization erased resident-pool instance state.");
    }

    RetentionRegistry.Checkpoint("test.ResidentClassificationCheckpoint");
    WaitForManifest(
        diagnosticsDirectory,
        text => ManifestContainsWatch(
                    text,
                    typeof(ResidentTransitionTarget).FullName!,
                    "MustCollect",
                    "test.NormalPeerDeactivated")
                && ManifestContainsWatch(
                    text,
                    typeof(ResidentTransitionTarget).FullName!,
                    "MustDetach",
                    "test.ResidentPeerDeactivated"),
        "per-instance resident-pool classifications");

    RetentionWatch targetWatch = RetentionRegistry.Watches.Single(
        watch => watch.TypeName == typeof(TrackedTarget).FullName);
    long firstStateChange = targetWatch.StateChangedUtcTicks;
    RetentionRegistry.MarkMustCollect(graph.Target, "test.DisposeAgain");
    if (targetWatch.StateChangedUtcTicks != firstStateChange)
    {
        throw new InvalidOperationException(
            "Repeated disposal reset the retirement timestamp.");
    }

    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    RetentionRegistry.Checkpoint("test.AliveCheckpoint");
    string aliveManifest = WaitForManifest(diagnosticsDirectory,
        text => text.Contains("MustCollect") && text.Contains("MustDetach"),
        "live diagnostic candidates");
    if (!aliveManifest.Contains("MustCollect")
        || !aliveManifest.Contains("MustDetach")
        || !aliveManifest.Contains("# magicka-gc-retention-v2")
        || !aliveManifest.Contains("# process-start-utc-ticks\t")
        || !aliveManifest.Contains("# process-path\t")
        || !aliveManifest.Contains("# registry-version\t"))
    {
        throw new InvalidOperationException(
            "Live diagnostic candidates were not written to the manifest.");
    }

    return diagnosticsDirectory;
}

static void AssertWatchState(
    RetentionWatch watch,
    int expectedExpectation,
    string expectedLifecycle,
    string message)
{
    if (watch.Expectation != expectedExpectation
        || watch.LastLifecycle != expectedLifecycle)
    {
        throw new InvalidOperationException(
            message
            + " Expected expectation/lifecycle "
            + expectedExpectation
            + "/"
            + expectedLifecycle
            + ", but found "
            + watch.Expectation
            + "/"
            + watch.LastLifecycle
            + ".");
    }
}

static bool ManifestContainsWatch(
    string manifest,
    string typeName,
    string expectation,
    string lifecycle)
{
    foreach (string line in manifest.Split(
        new[] { '\r', '\n' },
        StringSplitOptions.RemoveEmptyEntries))
    {
        if (line.StartsWith("#", StringComparison.Ordinal)
            || line.StartsWith("id\t", StringComparison.Ordinal))
        {
            continue;
        }

        string[] columns = line.Split('\t');
        if (columns.Length >= 6
            && columns[2] == expectation
            && columns[4] == typeName
            && columns[5] == lifecycle)
        {
            return true;
        }
    }

    return false;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static TrackedGraph CreateTrackedGraph()
{
    TrackedTarget target = new TrackedTarget();
    TrackedOwner owner = new TrackedOwner { Target = target };
    RetentionRegistry.Register(target, "test.TargetCtor");
    RetentionRegistry.Register(owner, "test.OwnerCtor");
    return new TrackedGraph(owner, target);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void CollectAfterReturningFromHelper()
{
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
}

static string WaitForManifest(
    string directory,
    Func<string, bool> predicate,
    string description)
{
    DateTime deadline = DateTime.UtcNow.AddSeconds(8);
    string latest = string.Empty;
    while (DateTime.UtcNow < deadline)
    {
        FileInfo? newest = Directory.Exists(directory)
            ? new DirectoryInfo(directory)
                .GetFiles("retention-*.tsv")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (newest is not null)
        {
            try
            {
                latest = File.ReadAllText(newest.FullName);
                if (predicate(latest))
                {
                    return latest;
                }
            }
            catch (IOException)
            {
            }
        }

        Thread.Sleep(100);
    }

    throw new InvalidOperationException(
        "Timed out waiting for " + description + " in " + directory);
}

static class TestRoots
{
    public static TrackedGraph? Graph;
    public static StateTransitionTarget? StateProbe;
    public static ResidentTransitionTarget? NormalPoolPeer;
    public static ResidentTransitionTarget? ResidentPoolPeer;
}

sealed class TrackedGraph
{
    public TrackedGraph(TrackedOwner owner, TrackedTarget target)
    {
        Owner = owner;
        Target = target;
    }

    public TrackedOwner Owner { get; }
    public TrackedTarget Target { get; }
}

sealed class TrackedOwner
{
    public TrackedTarget? Target;
}

sealed class TrackedTarget
{
}

sealed class ResidentTransitionTarget
{
}

sealed class StateTransitionTarget
{
}
