using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Diagnostics.Runtime;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

AnalyzerOptions options;
try
{
    options = AnalyzerOptions.Parse(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    PrintUsage();
    return 2;
}

if (Environment.Is64BitProcess)
{
    Console.Error.WriteLine(
        "Magicka is x86. Run this analyzer with the x86 dotnet host:");
    Console.Error.WriteLine(
        "  & 'C:\\Program Files (x86)\\dotnet\\dotnet.exe'"
        + " .\\Magicka.GcAnalyzer.dll <manifest.tsv>");
    return 3;
}

ManifestDocument document;
try
{
    document = ManifestDocument.Load(options.ManifestPath);
}
catch (Exception exception)
{
    Console.Error.WriteLine("Could not read retention manifest: " + exception.Message);
    return 4;
}

RetentionManifest manifest = document.Manifest;
long nowUtcTicks = DateTime.UtcNow.Ticks;
List<WatchRecord> eligible = manifest.Watches
    .Where(watch => IsEligible(watch, options, nowUtcTicks))
    .ToList();

Report report;
try
{
    report = new Report(options.OutputPath, options.TelemetryOutputPath);
}
catch (Exception exception)
{
    Console.Error.WriteLine("Could not create analysis report: " + exception.Message);
    return 4;
}

using (report)
{
    report.TelemetryValue(
        "candidate_count",
        eligible.Count.ToString(CultureInfo.InvariantCulture));
    report.Line("Magicka GC retention analysis");
    report.Line("UTC: " + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
    report.Line("Manifest: " + options.ManifestPath);
    report.Line(
        "PID: " + manifest.ProcessId.ToString(CultureInfo.InvariantCulture)
        + ", epoch: " + manifest.Epoch.ToString(CultureInfo.InvariantCulture)
        + ", checkpoint: "
        + manifest.Checkpoint.ToString(CultureInfo.InvariantCulture));
    report.Line(
        "Candidates: " + eligible.Count.ToString(CultureInfo.InvariantCulture)
        + " eligible / "
        + manifest.Watches.Count.ToString(CultureInfo.InvariantCulture)
        + " recorded");
    report.Line();

    TargetProcessInfo targetProcess;
    try
    {
        document.EnsureUnchanged("before process validation");
        targetProcess = ValidateTargetProcess(manifest);
    }
    catch (ManifestChangedException exception)
    {
        report.TelemetryValue("status", "manifest_changed");
        report.Line("MANIFEST CHANGED: " + exception.Message);
        return 7;
    }
    catch (TargetProcessException exception)
    {
        report.TelemetryValue("status", "target_identity_error");
        report.Line("TARGET IDENTITY ERROR: " + exception.Message);
        return 5;
    }

    report.Line(
        "Target: " + targetProcess.ProcessName + " ("
        + manifest.ProcessId.ToString(CultureInfo.InvariantCulture) + ")");
    report.Line("Target path: " + targetProcess.ProcessPath);

    string analysisStage = "snapshot_attach";
    try
    {
        document.EnsureUnchanged("immediately before the snapshot");

        using DataTarget target = DataTarget.CreateSnapshotAndAttach(
            manifest.ProcessId);

        analysisStage = "snapshot_validation";
        document.EnsureUnchanged("immediately after the snapshot");
        ValidateTargetProcess(manifest);

        analysisStage = "clr_discovery";
        if (target.ClrVersions.Count == 0)
        {
            throw new InvalidOperationException("No CLR runtime was found in Magicka.");
        }

        ClrInfo clrInfo = target.ClrVersions[0];
        report.Line(
            "CLR: " + clrInfo.Version + " (" + clrInfo.Flavor + ")");
        analysisStage = "runtime_create";
        ClrRuntime runtime = clrInfo.CreateRuntime();
        analysisStage = "registry_validation";
        ValidateRegistryVersion(runtime, manifest);

        if (eligible.Count == 0)
        {
            report.TelemetryValue("resolved_count", "0");
            report.TelemetryValue("status", "no_candidates");
            report.Line();
            report.Line(
                "No candidate met its expectation-specific eligibility rule:"
                + " minimum age for MustCollect; minimum age plus a later"
                + " generation-2 collection for MustDetach.");
            return 0;
        }

        analysisStage = "heap_validation";
        ClrHeap heap = runtime.Heap;
        if (!heap.CanWalkHeap)
        {
            throw new InvalidOperationException("ClrMD cannot walk this GC heap.");
        }

        analysisStage = "handle_enumeration";
        Dictionary<ulong, ClrHandle> handles = new Dictionary<ulong, ClrHandle>();
        foreach (ClrHandle handle in runtime.EnumerateHandles())
        {
            if (!handles.ContainsKey(handle.Address))
            {
                handles.Add(handle.Address, handle);
            }
        }

        analysisStage = "candidate_resolution";
        List<ResolvedWatch> resolved = new List<ResolvedWatch>();
        int rejectedCandidateCount = 0;
        foreach (WatchRecord watch in eligible)
        {
            if (!handles.TryGetValue(watch.HandleAddress, out ClrHandle? handle)
                || handle is null)
            {
                report.Line(
                    "UNRESOLVED #" + watch.Id + " " + watch.TypeName
                    + ": weak handle 0x"
                    + watch.HandleAddress.ToString("x", CultureInfo.InvariantCulture)
                    + " was not present in the snapshot.");
                rejectedCandidateCount++;
                continue;
            }

            if (handle.HandleType != HandleType.WeakShort)
            {
                report.Line(
                    "REUSED/STALE #" + watch.Id + " " + watch.TypeName
                    + ": handle 0x"
                    + watch.HandleAddress.ToString("x", CultureInfo.InvariantCulture)
                    + " has kind " + handle.HandleType
                    + "; expected WeakShort.");
                rejectedCandidateCount++;
                continue;
            }

            ClrObject value = heap.GetObject(handle.Object);
            if (value.IsNull || value.Type is null || value.Type.IsFree)
            {
                report.Line(
                    "COLLECTED #" + watch.Id + " " + watch.TypeName
                    + ": the weak handle no longer has a live target.");
                continue;
            }

            string actualTypeName = value.Type?.Name ?? string.Empty;
            if (!string.Equals(
                    actualTypeName,
                    watch.TypeName,
                    StringComparison.Ordinal))
            {
                report.Line(
                    "REUSED/STALE #" + watch.Id + " " + watch.TypeName
                    + ": handle 0x"
                    + watch.HandleAddress.ToString("x", CultureInfo.InvariantCulture)
                    + " points to "
                    + (actualTypeName.Length == 0 ? "<unknown>" : actualTypeName)
                    + " instead.");
                rejectedCandidateCount++;
                continue;
            }

            resolved.Add(new ResolvedWatch(watch, value));
        }

        report.Line(
            "Resolved live candidates: "
            + resolved.Count.ToString(CultureInfo.InvariantCulture));
        report.TelemetryValue(
            "resolved_count",
            resolved.Count.ToString(CultureInfo.InvariantCulture));
        report.Line();

        analysisStage = "root_analysis";
        AnalyzeMustCollect(heap, resolved, options, report);
        AnalyzeMustDetach(resolved, options, report);
        if (rejectedCandidateCount != 0)
        {
            report.TelemetryValue("status", "incomplete");
            report.Line(
                "INCOMPLETE: "
                + rejectedCandidateCount.ToString(
                    CultureInfo.InvariantCulture)
                + " eligible candidate(s) had an unresolved, reused, or"
                + " mismatched weak handle. Valid candidates above were"
                + " analysed; rerun with the latest manifest.");
            return 8;
        }

        report.TelemetryValue("status", "completed");
    }
    catch (ManifestChangedException exception)
    {
        report.TelemetryValue("status", "manifest_changed");
        report.Line();
        report.Line("MANIFEST CHANGED: " + exception.Message);
        report.Line(
            "The snapshot was discarded. Run the analyzer again against the"
            + " latest manifest.");
        return 7;
    }
    catch (TargetProcessException exception)
    {
        report.TelemetryValue("status", "target_identity_error");
        report.Line();
        report.Line("TARGET IDENTITY ERROR: " + exception.Message);
        report.Line(
            "The snapshot was discarded because the PID no longer identifies"
            + " the manifest process.");
        return 5;
    }
    catch (Exception exception)
    {
        report.TelemetryValue("status", "analyzer_error");
        report.TelemetryValue("error_stage", analysisStage);
        report.TelemetryValue(
            "exception_type",
            exception.GetType().FullName ?? exception.GetType().Name);
        report.TelemetryValue(
            "inner_exception_type",
            exception.InnerException?.GetType().FullName ?? string.Empty);
        report.TelemetryValue(
            "exception_hresult",
            exception.HResult.ToString("x8", CultureInfo.InvariantCulture));
        report.Line();
        report.Line("ANALYZER ERROR: " + exception);
        return 6;
    }

    return 0;
}

static bool IsEligible(
    WatchRecord watch,
    AnalyzerOptions options,
    long nowUtcTicks)
{
    if (options.IncludeYoung)
    {
        return true;
    }

    long minimumAgeTicks =
        TimeSpan.FromSeconds(options.MinimumAgeSeconds).Ticks;
    bool oldEnough = watch.StateUtcTicks > 0
                     && nowUtcTicks >= watch.StateUtcTicks
                     && nowUtcTicks - watch.StateUtcTicks >= minimumAgeTicks;
    if (!oldEnough)
    {
        return false;
    }

    if (watch.Expectation == "MustCollect")
    {
        return true;
    }

    return watch.Expectation == "MustDetach"
           && watch.CurrentGen2 > watch.Gen2AtState;
}

static TargetProcessInfo ValidateTargetProcess(RetentionManifest manifest)
{
    try
    {
        using Process process = Process.GetProcessById(manifest.ProcessId);
        long actualStartUtcTicks = process.StartTime.ToUniversalTime().Ticks;
        ProcessModule? mainModule = process.MainModule;
        string actualPath = Path.GetFullPath(
            mainModule?.FileName
            ?? throw new InvalidOperationException(
                "The target main module path is unavailable."));
        string expectedPath = Path.GetFullPath(manifest.ProcessPath);
        string expectedProcessName = Path.GetFileNameWithoutExtension(expectedPath);

        if (actualStartUtcTicks != manifest.ProcessStartUtcTicks)
        {
            throw new TargetProcessException(
                "PID " + manifest.ProcessId.ToString(CultureInfo.InvariantCulture)
                + " started at "
                + actualStartUtcTicks.ToString(CultureInfo.InvariantCulture)
                + " ticks, but the manifest records "
                + manifest.ProcessStartUtcTicks.ToString(CultureInfo.InvariantCulture)
                + ".");
        }

        if (!string.Equals(
                actualPath,
                expectedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new TargetProcessException(
                "PID " + manifest.ProcessId.ToString(CultureInfo.InvariantCulture)
                + " runs '" + actualPath + "', but the manifest records '"
                + expectedPath + "'.");
        }

        if (!string.Equals(
                process.ProcessName,
                expectedProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new TargetProcessException(
                "PID " + manifest.ProcessId.ToString(CultureInfo.InvariantCulture)
                + " is named '" + process.ProcessName
                + "', but its manifest path implies '"
                + expectedProcessName + "'.");
        }

        return new TargetProcessInfo(process.ProcessName, actualPath);
    }
    catch (TargetProcessException)
    {
        throw;
    }
    catch (Exception exception)
    {
        throw new TargetProcessException(
            "Could not validate PID "
            + manifest.ProcessId.ToString(CultureInfo.InvariantCulture)
            + ": " + exception.Message,
            exception);
    }
}

static void ValidateRegistryVersion(
    ClrRuntime runtime,
    RetentionManifest manifest)
{
    const string registryTypeName =
        "Magicka.GcDiagnostics.RetentionState";
    List<long> values = new List<long>();
    try
    {
        foreach (ClrAppDomain appDomain in runtime.AppDomains)
        {
            foreach (ClrModule module in appDomain.Modules)
            {
                ClrType? registry = module.GetTypeByName(registryTypeName);
                if (registry is null)
                {
                    continue;
                }

                ClrStaticField? field =
                    registry.GetStaticFieldByName("RegistryVersion");
                if (field is not null && field.IsInitialized(appDomain))
                {
                    values.Add(Convert.ToInt64(
                        field.GetValue(appDomain),
                        CultureInfo.InvariantCulture));
                }
            }
        }
    }
    catch (Exception exception)
    {
        throw new ManifestChangedException(
            "Could not read the live retention-registry version from the"
            + " process snapshot: " + exception.Message,
            exception);
    }

    long[] distinctValues = values.Distinct().ToArray();
    if (distinctValues.Length == 0)
    {
        throw new ManifestChangedException(
            "The retention-registry version was not available in the"
            + " process snapshot.");
    }

    if (distinctValues.Length != 1)
    {
        throw new ManifestChangedException(
            "The process snapshot contains ambiguous retention-registry"
            + " versions: "
            + string.Join(
                ", ",
                distinctValues.Select(
                    value => value.ToString(
                        CultureInfo.InvariantCulture))));
    }

    if (distinctValues[0] != manifest.RegistryVersion)
    {
        throw new ManifestChangedException(
            "The manifest records registry version "
            + manifest.RegistryVersion.ToString(CultureInfo.InvariantCulture)
            + ", but the process snapshot contains version "
            + distinctValues[0].ToString(CultureInfo.InvariantCulture)
            + ". Use the latest manifest and retry.");
    }
}

static void AnalyzeMustCollect(
    ClrHeap heap,
    IReadOnlyList<ResolvedWatch> resolved,
    AnalyzerOptions options,
    Report report)
{
    ResolvedWatch[] targets = resolved
        .Where(item => item.Watch.Expectation == "MustCollect")
        .ToArray();
    report.Line("=== Objects that must be collectible ===");
    if (targets.Length == 0)
    {
        report.Line("None.");
        report.Line();
        return;
    }

    Dictionary<long, int> pathCount = targets.ToDictionary(
        item => item.Watch.Id,
        item => 0);
    HashSet<long> pathLimitReached = new HashSet<long>();
    bool rootSearchTimedOut = false;

    using CancellationTokenSource timeout = new CancellationTokenSource(
        TimeSpan.FromSeconds(options.TimeoutSeconds));
    try
    {
        IReadOnlyDictionary<ulong, IReadOnlyList<StaticRootInfo>> staticRoots =
            EnumerateNamedStaticRoots(heap, timeout.Token, report);
        GCRoot gcRoot = new GCRoot(heap);
        foreach (ResolvedWatch watched in targets)
        {
            foreach (GCRootPath rootPath in gcRoot.EnumerateGCRoots(
                         watched.Object.Address,
                         timeout.Token))
            {
                if (pathCount[watched.Watch.Id]
                    >= options.MaximumRootPaths)
                {
                    pathLimitReached.Add(watched.Watch.Id);
                    break;
                }

                ClrRoot root = rootPath.Root;
                List<ulong> path = rootPath.Path
                    .Select(value => value.Address)
                    .ToList();
                NormalizeRootPath(root, path);
                RootPathSelection? staticSelection =
                    RootPathAlgorithms.SelectPreferredStaticRoot(
                        path,
                        staticRoots);
                string rootKind = root.Kind.ToString();
                string rootName = string.Empty;
                if (staticSelection is not null)
                {
                    path = path.Skip(staticSelection.StartIndex).ToList();
                    rootKind = staticSelection.Kind;
                    rootName = staticSelection.Name;
                }

                pathCount[watched.Watch.Id]++;
                report.Line(
                    "LEAK #" + watched.Watch.Id + " "
                    + watched.Watch.TypeName + " [epoch "
                    + watched.Watch.Epoch.ToString(
                        CultureInfo.InvariantCulture)
                    + ", " + watched.Watch.Lifecycle + "]");
                report.Line(
                    "  root " + rootKind
                    + (rootName.Length == 0 ? string.Empty : " " + rootName)
                    + ": "
                    + FormatObject(heap.GetObject(path[0])));
                report.TelemetryFinding(
                    watched.Watch.Expectation,
                    watched.Watch.TypeName,
                    watched.Watch.Lifecycle,
                    rootKind,
                    BuildTelemetryPath(
                        heap,
                        path,
                        rootName,
                        includeRootArrayContext: staticSelection is null
                            && root.Kind == GCRootKind.Pinning));
                WritePath(heap, path, report, "  ");
                report.Line();
            }
        }
    }
    catch (OperationCanceledException)
    {
        rootSearchTimedOut = true;
        report.TelemetryValue("truncated", "true");
        report.Line(
            "TRUNCATED ROOT SEARCH: reached the global "
            + options.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)
            + "-second timeout; remaining candidates were not classified.");
    }

    foreach (long watchId in pathLimitReached.OrderBy(id => id))
    {
        report.TelemetryValue("truncated", "true");
        report.Line(
            "TRUNCATED ROOT PATHS #" + watchId + ": reached the configured"
            + " maximum of "
            + options.MaximumRootPaths.ToString(CultureInfo.InvariantCulture)
            + " path(s).");
    }

    foreach (ResolvedWatch target in targets.Where(
                 target => pathCount[target.Watch.Id] == 0))
    {
        if (rootSearchTimedOut)
        {
            report.Line(
                "TRUNCATED ROOT SEARCH #" + target.Watch.Id + " "
                + target.Watch.TypeName
                + ": this candidate was not reached before the global"
                + " timeout.");
        }
        else
        {
            report.Line(
                "NO ROOT PATH #" + target.Watch.Id + " "
                + target.Watch.TypeName
                + " (the object may have died after the snapshot began, or"
                + " the CLR-2 DAC could not expose this root).");
        }
    }

    report.Line();
}

static IReadOnlyDictionary<ulong, IReadOnlyList<StaticRootInfo>>
    EnumerateNamedStaticRoots(
        ClrHeap heap,
        CancellationToken cancellationToken,
        Report report)
{
    Dictionary<ulong, List<StaticRootInfo>> roots = new();
    try
    {
        foreach (ClrRoot root in heap.EnumerateRoots(enumerateStatics: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((root.Kind != GCRootKind.StaticVar
                 && root.Kind != GCRootKind.ThreadStaticVar)
                || root.Object == 0)
            {
                continue;
            }

            string name = NormalizeStaticRootName(root.Name);
            if (name.Length == 0)
            {
                continue;
            }

            if (!roots.TryGetValue(root.Object, out List<StaticRootInfo>? values))
            {
                values = new List<StaticRootInfo>();
                roots.Add(root.Object, values);
            }

            if (!values.Any(value =>
                    value.Kind == root.Kind.ToString()
                    && value.Name == name))
            {
                values.Add(new StaticRootInfo(
                    root.Object,
                    root.Kind.ToString(),
                    name));
            }
        }
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception exception)
    {
        report.Line(
            "STATIC ROOT METADATA UNAVAILABLE: retaining handle-table root"
            + " paths (" + exception.GetType().Name + ").");
        return new Dictionary<ulong, IReadOnlyList<StaticRootInfo>>();
    }

    return roots.ToDictionary(
        item => item.Key,
        item => (IReadOnlyList<StaticRootInfo>)item.Value
            .OrderBy(value => value.Kind, StringComparer.Ordinal)
            .ThenBy(value => value.Name, StringComparer.Ordinal)
            .ToArray());
}

static string NormalizeStaticRootName(string? value)
{
    string name = (value ?? string.Empty).Trim();
    string[] prefixes = ["static var ", "thread static var "];
    foreach (string prefix in prefixes)
    {
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return name.Substring(prefix.Length).Trim();
        }
    }

    return name;
}

static void AnalyzeMustDetach(
    IReadOnlyList<ResolvedWatch> resolved,
    AnalyzerOptions options,
    Report report)
{
    ResolvedWatch[] owners = resolved
        .Where(item => item.Watch.Expectation == "MustDetach")
        .ToArray();
    Dictionary<ulong, ResolvedWatch> candidates = resolved
        .GroupBy(item => item.Object.Address)
        .ToDictionary(group => group.Key, group => group.First());

    report.Line("=== Pooled/deactivated entities that must detach ===");
    if (owners.Length == 0)
    {
        report.Line("None.");
        report.Line();
        return;
    }

    DetachTraversalBudget budget = new DetachTraversalBudget(
        options.MaximumDetachNodes,
        TimeSpan.FromSeconds(options.TimeoutSeconds));
    int fullyUnanalysedOwners = 0;
    int partiallyAnalysedOwners = 0;
    bool anyTruncation = false;

    for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
    {
        ResolvedWatch owner = owners[ownerIndex];
        if (budget.IsTimedOut)
        {
            anyTruncation = true;
            fullyUnanalysedOwners++;
            report.Line(
                "TRUNCATED #" + owner.Watch.Id + " "
                + owner.Watch.TypeName
                + ": global detach timeout expired before this owner was"
                + " analysed.");
            continue;
        }

        if (!budget.TryReserveNode())
        {
            anyTruncation = true;
            fullyUnanalysedOwners++;
            report.Line(
                "TRUNCATED #" + owner.Watch.Id + " "
                + owner.Watch.TypeName + ": global detach "
                + (budget.IsTimedOut
                    ? "timeout expired"
                    : "node budget was exhausted")
                + " before this owner was analysed.");
            continue;
        }

        OutboundAnalysis analysis = FindOutboundLeaks(
            owner,
            candidates,
            options.DetachDepth,
            options.MaximumDetachPaths,
            budget);

        bool writeOwner = analysis.Leaks.Count != 0
                          || analysis.IsTruncated
                          || analysis.UnreadableNodes != 0;
        if (writeOwner)
        {
            report.Line(
                (analysis.Leaks.Count == 0 ? "DETACH CHECK #" : "STALE EDGES #")
                + owner.Watch.Id + " "
                + owner.Watch.TypeName + " [epoch "
                + owner.Watch.Epoch.ToString(CultureInfo.InvariantCulture)
                + ", " + owner.Watch.Lifecycle + "]");
        }

        foreach (OutboundLeak leak in analysis.Leaks)
        {
            report.TelemetryFinding(
                owner.Watch.Expectation,
                owner.Watch.TypeName,
                owner.Watch.Lifecycle,
                "pool_owner",
                BuildDetachTelemetryPath(owner.Object, leak.Path));
            report.Line(
                "  reaches #" + leak.Target.Watch.Id + " "
                + leak.Target.Watch.TypeName + " ["
                + leak.Target.Watch.Expectation + ", epoch "
                + leak.Target.Watch.Epoch.ToString(CultureInfo.InvariantCulture)
                + "]");
            foreach (PathEdge edge in leak.Path)
            {
                report.Line(
                    "    " + FormatObject(edge.Source)
                    + " --" + edge.Label + "--> "
                    + FormatObject(edge.Target));
            }
        }

        if (analysis.DepthLimitReached)
        {
            report.Line(
                "  TRUNCATED: detach depth limit "
                + options.DetachDepth.ToString(CultureInfo.InvariantCulture)
                + " was reached.");
        }

        if (analysis.NodeLimitReached)
        {
            report.Line(
                "  TRUNCATED: global detach node budget "
                + options.MaximumDetachNodes.ToString(CultureInfo.InvariantCulture)
                + " was reached; direct references of every processed node"
                + " were still checked.");
        }

        if (analysis.PathLimitReached)
        {
            report.Line(
                "  TRUNCATED: detach path limit "
                + options.MaximumDetachPaths.ToString(CultureInfo.InvariantCulture)
                + " was reached; "
                + analysis.OmittedPaths.ToString(CultureInfo.InvariantCulture)
                + " additional directly observed path(s) were omitted.");
        }

        if (analysis.TimeoutReached)
        {
            report.Line(
                "  TRUNCATED: global detach timeout "
                + options.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)
                + " seconds was reached.");
        }

        if (analysis.UnreadableNodes != 0)
        {
            report.Line(
                "  UNREADABLE: ClrMD could not enumerate "
                + analysis.UnreadableNodes.ToString(CultureInfo.InvariantCulture)
                + " processed node(s).");
        }

        if (writeOwner)
        {
            report.Line();
        }

        if (analysis.IsTruncated)
        {
            report.TelemetryValue("truncated", "true");
            anyTruncation = true;
            partiallyAnalysedOwners++;
        }
    }

    if (anyTruncation)
    {
        report.Line(
            "TRUNCATED SUMMARY: fully unanalysed owners: "
            + fullyUnanalysedOwners.ToString(CultureInfo.InvariantCulture)
            + "; partially analysed owners: "
            + partiallyAnalysedOwners.ToString(CultureInfo.InvariantCulture)
            + "; global nodes reserved: "
            + budget.ReservedNodes.ToString(CultureInfo.InvariantCulture)
            + "/"
            + options.MaximumDetachNodes.ToString(CultureInfo.InvariantCulture)
            + "; elapsed seconds: "
            + budget.Elapsed.TotalSeconds.ToString(
                "0.###",
                CultureInfo.InvariantCulture)
            + ".");
    }

    report.Line();
}

static OutboundAnalysis FindOutboundLeaks(
    ResolvedWatch owner,
    IReadOnlyDictionary<ulong, ResolvedWatch> candidates,
    int maximumDepth,
    int maximumPaths,
    DetachTraversalBudget budget)
{
    OutboundAnalysis analysis = new OutboundAnalysis();
    Queue<TraversalNode> queue = new Queue<TraversalNode>();
    HashSet<ulong> visited = new HashSet<ulong>();
    queue.Enqueue(new TraversalNode(owner.Object, new List<PathEdge>(), 0));
    visited.Add(owner.Object.Address);

    while (queue.Count != 0)
    {
        if (budget.IsTimedOut)
        {
            analysis.TimeoutReached = true;
            break;
        }

        TraversalNode node = queue.Dequeue();
        bool stopAfterCurrentNode = false;

        try
        {
            foreach (ClrObjectReference reference in
                     node.Object.Type.EnumerateObjectReferencesWithFields(
                         node.Object.Address, carefully: true))
            {
                if (budget.IsTimedOut)
                {
                    analysis.TimeoutReached = true;
                    stopAfterCurrentNode = true;
                    break;
                }

                ClrObject child = reference.Object;
                if (child.IsNull || child.Type is null || child.Type.IsFree)
                {
                    continue;
                }

                bool isCandidate = candidates.TryGetValue(
                    child.Address,
                    out ResolvedWatch? target)
                    && target.Watch.Id != owner.Watch.Id
                    && target.Watch.Epoch <= owner.Watch.Epoch;

                List<PathEdge>? path = null;
                if (isCandidate)
                {
                    path = ExtendPath(node, child, reference);
                    if (analysis.Leaks.Count < maximumPaths)
                    {
                        analysis.Leaks.Add(new OutboundLeak(target!, path));
                    }
                    else
                    {
                        analysis.PathLimitReached = true;
                        analysis.OmittedPaths++;
                        stopAfterCurrentNode = true;
                    }
                }

                if (!visited.Add(child.Address))
                {
                    continue;
                }

                int childDepth = node.Depth + 1;
                if (childDepth >= maximumDepth)
                {
                    analysis.DepthLimitReached = true;
                    continue;
                }

                if (analysis.Leaks.Count >= maximumPaths)
                {
                    analysis.PathLimitReached = true;
                    stopAfterCurrentNode = true;
                    continue;
                }

                if (!budget.TryReserveNode())
                {
                    if (budget.IsTimedOut)
                    {
                        analysis.TimeoutReached = true;
                    }
                    else
                    {
                        analysis.NodeLimitReached = true;
                    }

                    continue;
                }

                path ??= ExtendPath(node, child, reference);
                queue.Enqueue(new TraversalNode(child, path, childDepth));
            }
        }
        catch
        {
            analysis.UnreadableNodes++;
        }

        if (stopAfterCurrentNode)
        {
            if (queue.Count != 0 && analysis.Leaks.Count >= maximumPaths)
            {
                analysis.PathLimitReached = true;
            }

            break;
        }
    }

    return analysis;
}

static List<PathEdge> ExtendPath(
    TraversalNode node,
    ClrObject child,
    ClrObjectReference reference)
{
    PathEdge edge = new PathEdge(
        node.Object,
        child,
        DescribeReference(node.Object, reference));
    return new List<PathEdge>(node.Path) { edge };
}

static void NormalizeRootPath(ClrRoot root, List<ulong> path)
{
    if (path.Count == 0)
    {
        path.Add(root.Object);
        return;
    }

    if (path[0] == root.Object)
    {
        return;
    }

    if (path[path.Count - 1] == root.Object)
    {
        path.Reverse();
        return;
    }

    path.Insert(0, root.Object);
}

static string BuildTelemetryPath(
    ClrHeap heap,
    IReadOnlyList<ulong> path,
    string rootName,
    bool includeRootArrayContext)
{
    StringBuilder builder = new StringBuilder();
    if (!string.IsNullOrEmpty(rootName))
    {
        builder.Append(rootName);
        builder.Append(" --> ");
    }

    int count = Math.Min(path.Count, 12);
    for (int index = 0; index < count; index++)
    {
        ClrObject current = heap.GetObject(path[index]);
        if (index != 0)
        {
            ClrObject previous = heap.GetObject(path[index - 1]);
            builder.Append(" --");
            builder.Append(TelemetryReferenceLabel(
                FindReferenceLabel(previous, current.Address)));
            builder.Append("--> ");
        }

        builder.Append(
            index == 0 && includeRootArrayContext
                ? TelemetryArrayContext(heap, current)
                : TelemetryTypeName(current));
    }

    if (path.Count > count)
    {
        builder.Append(" --truncated--> ...");
    }

    return builder.ToString();
}

static string TelemetryArrayContext(ClrHeap heap, ClrObject value)
{
    string typeName = TelemetryTypeName(value);
    if (!value.IsArray || value.Type is null)
    {
        return typeName;
    }

    try
    {
        int length = value.Type.GetArrayLength(value.Address);
        int inspected = Math.Min(length, 64);
        int nonNull = 0;
        Dictionary<string, int> types = new(StringComparer.Ordinal);
        for (int index = 0; index < inspected; index++)
        {
            ulong? address = TryGetArrayObjectAddress(value, index);
            if (!address.HasValue || address.Value == 0)
            {
                continue;
            }

            ClrObject element = heap.GetObject(address.Value);
            if (element.IsNull || element.Type is null || element.Type.IsFree)
            {
                continue;
            }

            nonNull++;
            string elementType = TelemetryTypeName(element);
            types.TryGetValue(elementType, out int count);
            types[elementType] = count + 1;
        }

        string histogram = string.Join(
            ",",
            types.OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Take(4)
                .Select(item => item.Key + " x" + item.Value.ToString(
                    CultureInfo.InvariantCulture)));
        return typeName + "[length="
               + length.ToString(CultureInfo.InvariantCulture)
               + ";inspected="
               + inspected.ToString(CultureInfo.InvariantCulture)
               + ";nonnull="
               + nonNull.ToString(CultureInfo.InvariantCulture)
               + (histogram.Length == 0 ? string.Empty : ";types=" + histogram)
               + "]";
    }
    catch
    {
        return typeName;
    }
}

static string BuildDetachTelemetryPath(
    ClrObject owner,
    IReadOnlyList<PathEdge> path)
{
    StringBuilder builder = new StringBuilder();
    builder.Append(TelemetryTypeName(owner));
    int count = Math.Min(path.Count, 12);
    for (int index = 0; index < count; index++)
    {
        PathEdge edge = path[index];
        builder.Append(" --");
        builder.Append(TelemetryReferenceLabel(edge.Label));
        builder.Append("--> ");
        builder.Append(TelemetryTypeName(edge.Target));
    }

    if (path.Count > count)
    {
        builder.Append(" --truncated--> ...");
    }

    return builder.ToString();
}

static string TelemetryTypeName(ClrObject value)
{
    return value.Type?.Name ?? "<unknown>";
}

static string TelemetryReferenceLabel(string value)
{
    return RootPathAlgorithms.NormalizeTelemetryReferenceLabel(value);
}

static void WritePath(
    ClrHeap heap,
    IReadOnlyList<ulong> path,
    Report report,
    string indent)
{
    for (int index = 0; index < path.Count; index++)
    {
        ClrObject current = heap.GetObject(path[index]);
        if (index == 0)
        {
            report.Line(indent + FormatObject(current));
            continue;
        }

        ClrObject previous = heap.GetObject(path[index - 1]);
        string label = FindReferenceLabel(previous, current.Address);
        report.Line(
            indent + "  --" + label + "--> " + FormatObject(current));
    }
}

static string FindReferenceLabel(ClrObject source, ulong targetAddress)
{
    try
    {
        foreach (ClrObjectReference reference in
                 source.Type.EnumerateObjectReferencesWithFields(
                     source.Address, carefully: true))
        {
            if (reference.Object.Address == targetAddress)
            {
                return DescribeReference(source, reference);
            }
        }
    }
    catch
    {
    }

    return "reference";
}

static string DescribeReference(
    ClrObject source,
    ClrObjectReference reference)
{
    ClrType? sourceType = source.Type;
    if (sourceType is null)
    {
        return "reference";
    }

    if (source.IsArray)
    {
        int index = FindArrayReferenceIndex(source, reference);
        return index >= 0
            ? "[" + index.ToString(CultureInfo.InvariantCulture) + "]"
            : "[element]";
    }

    if (sourceType.GetFieldForOffset(
            reference.FieldOffset,
            false,
            out ClrInstanceField? field,
            out int childFieldOffset)
        && field is not null)
    {
        return "." + field.Name;
    }

    return "offset 0x"
           + reference.FieldOffset.ToString("x", CultureInfo.InvariantCulture);
}

static int FindArrayReferenceIndex(
    ClrObject source,
    ClrObjectReference reference)
{
    try
    {
        int length = source.Type.GetArrayLength(source.Address);
        return RootPathAlgorithms.FindArrayElementIndex(
            length,
            reference.FieldOffset,
            maximumElements: 4096,
            elementOffset: index =>
            {
                try
                {
                    ulong address = source.Type.GetArrayElementAddress(
                        source.Address,
                        index);
                    ulong difference = address - source.Address;
                    return difference <= int.MaxValue
                        ? (int?)difference
                        : null;
                }
                catch
                {
                    return null;
                }
            },
            targetAddress: reference.Object.Address,
            elementValue: index => TryGetArrayObjectAddress(source, index));
    }
    catch
    {
        return -1;
    }
}

static ulong? TryGetArrayObjectAddress(ClrObject array, int index)
{
    try
    {
        object? value = array.Type.GetArrayElementValue(array.Address, index);
        return value switch
        {
            null => null,
            ulong unsigned => unsigned,
            long signed when signed >= 0 => (ulong)signed,
            uint unsigned32 => unsigned32,
            int signed32 when signed32 >= 0 => (ulong)signed32,
            UIntPtr unsignedPointer => unsignedPointer.ToUInt64(),
            IntPtr signedPointer when signedPointer.ToInt64() >= 0
                => (ulong)signedPointer.ToInt64(),
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
        };
    }
    catch
    {
        return null;
    }
}

static string FormatObject(ClrObject value)
{
    string typeName = value.Type?.Name ?? "<unknown>";
    return typeName + " @0x"
           + value.Address.ToString("x", CultureInfo.InvariantCulture);
}

static void PrintUsage()
{
    Console.Error.WriteLine(
        "Usage: Magicka.GcAnalyzer <retention-manifest.tsv>"
        + " [--output <report.txt>] [--telemetry-output <report.tsv>]"
        + " [--include-young]"
        + " [--timeout <seconds>] [--max-root-paths <count>]"
        + " [--detach-depth <count>] [--max-detach-nodes <count>]"
        + " [--max-detach-paths <count>]");
}

sealed class AnalyzerOptions
{
    public string ManifestPath { get; private set; } = string.Empty;
    public string OutputPath { get; private set; } = string.Empty;
    public string TelemetryOutputPath { get; private set; } = string.Empty;
    public bool IncludeYoung { get; private set; }
    public int MinimumAgeSeconds { get; private set; } = 5;
    public int TimeoutSeconds { get; private set; } = 45;
    public int MaximumRootPaths { get; private set; } = 3;
    public int DetachDepth { get; private set; } = 6;
    public int MaximumDetachNodes { get; private set; } = 10000;
    public int MaximumDetachPaths { get; private set; } = 5;

    public static AnalyzerOptions Parse(string[] arguments)
    {
        AnalyzerOptions options = new AnalyzerOptions();
        options.ManifestPath = Path.GetFullPath(arguments[0]);
        string baseDirectory = Path.GetDirectoryName(options.ManifestPath)
                               ?? Environment.CurrentDirectory;
        options.OutputPath = Path.Combine(
            baseDirectory,
            "retention-analysis-"
            + DateTime.UtcNow.ToString(
                "yyyyMMdd-HHmmss-fff",
                CultureInfo.InvariantCulture)
            + "-"
            + Guid.NewGuid().ToString("N").Substring(0, 8)
            + ".txt");

        for (int index = 1; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (argument == "--include-young")
            {
                options.IncludeYoung = true;
                continue;
            }

            if (index + 1 >= arguments.Length)
            {
                throw new ArgumentException("Missing value for " + argument);
            }

            string value = arguments[++index];
            switch (argument)
            {
                case "--output":
                    options.OutputPath = Path.GetFullPath(value);
                    break;
                case "--telemetry-output":
                    options.TelemetryOutputPath = Path.GetFullPath(value);
                    break;
                case "--timeout":
                    options.TimeoutSeconds = PositiveInt(value, argument);
                    break;
                case "--max-root-paths":
                    options.MaximumRootPaths = PositiveInt(value, argument);
                    break;
                case "--detach-depth":
                    options.DetachDepth = PositiveInt(value, argument);
                    break;
                case "--max-detach-nodes":
                    options.MaximumDetachNodes = PositiveInt(value, argument);
                    break;
                case "--max-detach-paths":
                    options.MaximumDetachPaths = PositiveInt(value, argument);
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + argument);
            }
        }

        if (string.Equals(
                options.ManifestPath,
                options.OutputPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The output path must not overwrite the retention manifest.");
        }


        if (!string.IsNullOrEmpty(options.TelemetryOutputPath)
            && (string.Equals(
                    options.ManifestPath,
                    options.TelemetryOutputPath,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    options.OutputPath,
                    options.TelemetryOutputPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The telemetry output path must be separate from other inputs and outputs.");
        }

        return options;
    }

    private static int PositiveInt(string value, string option)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed)
            || parsed <= 0)
        {
            throw new ArgumentException(
                option + " requires a positive integer.");
        }

        return parsed;
    }
}

sealed class ManifestDocument
{
    private ManifestDocument(
        string path,
        ManifestFingerprint fingerprint,
        RetentionManifest manifest)
    {
        Path = path;
        Fingerprint = fingerprint;
        Manifest = manifest;
    }

    public string Path { get; }
    public ManifestFingerprint Fingerprint { get; }
    public RetentionManifest Manifest { get; }

    public static ManifestDocument Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ManifestFingerprint fingerprint = ManifestFingerprint.FromBytes(bytes);
        string content = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        RetentionManifest manifest = RetentionManifest.Parse(content);
        return new ManifestDocument(path, fingerprint, manifest);
    }

    public void EnsureUnchanged(string phase)
    {
        ManifestFingerprint current;
        try
        {
            current = ManifestFingerprint.Capture(Path);
        }
        catch (Exception exception)
        {
            throw new ManifestChangedException(
                "The manifest could not be fingerprinted " + phase
                + ": " + exception.Message,
                exception);
        }

        if (current != Fingerprint)
        {
            throw new ManifestChangedException(
                "The manifest fingerprint changed " + phase
                + " (loaded " + Fingerprint + ", current " + current + ").");
        }
    }
}

sealed record ManifestFingerprint(long Length, string Sha256)
{
    public static ManifestFingerprint Capture(string path)
    {
        return FromBytes(File.ReadAllBytes(path));
    }

    public static ManifestFingerprint FromBytes(byte[] bytes)
    {
        string hash;
        using (SHA256 sha256 = SHA256.Create())
        {
            hash = BitConverter.ToString(sha256.ComputeHash(bytes))
                .Replace("-", string.Empty);
        }

        return new ManifestFingerprint(
            bytes.LongLength,
            hash);
    }

    public override string ToString()
    {
        return Length.ToString(CultureInfo.InvariantCulture)
               + " bytes/"
               + Sha256;
    }
}

sealed class RetentionManifest
{
    public int ProcessId { get; private set; }
    public long ProcessStartUtcTicks { get; private set; }
    public string ProcessPath { get; private set; } = string.Empty;
    public int Epoch { get; private set; }
    public int Checkpoint { get; private set; }
    public long RegistryVersion { get; private set; }
    public List<WatchRecord> Watches { get; } = new List<WatchRecord>();

    public static RetentionManifest Parse(string content)
    {
        RetentionManifest manifest = new RetentionManifest();
        bool isVersion2 = false;
        using StringReader reader = new StringReader(content);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            if (rawLine == "# magicka-gc-retention-v2")
            {
                isVersion2 = true;
                continue;
            }

            if (rawLine.StartsWith("# ", StringComparison.Ordinal))
            {
                string[] header = rawLine.Substring(2).Split(
                    new[] { '\t' },
                    2);
                if (header.Length >= 2)
                {
                    if (header[0] == "pid")
                    {
                        manifest.ProcessId = ParseInt(header[1], "pid");
                    }
                    else if (header[0] == "process-start-utc-ticks")
                    {
                        manifest.ProcessStartUtcTicks = ParseLong(
                            header[1],
                            "process-start-utc-ticks");
                    }
                    else if (header[0] == "process-path")
                    {
                        manifest.ProcessPath = header[1];
                    }
                    else if (header[0] == "epoch")
                    {
                        manifest.Epoch = ParseInt(header[1], "epoch");
                    }
                    else if (header[0] == "checkpoint")
                    {
                        manifest.Checkpoint = ParseInt(header[1], "checkpoint");
                    }
                    else if (header[0] == "registry-version")
                    {
                        manifest.RegistryVersion = ParseLong(
                            header[1],
                            "registry-version");
                    }
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(rawLine)
                || rawLine.StartsWith("id\t", StringComparison.Ordinal))
            {
                continue;
            }

            string[] columns = rawLine.Split('\t');
            if (columns.Length != 10)
            {
                throw new InvalidDataException(
                    "Expected 10 tab-separated columns, found "
                    + columns.Length.ToString(CultureInfo.InvariantCulture)
                    + ".");
            }

            string expectation = columns[2];
            if (expectation != "MustCollect" && expectation != "MustDetach")
            {
                throw new InvalidDataException(
                    "Invalid expectation: " + expectation);
            }

            manifest.Watches.Add(
                new WatchRecord(
                    ParseLong(columns[0], "id"),
                    ParseHex(columns[1], "handle"),
                    expectation,
                    ParseInt(columns[3], "epoch"),
                    columns[4],
                    columns[5],
                    ParseLong(columns[6], "created_utc_ticks"),
                    ParseLong(columns[7], "state_utc_ticks"),
                    ParseInt(columns[8], "gen2_at_state"),
                    ParseInt(columns[9], "current_gen2")));
        }

        if (!isVersion2)
        {
            throw new InvalidDataException(
                "Manifest version 2 is required for safe process validation.");
        }

        if (manifest.ProcessId <= 0)
        {
            throw new InvalidDataException("Manifest PID is missing or invalid.");
        }

        if (manifest.ProcessStartUtcTicks <= 0)
        {
            throw new InvalidDataException(
                "Manifest process start time is missing or invalid.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ProcessPath)
            || !Path.IsPathRooted(manifest.ProcessPath))
        {
            throw new InvalidDataException(
                "Manifest process path is missing or not absolute.");
        }

        if (manifest.RegistryVersion <= 0)
        {
            throw new InvalidDataException(
                "Manifest registry version is missing or invalid.");
        }

        return manifest;
    }

    private static int ParseInt(string value, string name)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed))
        {
            throw new InvalidDataException("Invalid " + name + ": " + value);
        }

        return parsed;
    }

    private static long ParseLong(string value, string name)
    {
        if (!long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed))
        {
            throw new InvalidDataException("Invalid " + name + ": " + value);
        }

        return parsed;
    }

    private static ulong ParseHex(string value, string name)
    {
        if (!ulong.TryParse(
                value,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ulong parsed))
        {
            throw new InvalidDataException("Invalid " + name + ": " + value);
        }

        return parsed;
    }
}

sealed class DetachTraversalBudget
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly TimeSpan _timeout;
    private readonly int _maximumNodes;

    public DetachTraversalBudget(int maximumNodes, TimeSpan timeout)
    {
        _maximumNodes = maximumNodes;
        _timeout = timeout;
    }

    public int ReservedNodes { get; private set; }
    public bool IsTimedOut => _stopwatch.Elapsed >= _timeout;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public bool TryReserveNode()
    {
        if (IsTimedOut || ReservedNodes >= _maximumNodes)
        {
            return false;
        }

        ReservedNodes++;
        return true;
    }
}

sealed class OutboundAnalysis
{
    public List<OutboundLeak> Leaks { get; } = new List<OutboundLeak>();
    public bool DepthLimitReached { get; set; }
    public bool NodeLimitReached { get; set; }
    public bool PathLimitReached { get; set; }
    public bool TimeoutReached { get; set; }
    public int OmittedPaths { get; set; }
    public int UnreadableNodes { get; set; }

    public bool IsTruncated =>
        DepthLimitReached
        || NodeLimitReached
        || PathLimitReached
        || TimeoutReached;
}

sealed record WatchRecord(
    long Id,
    ulong HandleAddress,
    string Expectation,
    int Epoch,
    string TypeName,
    string Lifecycle,
    long CreatedUtcTicks,
    long StateUtcTicks,
    int Gen2AtState,
    int CurrentGen2);

sealed record ResolvedWatch(
    WatchRecord Watch,
    ClrObject Object);

sealed record TargetProcessInfo(
    string ProcessName,
    string ProcessPath);

sealed record PathEdge(
    ClrObject Source,
    ClrObject Target,
    string Label);

sealed record TraversalNode(
    ClrObject Object,
    List<PathEdge> Path,
    int Depth);

sealed record OutboundLeak(
    ResolvedWatch Target,
    List<PathEdge> Path);

sealed class ManifestChangedException : Exception
{
    public ManifestChangedException(string message)
        : base(message)
    {
    }

    public ManifestChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

sealed class TargetProcessException : Exception
{
    public TargetProcessException(string message)
        : base(message)
    {
    }

    public TargetProcessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

sealed class Report : IDisposable
{
    private const int ConsoleLineLimit = 200;
    private const int TelemetryFindingLimit = 8;
    private readonly StreamWriter _writer;
    private readonly string _path;
    private readonly string? _telemetryPath;
    private readonly Dictionary<string, string> _telemetryValues = new();
    private readonly List<string> _telemetryFindings = new();
    private int _consoleLines;
    private int _suppressedConsoleLines;
    private int _telemetryFindingCount;
    private bool _disposed;

    public Report(string path, string telemetryPath)
    {
        _path = Path.GetFullPath(path);
        _telemetryPath = string.IsNullOrEmpty(telemetryPath)
            ? null
            : Path.GetFullPath(telemetryPath);
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(
            _path,
            false,
            new UTF8Encoding(false));
        _writer.AutoFlush = true;
        _telemetryValues["status"] = "partial";
        _telemetryValues["resolved_count"] = "0";
        _telemetryValues["truncated"] = "false";
    }

    public void Line(string value = "")
    {
        _writer.WriteLine(value);
        if (_consoleLines < ConsoleLineLimit)
        {
            Console.WriteLine(value);
            _consoleLines++;
        }
        else
        {
            _suppressedConsoleLines++;
        }
    }

    public void TelemetryValue(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _telemetryValues[TelemetryText(key, 80)] = TelemetryText(value, 500);
    }

    public void TelemetryFinding(
        string expectation,
        string typeName,
        string lifecycle,
        string rootKind,
        string path)
    {
        _telemetryFindingCount++;
        if (_telemetryFindings.Count >= TelemetryFindingLimit)
        {
            _telemetryValues["truncated"] = "true";
            return;
        }

        _telemetryFindings.Add(
            TelemetryText(expectation, 80) + "\t"
            + TelemetryText(typeName, 300) + "\t"
            + TelemetryText(lifecycle, 300) + "\t"
            + TelemetryText(rootKind, 80) + "\t"
            + TelemetryText(path, 1200));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Dispose();
        WriteTelemetry();
        if (_suppressedConsoleLines != 0)
        {
            Console.WriteLine(
                "Console output truncated; "
                + _suppressedConsoleLines.ToString(CultureInfo.InvariantCulture)
                + " additional report line(s) were written only to disk.");
        }

        Console.WriteLine("Full report: " + _path);
    }

    private void WriteTelemetry()
    {
        if (string.IsNullOrEmpty(_telemetryPath))
        {
            return;
        }

        string temporaryPath = _telemetryPath + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(_telemetryPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (StreamWriter writer = new StreamWriter(
                       temporaryPath,
                       false,
                       new UTF8Encoding(false)))
            {
                writer.WriteLine("schema\tgc-retention-telemetry-v1");
                _telemetryValues["finding_count"] =
                    _telemetryFindingCount.ToString(CultureInfo.InvariantCulture);
                foreach (KeyValuePair<string, string> value in
                         _telemetryValues.OrderBy(item => item.Key))
                {
                    writer.Write(value.Key);
                    writer.Write('\t');
                    writer.WriteLine(value.Value);
                }

                foreach (string finding in _telemetryFindings)
                {
                    writer.Write("finding\t");
                    writer.WriteLine(finding);
                }
            }

            if (File.Exists(_telemetryPath))
            {
                File.Delete(_telemetryPath);
            }

            File.Move(temporaryPath, _telemetryPath);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private static string TelemetryText(string? value, int limit)
    {
        string sanitized = (value ?? string.Empty)
            .Replace('\t', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return sanitized.Length <= limit
            ? sanitized
            : sanitized.Substring(0, limit);
    }
}
