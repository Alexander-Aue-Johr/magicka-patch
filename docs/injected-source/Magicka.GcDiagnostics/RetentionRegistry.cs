using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Magicka.GcDiagnostics
{
    public static class RetentionRegistry
    {
        private static int Enabled;

        public static void Configure(
            bool enabled,
            string analyzerPath)
        {
            if (!enabled)
            {
                return;
            }

            try
            {
                RetentionState.Configure(analyzerPath);
                Interlocked.Exchange(ref Enabled, 1);
            }
            catch
            {
            }
        }

        public static void BeginEpoch(object target, string lifecycle)
        {
            if (Enabled != 0)
            {
                RetentionState.BeginEpoch(target, lifecycle);
            }
        }

        public static void Register(object target, string lifecycle)
        {
            if (Enabled != 0)
            {
                RetentionState.Register(target, lifecycle);
            }
        }

        public static void MarkActive(object target, string lifecycle)
        {
            if (Enabled != 0)
            {
                RetentionState.MarkActive(target, lifecycle);
            }
        }

        public static void MarkResidentActive(object target, string lifecycle)
        {
            if (Enabled != 0)
            {
                RetentionState.MarkResidentActive(target, lifecycle);
            }
        }

        public static void MarkMustCollect(object target, string lifecycle)
        {
            if (Enabled != 0)
            {
                RetentionState.MarkMustCollect(target, lifecycle);
            }
        }

        public static void MarkDeactivated(object target, string lifecycle)
        {
            if (Enabled != 0)
            {
                RetentionState.MarkDeactivated(target, lifecycle);
            }
        }

        public static void MarkMustDetach(object target, string lifecycle)
        {
            if (Enabled != 0)
            {
                RetentionState.MarkMustDetach(target, lifecycle);
            }
        }

        public static void Checkpoint(string lifecycle)
        {
            if (Enabled != 0)
            {
                RetentionState.Checkpoint(lifecycle);
            }
        }
    }

    public sealed class RetentionWatch
    {
        public long Id;
        public IntPtr HandleAddress;
        public int IdentityHash;
        public int Expectation;
        public bool IsResidentPool;
        public int Epoch;
        public string TypeName;
        public string LastLifecycle;
        public long CreatedUtcTicks;
        public long StateChangedUtcTicks;
        public int Gen2AtStateChange;
        public int LastObservedGen2;
        public int LastLoggedGen2;
    }

    internal static class RetentionState
    {
        private const int MaxWatches = 8192;
        private const int AnalyzerFindingTextLimit = 3500;

        public const int Active = 0;
        public const int MustCollect = 1;
        public const int MustDetach = 2;
        public const int Deactivated = 3;

        public static readonly List<RetentionWatch> Watches =
            new List<RetentionWatch>();

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, List<RetentionWatch>> ByIdentity =
            new Dictionary<int, List<RetentionWatch>>();
        private static readonly List<string> PendingLogLines =
            new List<string>();
        private static readonly List<IntPtr> PendingFreeHandles =
            new List<IntPtr>();
        private static readonly List<string> ObsoleteManifestPaths =
            new List<string>();
        private static readonly Dictionary<string, HashSet<IntPtr>>
            ManifestHandlesByPath =
                new Dictionary<string, HashSet<IntPtr>>(
                    StringComparer.OrdinalIgnoreCase);
        private static bool Enabled;
        private static string AnalyzerPath;
        private static readonly int ProcessId = GetProcessId();
        private static readonly long ProcessStartUtcTicks =
            GetProcessStartUtcTicks();
        private static readonly string ProcessPath = GetProcessPath();
        private static long NextId;
        private static long RegistryVersion;
        private static long NextSnapshotSequence;
        private static long LastPublishedVersion = -1;
        private static int LastPublishedGen2 = -1;
        private static string PublishedManifestPath;
        private static int CurrentEpoch;
        private static int CheckpointNumber;
        private static int SweepRunning;
        private static int DroppedLogLines;
        private static Timer SweepTimer;
        private static bool TrackingClosed;
        private static int AnalysisStarted;
        private static int SuppressedCheckpointCount;
        private static int DroppedWatchCount;
        private static string CheckpointLifecycle;
        private static long CheckpointUtcTicks;

        public static void Configure(string analyzerPath)
        {
            AnalyzerPath = analyzerPath ?? string.Empty;
            Enabled = true;
        }

        public static void BeginEpoch(object target, string lifecycle)
        {
            if (!Enabled || TrackingClosed || target == null)
            {
                return;
            }

            try
            {
                lock (Sync)
                {
                    Interlocked.Increment(ref RegistryVersion);
                    CurrentEpoch++;
                    RetentionWatch watch = GetOrCreateLocked(target, lifecycle);
                    if (watch != null)
                    {
                        MarkActiveLocked(watch, lifecycle);
                    }
                    QueueLogLocked(
                        "epoch\t" + CurrentEpoch.ToString(CultureInfo.InvariantCulture)
                        + "\t" + Sanitize(lifecycle));
                }
            }
            catch
            {
            }
        }

        public static void Register(object target, string lifecycle)
        {
            if (!Enabled || TrackingClosed || target == null)
            {
                return;
            }

            try
            {
                lock (Sync)
                {
                    GetOrCreateLocked(target, lifecycle);
                }
            }
            catch
            {
            }
        }

        public static void MarkActive(object target, string lifecycle)
        {
            MarkActiveInternal(target, lifecycle, false);
        }

        public static void MarkResidentActive(
            object target,
            string lifecycle)
        {
            MarkActiveInternal(target, lifecycle, true);
        }

        private static void MarkActiveInternal(
            object target,
            string lifecycle,
            bool residentPool)
        {
            if (!Enabled || TrackingClosed || target == null)
            {
                return;
            }

            try
            {
                lock (Sync)
                {
                    RetentionWatch watch = GetOrCreateLocked(target, lifecycle);
                    if (watch == null)
                    {
                        return;
                    }

                    if (residentPool)
                    {
                        Interlocked.Increment(ref RegistryVersion);
                        watch.IsResidentPool = true;
                    }

                    MarkActiveLocked(watch, lifecycle);
                }
            }
            catch
            {
            }
        }

        public static void MarkMustCollect(object target, string lifecycle)
        {
            MarkExpectation(target, lifecycle, MustCollect);
        }

        public static void MarkDeactivated(object target, string lifecycle)
        {
            MarkExpectation(target, lifecycle, Deactivated);
        }

        public static void MarkMustDetach(object target, string lifecycle)
        {
            MarkExpectation(target, lifecycle, MustDetach);
        }

        public static void Checkpoint(string lifecycle)
        {
            if (!Enabled)
            {
                return;
            }

            try
            {
                lock (Sync)
                {
                    if (TrackingClosed)
                    {
                        SuppressedCheckpointCount++;
                        return;
                    }

                    TrackingClosed = true;
                    CheckpointLifecycle = lifecycle ?? string.Empty;
                    CheckpointUtcTicks = DateTime.UtcNow.Ticks;
                    Interlocked.Increment(ref RegistryVersion);
                    CheckpointNumber++;
                    QueueLogLocked(
                        "checkpoint\t"
                        + CheckpointNumber.ToString(CultureInfo.InvariantCulture)
                        + "\tepoch\t"
                        + CurrentEpoch.ToString(CultureInfo.InvariantCulture)
                        + "\tgen2\t"
                        + GC.CollectionCount(GC.MaxGeneration).ToString(
                            CultureInfo.InvariantCulture)
                        + "\t"
                        + Sanitize(lifecycle));
                    EnsureTimerLocked();
                    SweepTimer.Change(
                        TimeSpan.FromSeconds(10.0),
                        TimeSpan.FromSeconds(10.0));
                }
            }
            catch
            {
            }
        }

        private static void MarkExpectation(
            object target,
            string lifecycle,
            int expectation)
        {
            if (!Enabled || TrackingClosed || target == null)
            {
                return;
            }

            try
            {
                lock (Sync)
                {
                    RetentionWatch watch = GetOrCreateLocked(target, lifecycle);
                    if (watch == null)
                    {
                        return;
                    }

                    if (expectation == Deactivated && watch.IsResidentPool)
                    {
                        expectation = MustDetach;
                    }

                    if (watch.Expectation == MustCollect
                        && expectation != MustCollect)
                    {
                        return;
                    }

                    if (watch.Expectation == MustDetach
                        && expectation == Deactivated)
                    {
                        return;
                    }

                    if (watch.Expectation == expectation)
                    {
                        string repeatedLifecycle = lifecycle ?? string.Empty;
                        if (watch.LastLifecycle != repeatedLifecycle)
                        {
                            Interlocked.Increment(ref RegistryVersion);
                            watch.LastLifecycle = repeatedLifecycle;
                        }
                        return;
                    }

                    Interlocked.Increment(ref RegistryVersion);
                    watch.Expectation = expectation;
                    watch.LastLifecycle = lifecycle ?? string.Empty;
                    watch.StateChangedUtcTicks = DateTime.UtcNow.Ticks;
                    watch.Gen2AtStateChange = GC.CollectionCount(GC.MaxGeneration);
                    watch.LastObservedGen2 = watch.Gen2AtStateChange;
                    watch.LastLoggedGen2 = watch.Gen2AtStateChange;
                    if (watch.Epoch == 0)
                    {
                        watch.Epoch = CurrentEpoch;
                    }
                }
            }
            catch
            {
            }
        }

        private static void MarkActiveLocked(
            RetentionWatch watch,
            string lifecycle)
        {
            Interlocked.Increment(ref RegistryVersion);
            watch.Expectation = Active;
            watch.Epoch = CurrentEpoch;
            watch.LastLifecycle = lifecycle ?? string.Empty;
            watch.StateChangedUtcTicks = DateTime.UtcNow.Ticks;
            watch.Gen2AtStateChange = GC.CollectionCount(GC.MaxGeneration);
            watch.LastObservedGen2 = watch.Gen2AtStateChange;
            watch.LastLoggedGen2 = watch.Gen2AtStateChange;
        }

        private static RetentionWatch GetOrCreateLocked(
            object target,
            string lifecycle)
        {
            int identityHash = RuntimeHelpers.GetHashCode(target);
            List<RetentionWatch> bucket;
            bool bucketExists =
                ByIdentity.TryGetValue(identityHash, out bucket);
            if (bucketExists)
            {
                for (int index = bucket.Count - 1; index >= 0; index--)
                {
                    RetentionWatch candidate = bucket[index];
                    object existingTarget = GetTarget(candidate);
                    bool matches = object.ReferenceEquals(existingTarget, target);
                    existingTarget = null;
                    if (matches)
                    {
                        return candidate;
                    }
                }
            }
            else
            {
                bucket = new List<RetentionWatch>();
            }

            if (Watches.Count >= MaxWatches)
            {
                if (DroppedWatchCount != int.MaxValue)
                {
                    DroppedWatchCount++;
                }

                return null;
            }

            RetentionWatch watch = new RetentionWatch();
            watch.Id = ++NextId;
            watch.IdentityHash = identityHash;
            watch.Expectation = Active;
            watch.Epoch = CurrentEpoch;
            watch.TypeName = target.GetType().FullName ?? target.GetType().Name;
            watch.LastLifecycle = lifecycle ?? string.Empty;
            watch.CreatedUtcTicks = DateTime.UtcNow.Ticks;
            watch.StateChangedUtcTicks = watch.CreatedUtcTicks;
            watch.Gen2AtStateChange = GC.CollectionCount(GC.MaxGeneration);
            watch.LastObservedGen2 = watch.Gen2AtStateChange;
            watch.LastLoggedGen2 = watch.Gen2AtStateChange;
            GCHandle handle = default(GCHandle);
            bool handleAllocated = false;
            bool bucketPublished = false;
            bool bucketEntryAdded = false;
            bool watchAdded = false;
            try
            {
                handle = GCHandle.Alloc(target, GCHandleType.Weak);
                handleAllocated = true;
                watch.HandleAddress = GCHandle.ToIntPtr(handle);
                Interlocked.Increment(ref RegistryVersion);
                if (!bucketExists)
                {
                    ByIdentity.Add(identityHash, bucket);
                    bucketPublished = true;
                }

                bucket.Add(watch);
                bucketEntryAdded = true;
                Watches.Add(watch);
                watchAdded = true;
                return watch;
            }
            catch
            {
                if (watchAdded)
                {
                    Watches.Remove(watch);
                }

                if (bucketEntryAdded)
                {
                    bucket.Remove(watch);
                }

                if (bucketPublished && bucket.Count == 0)
                {
                    ByIdentity.Remove(identityHash);
                }

                if (handleAllocated)
                {
                    try
                    {
                        if (handle.IsAllocated)
                        {
                            handle.Free();
                        }
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }

        private static object GetTarget(RetentionWatch watch)
        {
            if (watch == null || watch.HandleAddress == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                GCHandle handle = GCHandle.FromIntPtr(watch.HandleAddress);
                return handle.Target;
            }
            catch
            {
                return null;
            }
        }

        private static void SweepTimerTick(object state)
        {
            if (!Enabled || Interlocked.Exchange(ref SweepRunning, 1) != 0)
            {
                return;
            }

            try
            {
                string[] logLines;
                lock (Sync)
                {
                    SweepDeadLocked();
                    if (DroppedLogLines != 0 && PendingLogLines.Count < 256)
                    {
                        QueueLogLocked(
                            "log-lines-dropped\t"
                            + DroppedLogLines.ToString(CultureInfo.InvariantCulture));
                        DroppedLogLines = 0;
                    }

                    logLines = PendingLogLines.ToArray();
                    PendingLogLines.Clear();
                }

                bool published = WriteSnapshot();
                AppendLogBatch(logLines);
                if (published)
                {
                    ReleasePendingHandles();
                    TryRunAnalysis();
                }
                else
                {
                    ScheduleSweepRetry("snapshot-publish-returned-false", null);
                }
            }
            catch (Exception exception)
            {
                ScheduleSweepRetry("sweep-timer-failed", exception);
            }
            finally
            {
                FinishExpiredObservation();
                Interlocked.Exchange(ref SweepRunning, 0);
            }
        }

        private static void FinishExpiredObservation()
        {
            if (!TrackingClosed
                || CheckpointUtcTicks == 0L
                || Interlocked.CompareExchange(
                    ref AnalysisStarted,
                    0,
                    0) != 0
                || DateTime.UtcNow.Ticks - CheckpointUtcTicks
                    < TimeSpan.TicksPerMinute * 2L)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref AnalysisStarted, 1, 0) == 0)
            {
                FinishAnalysis();
            }
        }

        private static void SweepDeadLocked()
        {
            int currentGen2 = GC.CollectionCount(GC.MaxGeneration);
            int mustCollectSurvivors = 0;
            int mustDetachSurvivors = 0;
            long nowTicks = DateTime.UtcNow.Ticks;
            for (int index = Watches.Count - 1; index >= 0; index--)
            {
                RetentionWatch watch = Watches[index];
                object target = GetTarget(watch);
                bool alive = target != null;
                target = null;
                if (!alive)
                {
                    RemoveWatchLocked(index, watch);
                    continue;
                }

                watch.LastObservedGen2 = currentGen2;
                if (watch.Expectation != Active
                    && currentGen2 > watch.Gen2AtStateChange
                    && currentGen2 > watch.LastLoggedGen2
                    && nowTicks - watch.StateChangedUtcTicks
                        >= TimeSpan.TicksPerSecond * 5L)
                {
                    watch.LastLoggedGen2 = currentGen2;
                    if (watch.Expectation != MustDetach)
                    {
                        mustCollectSurvivors++;
                    }
                    else
                    {
                        mustDetachSurvivors++;
                    }
                }
            }

            if (mustCollectSurvivors != 0 || mustDetachSurvivors != 0)
            {
                QueueLogLocked(
                    "survived-summary\tMustCollect\t"
                    + mustCollectSurvivors.ToString(CultureInfo.InvariantCulture)
                    + "\tMustDetach\t"
                    + mustDetachSurvivors.ToString(CultureInfo.InvariantCulture)
                    + "\tgen2\t"
                    + currentGen2.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void RemoveWatchLocked(
            int watchIndex,
            RetentionWatch watch)
        {
            IntPtr handleAddress = watch.HandleAddress;
            bool protectedHandle = handleAddress != IntPtr.Zero
                && IsHandleProtectedLocked(handleAddress);
            if (protectedHandle)
            {
                try
                {
                    PendingFreeHandles.Add(handleAddress);
                }
                catch
                {
                    // Keep the watch and its handle intact so a later sweep can
                    // retry instead of losing ownership during memory pressure.
                    return;
                }
            }

            Interlocked.Increment(ref RegistryVersion);
            watch.HandleAddress = IntPtr.Zero;
            Watches.RemoveAt(watchIndex);
            List<RetentionWatch> bucket;
            if (ByIdentity.TryGetValue(watch.IdentityHash, out bucket))
            {
                bucket.Remove(watch);
                if (bucket.Count == 0)
                {
                    ByIdentity.Remove(watch.IdentityHash);
                }
            }

            if (handleAddress != IntPtr.Zero && !protectedHandle)
            {
                FreeHandle(handleAddress);
            }
        }

        private static void EnsureTimerLocked()
        {
            if (SweepTimer == null)
            {
                SweepTimer = new Timer(
                    SweepTimerTick,
                    null,
                    TimeSpan.FromSeconds(1.0),
                TimeSpan.FromSeconds(10.0));
            }
        }

        private static void TryRunAnalysis()
        {
            if (!Enabled || !TrackingClosed
                || Interlocked.CompareExchange(ref AnalysisStarted, 0, 0) != 0)
            {
                return;
            }

            string manifestPath;
            int candidateCount;
            string survivorSummary;
            lock (Sync)
            {
                candidateCount = BuildEligibleSurvivorSummaryLocked(
                    out survivorSummary);
                manifestPath = PublishedManifestPath;
            }

            if (candidateCount == 0 || string.IsNullOrEmpty(manifestPath))
            {
                return;
            }

            if (Interlocked.CompareExchange(ref AnalysisStarted, 1, 0) != 0)
            {
                return;
            }

            try
            {
                if (IsMonoRuntime())
                {
                    SendSurvivorTelemetry(
                        "weak_survivors_only",
                        "mono_heap_roots_unavailable",
                        candidateCount,
                        survivorSummary);
                    return;
                }

                if (string.IsNullOrEmpty(AnalyzerPath)
                    || !File.Exists(AnalyzerPath))
                {
                    SendSurvivorTelemetry(
                        "weak_survivors_only",
                        "analyzer_missing",
                        candidateCount,
                        survivorSummary);
                    return;
                }

                RunExternalAnalyzer(
                    manifestPath,
                    candidateCount,
                    survivorSummary);
            }
            catch (Exception exception)
            {
                SendSurvivorTelemetry(
                    "weak_survivors_only",
                    "analyzer_launch_failed_"
                        + SanitizeCategory(exception.GetType().Name),
                    candidateCount,
                    survivorSummary);
            }
            finally
            {
                FinishAnalysis();
            }
        }

        private static int BuildEligibleSurvivorSummaryLocked(
            out string summary)
        {
            int currentGen2 = GC.CollectionCount(GC.MaxGeneration);
            long nowTicks = DateTime.UtcNow.Ticks;
            Dictionary<string, int> groups = new Dictionary<string, int>();
            int candidates = 0;
            for (int index = 0; index < Watches.Count; index++)
            {
                RetentionWatch watch = Watches[index];
                if (watch.Expectation == Active
                    || watch.HandleAddress == IntPtr.Zero
                    || currentGen2 <= watch.Gen2AtStateChange
                    || nowTicks - watch.StateChangedUtcTicks
                        < TimeSpan.TicksPerSecond * 10L)
                {
                    continue;
                }

                object target = GetTarget(watch);
                bool alive = target != null;
                target = null;
                if (!alive)
                {
                    continue;
                }

                candidates++;
                string key = ExpectationName(watch.Expectation)
                    + ":" + Sanitize(watch.TypeName)
                    + "@" + Sanitize(watch.LastLifecycle);
                int count;
                groups.TryGetValue(key, out count);
                groups[key] = count + 1;
            }

            StringBuilder builder = new StringBuilder();
            int written = 0;
            foreach (KeyValuePair<string, int> group in groups)
            {
                if (written == 8 || builder.Length >= 1800)
                {
                    break;
                }

                if (builder.Length != 0)
                {
                    builder.Append("; ");
                }

                builder.Append(group.Key);
                builder.Append(" x");
                builder.Append(group.Value.ToString(CultureInfo.InvariantCulture));
                written++;
            }

            summary = builder.ToString();
            return candidates;
        }

        private static void RunExternalAnalyzer(
            string manifestPath,
            int candidateCount,
            string survivorSummary)
        {
            string directory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrEmpty(directory))
            {
                directory = GetOutputDirectory();
            }

            string suffix = Guid.NewGuid().ToString("N");
            string reportPath = Path.Combine(
                directory,
                "analysis-" + suffix + ".txt");
            string telemetryPath = Path.Combine(
                directory,
                "analysis-" + suffix + ".tsv");
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = AnalyzerPath;
                startInfo.Arguments = QuoteArgument(manifestPath)
                    + " --output " + QuoteArgument(reportPath)
                    + " --telemetry-output " + QuoteArgument(telemetryPath)
                    + " --timeout 20 --max-root-paths 1"
                    + " --detach-depth 6 --max-detach-nodes 5000"
                    + " --max-detach-paths 3";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null || !process.WaitForExit(55000))
                    {
                        try
                        {
                            if (process != null)
                            {
                                process.Kill();
                            }
                        }
                        catch
                        {
                        }

                        SendSurvivorTelemetry(
                            "weak_survivors_only",
                            "analyzer_timeout",
                            candidateCount,
                            survivorSummary);
                        return;
                    }

                    if (File.Exists(telemetryPath))
                    {
                        SendAnalyzerTelemetry(
                            telemetryPath,
                            process.ExitCode,
                            candidateCount,
                            survivorSummary);
                    }
                    else
                    {
                        SendSurvivorTelemetry(
                            "weak_survivors_only",
                            "analyzer_exit_"
                                + process.ExitCode.ToString(
                                    CultureInfo.InvariantCulture),
                            candidateCount,
                            survivorSummary);
                    }
                }
            }
            finally
            {
                TryDeleteFile(reportPath);
                TryDeleteFile(telemetryPath);
            }
        }

        private static void SendAnalyzerTelemetry(
            string telemetryPath,
            int exitCode,
            int candidateCount,
            string survivorSummary)
        {
            string[] lines = File.ReadAllLines(telemetryPath, Encoding.UTF8);
            Dictionary<string, string> properties = BaseTelemetryProperties(
                "clrmd_root_paths",
                "completed");
            properties["analyzer_exit_code"] = exitCode.ToString(
                CultureInfo.InvariantCulture);
            properties["candidate_count"] = candidateCount.ToString(
                CultureInfo.InvariantCulture);
            properties["survivor_groups"] = survivorSummary;
            StringBuilder findings = new StringBuilder();
            int serializedFindingCount = 0;
            long runtimeOmittedFindingCount = 0;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int separator = line.IndexOf('\t');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator);
                string value = line.Substring(separator + 1);
                if (key == "finding")
                {
                    int occurrenceCount;
                    if (AnalyzerFindingTelemetry.TryAppendFinding(
                            findings,
                            value,
                            AnalyzerFindingTextLimit,
                            out occurrenceCount))
                    {
                        serializedFindingCount++;
                    }
                    else
                    {
                        runtimeOmittedFindingCount += occurrenceCount;
                    }
                }
                else if (key == "resolved_count"
                    || key == "finding_count"
                    || key == "finding_group_count"
                    || key == "serialized_finding_count"
                    || key == "omitted_finding_count"
                    || key == "telemetry_truncated"
                    || key == "truncated"
                    || key == "status"
                    || key == "error_stage"
                    || key == "exception_type"
                    || key == "inner_exception_type"
                    || key == "exception_hresult")
                {
                    properties[key] = value;
                }
            }

            int analyzerOmittedFindingCount = 0;
            string analyzerOmittedValue;
            if (properties.TryGetValue(
                    "omitted_finding_count",
                    out analyzerOmittedValue))
            {
                int parsedCount;
                if (int.TryParse(
                        analyzerOmittedValue,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out parsedCount)
                    && parsedCount > 0)
                {
                    analyzerOmittedFindingCount = parsedCount;
                }
            }

            long totalOmittedFindingCount =
                (long)analyzerOmittedFindingCount
                + runtimeOmittedFindingCount;
            properties["serialized_finding_count"] =
                serializedFindingCount.ToString(CultureInfo.InvariantCulture);
            properties["omitted_finding_count"] =
                totalOmittedFindingCount.ToString(CultureInfo.InvariantCulture);
            properties["telemetry_truncated"] =
                totalOmittedFindingCount == 0 ? "false" : "true";
            properties["findings"] = findings.ToString();
            SendTelemetry(properties);
        }

        private static void SendSurvivorTelemetry(
            string mode,
            string status,
            int candidateCount,
            string survivorSummary)
        {
            Dictionary<string, string> properties = BaseTelemetryProperties(
                mode,
                status);
            properties["candidate_count"] = candidateCount.ToString(
                CultureInfo.InvariantCulture);
            properties["finding_count"] = "0";
            properties["finding_group_count"] = "0";
            properties["serialized_finding_count"] = "0";
            properties["omitted_finding_count"] = "0";
            properties["telemetry_truncated"] = "false";
            properties["survivor_groups"] = survivorSummary;
            SendTelemetry(properties);
        }

        private static Dictionary<string, string> BaseTelemetryProperties(
            string mode,
            string status)
        {
            Dictionary<string, string> properties =
                new Dictionary<string, string>();
            properties["analysis_mode"] = mode;
            properties["status"] = status;
            properties["runtime_family"] = IsMonoRuntime()
                ? "mono"
                : "microsoft_clr";
            properties["runtime_version"] = Sanitize(
                Environment.Version.ToString());
            properties["checkpoint"] = Sanitize(CheckpointLifecycle);
            properties["skipped_count"] = SuppressedCheckpointCount.ToString(
                CultureInfo.InvariantCulture);
            properties["tracked_count"] = Watches.Count.ToString(
                CultureInfo.InvariantCulture);
            properties["watch_limit"] = MaxWatches.ToString(
                CultureInfo.InvariantCulture);
            properties["dropped_watch_count"] = DroppedWatchCount.ToString(
                CultureInfo.InvariantCulture);
            return properties;
        }

        private static void SendTelemetry(
            Dictionary<string, string> properties)
        {
            try
            {
                Type telemetryType = Type.GetType(
                    "Magicka.CommunityPatch.PatchTelemetry, Magicka",
                    false);
                if (telemetryType == null)
                {
                    return;
                }

                MethodInfo sendAsync = telemetryType.GetMethod(
                    "SendAsync",
                    BindingFlags.Static
                        | BindingFlags.Public
                        | BindingFlags.NonPublic,
                    null,
                    new Type[]
                    {
                        typeof(string),
                        typeof(Dictionary<string, string>)
                    },
                    null);
                if (sendAsync != null)
                {
                    sendAsync.Invoke(
                        null,
                        new object[]
                        {
                            "magicka_patch_gc_retention",
                            properties
                        });
                }
            }
            catch
            {
            }
        }

        private static void FinishAnalysis()
        {
            HashSet<IntPtr> handles = new HashSet<IntPtr>();
            List<string> manifests = new List<string>();
            lock (Sync)
            {
                Enabled = false;
                if (SweepTimer != null)
                {
                    SweepTimer.Dispose();
                    SweepTimer = null;
                }

                for (int index = 0; index < Watches.Count; index++)
                {
                    if (Watches[index].HandleAddress != IntPtr.Zero)
                    {
                        handles.Add(Watches[index].HandleAddress);
                        Watches[index].HandleAddress = IntPtr.Zero;
                    }
                }

                for (int index = 0; index < PendingFreeHandles.Count; index++)
                {
                    handles.Add(PendingFreeHandles[index]);
                }

                foreach (string path in ManifestHandlesByPath.Keys)
                {
                    manifests.Add(path);
                }

                if (!string.IsNullOrEmpty(PublishedManifestPath)
                    && !manifests.Contains(PublishedManifestPath))
                {
                    manifests.Add(PublishedManifestPath);
                }

                Watches.Clear();
                ByIdentity.Clear();
                ManifestHandlesByPath.Clear();
                PendingFreeHandles.Clear();
                ObsoleteManifestPaths.Clear();
                PendingLogLines.Clear();
            }

            foreach (IntPtr handle in handles)
            {
                FreeHandle(handle);
            }

            for (int index = 0; index < manifests.Count; index++)
            {
                TryDeleteFile(manifests[index]);
            }


            TryDeleteFile(GetLogPath());
        }

        private static bool IsMonoRuntime()
        {
            try
            {
                return Type.GetType("Mono.Runtime") != null;
            }
            catch
            {
                return false;
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string SanitizeCategory(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            StringBuilder builder = new StringBuilder();
            for (int index = 0; index < value.Length && index < 80; index++)
            {
                char character = value[index];
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
                else
                {
                    builder.Append('_');
                }
            }

            return builder.ToString();
        }

        private static bool WriteSnapshot()
        {
            string temporaryPath = null;
            try
            {
                if (!DeleteObsoleteManifests())
                {
                    return false;
                }

                ReleasePendingHandles();
                int watchCount;
                int epoch;
                int checkpoint;
                int currentGen2;
                long snapshotVersion;
                long snapshotSequence;
                bool unchanged;
                lock (Sync)
                {
                    watchCount = Watches.Count;
                    epoch = CurrentEpoch;
                    checkpoint = CheckpointNumber;
                    currentGen2 = GC.CollectionCount(GC.MaxGeneration);
                    snapshotVersion = RegistryVersion;
                    unchanged = PublishedManifestPath != null
                        && LastPublishedVersion == snapshotVersion
                        && LastPublishedGen2 == currentGen2;
                    snapshotSequence = unchanged
                        ? NextSnapshotSequence
                        : ++NextSnapshotSequence;
                }

                if (unchanged)
                {
                    return DeleteObsoleteManifests();
                }

                string directory = GetOutputDirectory();
                Directory.CreateDirectory(directory);
                string path = Path.Combine(
                    directory,
                    "retention-"
                    + ProcessId.ToString(CultureInfo.InvariantCulture)
                    + "-"
                    + ProcessStartUtcTicks.ToString(CultureInfo.InvariantCulture)
                    + "-"
                    + snapshotSequence.ToString(
                        "D8",
                        CultureInfo.InvariantCulture)
                    + ".tsv");
                temporaryPath = path + ".tmp";

                HashSet<IntPtr> snapshotHandles = new HashSet<IntPtr>();
                using (StreamWriter writer = new StreamWriter(
                    temporaryPath,
                    false,
                    new UTF8Encoding(false)))
                {
                    writer.WriteLine("# magicka-gc-retention-v2");
                    writer.WriteLine(
                        "# pid\t"
                        + ProcessId.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine(
                        "# process-start-utc-ticks\t"
                        + ProcessStartUtcTicks.ToString(
                            CultureInfo.InvariantCulture));
                    writer.WriteLine("# process-path\t" + Sanitize(ProcessPath));
                    writer.WriteLine(
                        "# epoch\t"
                        + epoch.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine(
                        "# checkpoint\t"
                        + checkpoint.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine(
                        "# registry-version\t"
                        + snapshotVersion.ToString(
                            CultureInfo.InvariantCulture));
                    writer.WriteLine(
                        "# current-gen2\t"
                        + currentGen2.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine(
                        "id\thandle\texpectation\tepoch\ttype\tlifecycle"
                        + "\tcreated_utc_ticks\tstate_utc_ticks\tgen2_at_state"
                        + "\tcurrent_gen2");

                    const int batchSize = 256;
                    for (int offset = 0; offset < watchCount; offset += batchSize)
                    {
                        List<SnapshotRow> rows = new List<SnapshotRow>(batchSize);
                        lock (Sync)
                        {
                            int upper = Math.Min(
                                Math.Min(offset + batchSize, watchCount),
                                Watches.Count);
                            for (int index = offset; index < upper; index++)
                            {
                                RetentionWatch watch = Watches[index];
                                if (watch.Expectation == Active
                                    || watch.HandleAddress == IntPtr.Zero)
                                {
                                    continue;
                                }

                                rows.Add(new SnapshotRow(watch, currentGen2));
                                snapshotHandles.Add(watch.HandleAddress);
                            }
                        }

                        for (int index = 0; index < rows.Count; index++)
                        {
                            WriteSnapshotRow(writer, rows[index]);
                        }
                    }
                }

                IOException lastPublishError = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        bool changed;
                        lock (Sync)
                        {
                            changed = snapshotVersion != RegistryVersion;
                            if (!changed)
                            {
                                bool protectedNewManifest = false;
                                bool queuedOldManifest = false;
                                try
                                {
                                    ProtectManifestHandlesLocked(
                                        path,
                                        snapshotHandles);
                                    protectedNewManifest = true;
                                    if (!string.IsNullOrEmpty(
                                            PublishedManifestPath))
                                    {
                                        ObsoleteManifestPaths.Add(
                                            PublishedManifestPath);
                                        queuedOldManifest = true;
                                    }

                                    File.Move(temporaryPath, path);
                                }
                                catch
                                {
                                    if (queuedOldManifest)
                                    {
                                        ObsoleteManifestPaths.RemoveAt(
                                            ObsoleteManifestPaths.Count - 1);
                                    }

                                    if (protectedNewManifest)
                                    {
                                        UnprotectManifestHandlesLocked(path);
                                    }

                                    throw;
                                }

                                PublishedManifestPath = path;
                                LastPublishedVersion = snapshotVersion;
                                LastPublishedGen2 = currentGen2;
                            }
                        }

                        if (changed)
                        {
                            TryDeleteFile(temporaryPath);
                            return false;
                        }

                        return DeleteObsoleteManifests();
                    }
                    catch (IOException exception)
                    {
                        lastPublishError = exception;
                        if (attempt < 2)
                        {
                            Thread.Sleep(50 * (attempt + 1));
                        }
                    }
                }

                QueueBackgroundFailure(
                    "snapshot-publish-io-failed",
                    lastPublishError);
                TryDeleteFile(temporaryPath);
                return false;
            }
            catch (Exception exception)
            {
                QueueBackgroundFailure("snapshot-write-failed", exception);
                try
                {
                    if (!string.IsNullOrEmpty(temporaryPath)
                        && File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                }

                return false;
            }
        }

        private static void WriteSnapshotRow(
            StreamWriter writer,
            SnapshotRow row)
        {
            writer.Write(row.Id.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(FormatHandleAddress(row.HandleAddress));
            writer.Write('\t');
            writer.Write(ExpectationName(row.Expectation));
            writer.Write('\t');
            writer.Write(row.Epoch.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(Sanitize(row.TypeName));
            writer.Write('\t');
            writer.Write(Sanitize(row.LastLifecycle));
            writer.Write('\t');
            writer.Write(
                row.CreatedUtcTicks.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(
                row.StateChangedUtcTicks.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(
                row.Gen2AtStateChange.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.WriteLine(
                row.CurrentGen2.ToString(CultureInfo.InvariantCulture));
        }

        private static void ScheduleSweepRetry(
            string context,
            Exception exception)
        {
            QueueBackgroundFailure(context, exception);
            try
            {
                lock (Sync)
                {
                    if (SweepTimer != null)
                    {
                        SweepTimer.Change(
                            TimeSpan.FromSeconds(1.0),
                            TimeSpan.FromSeconds(10.0));
                    }
                }
            }
            catch
            {
            }
        }

        private static void QueueBackgroundFailure(
            string context,
            Exception exception)
        {
            try
            {
                lock (Sync)
                {
                    QueueLogLocked(
                        context + "\t"
                        + (exception == null
                            ? string.Empty
                            : Sanitize(
                                exception.GetType().FullName + ": "
                                + exception.Message)));
                }
            }
            catch
            {
            }
        }

        private static void QueueLogLocked(string line)
        {
            if (PendingLogLines.Count >= 256)
            {
                DroppedLogLines++;
                return;
            }

            PendingLogLines.Add(
                DateTime.UtcNow.ToString(
                    "o",
                    CultureInfo.InvariantCulture)
                + "\t"
                + line);
        }

        private static void AppendLogBatch(string[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return;
            }

            try
            {
                string directory = GetOutputDirectory();
                Directory.CreateDirectory(directory);
                string path = GetLogPath();
                using (StreamWriter writer = new StreamWriter(
                    path,
                    true,
                    new UTF8Encoding(false)))
                {
                    for (int index = 0; index < lines.Length; index++)
                    {
                        writer.WriteLine(lines[index]);
                    }
                }
            }
            catch
            {
            }
        }

        private static string GetLogPath()
        {
            return Path.Combine(
                GetOutputDirectory(),
                "retention-" + ProcessId.ToString(
                    CultureInfo.InvariantCulture)
                + "-"
                + ProcessStartUtcTicks.ToString(
                    CultureInfo.InvariantCulture)
                + ".log");
        }

        private static bool DeleteObsoleteManifests()
        {
            while (true)
            {
                string path;
                lock (Sync)
                {
                    if (ObsoleteManifestPaths.Count == 0)
                    {
                        return true;
                    }

                    path = ObsoleteManifestPaths[0];
                }

                if (!TryDeleteFile(path))
                {
                    QueueBackgroundFailure(
                        "obsolete-manifest-delete-failed",
                        null);
                    return false;
                }

                lock (Sync)
                {
                    int index = ObsoleteManifestPaths.IndexOf(path);
                    if (index >= 0)
                    {
                        ObsoleteManifestPaths.RemoveAt(index);
                    }

                    UnprotectManifestHandlesLocked(path);
                }
            }
        }

        private static void ProtectManifestHandlesLocked(
            string path,
            HashSet<IntPtr> handles)
        {
            ManifestHandlesByPath.Add(path, handles);
        }

        private static void UnprotectManifestHandlesLocked(string path)
        {
            ManifestHandlesByPath.Remove(path);
        }

        private static bool IsHandleProtectedLocked(IntPtr address)
        {
            foreach (HashSet<IntPtr> handles in ManifestHandlesByPath.Values)
            {
                if (handles.Contains(address))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ReleasePendingHandles()
        {
            List<IntPtr> releasable = new List<IntPtr>();
            lock (Sync)
            {
                int writeIndex = 0;
                for (int index = 0; index < PendingFreeHandles.Count; index++)
                {
                    IntPtr address = PendingFreeHandles[index];
                    if (IsHandleProtectedLocked(address))
                    {
                        PendingFreeHandles[writeIndex++] = address;
                    }
                    else
                    {
                        releasable.Add(address);
                    }
                }

                if (writeIndex < PendingFreeHandles.Count)
                {
                    PendingFreeHandles.RemoveRange(
                        writeIndex,
                        PendingFreeHandles.Count - writeIndex);
                }
            }

            for (int index = 0; index < releasable.Count; index++)
            {
                FreeHandle(releasable[index]);
            }
        }

        private static void FreeHandle(IntPtr address)
        {
            try
            {
                GCHandle handle = GCHandle.FromIntPtr(address);
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
            catch
            {
            }
        }

        private struct SnapshotRow
        {
            public readonly long Id;
            public readonly IntPtr HandleAddress;
            public readonly int Expectation;
            public readonly int Epoch;
            public readonly string TypeName;
            public readonly string LastLifecycle;
            public readonly long CreatedUtcTicks;
            public readonly long StateChangedUtcTicks;
            public readonly int Gen2AtStateChange;
            public readonly int CurrentGen2;

            public SnapshotRow(RetentionWatch watch, int currentGen2)
            {
                Id = watch.Id;
                HandleAddress = watch.HandleAddress;
                Expectation = watch.Expectation;
                Epoch = watch.Epoch;
                TypeName = watch.TypeName;
                LastLifecycle = watch.LastLifecycle;
                CreatedUtcTicks = watch.CreatedUtcTicks;
                StateChangedUtcTicks = watch.StateChangedUtcTicks;
                Gen2AtStateChange = watch.Gen2AtStateChange;
                CurrentGen2 = currentGen2;
            }
        }

        private static string GetOutputDirectory()
        {
            string configured = Environment.GetEnvironmentVariable(
                "MAGICKA_GC_DIAGNOSTICS_DIR");
            if (!string.IsNullOrEmpty(configured))
            {
                return Path.GetFullPath(configured);
            }

            string localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApplicationData))
            {
                return Path.Combine(
                    localApplicationData,
                    Path.Combine(
                        "MagickaCommunityPatch",
                        "gc-retention"));
            }

            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine("CommunityPatch", "gc-retention"));
        }

        private static string ExpectationName(int expectation)
        {
            return expectation == MustDetach ? "MustDetach" : "MustCollect";
        }

        private static string FormatHandleAddress(IntPtr address)
        {
            if (IntPtr.Size == 4)
            {
                return unchecked((uint)address.ToInt32()).ToString(
                    "x",
                    CultureInfo.InvariantCulture);
            }

            return unchecked((ulong)address.ToInt64()).ToString(
                "x",
                CultureInfo.InvariantCulture);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static bool IsEnabled()
        {
            try
            {
                string value = Environment.GetEnvironmentVariable(
                    "MAGICKA_GC_DIAGNOSTICS");
                return !string.Equals(
                    value,
                    "0",
                    StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        value,
                        "false",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        value,
                        "off",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        private static int GetProcessId()
        {
            try
            {
                return Process.GetCurrentProcess().Id;
            }
            catch
            {
                return 0;
            }
        }

        private static long GetProcessStartUtcTicks()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    return process.StartTime.ToUniversalTime().Ticks;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static string GetProcessPath()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    return Path.GetFullPath(process.MainModule.FileName);
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
