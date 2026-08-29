using Magicka.GcDiagnostics;
using System.Reflection;
using System.Text;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            name + ": expected " + expected + ", got " + actual + ".");
    }
}

static T RetentionStateField<T>(string name)
{
    Type state = typeof(RetentionRegistry).Assembly.GetType(
        "Magicka.GcDiagnostics.RetentionState",
        throwOnError: true)!;
    FieldInfo field = state.GetField(
        name,
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException(
            "Missing RetentionState field " + name + ".");
    return (T)(field.GetValue(null)
        ?? throw new InvalidOperationException(
            "Null RetentionState field " + name + "."));
}

static void FinishRetentionAnalysis()
{
    Type state = typeof(RetentionRegistry).Assembly.GetType(
        "Magicka.GcDiagnostics.RetentionState",
        throwOnError: true)!;
    MethodInfo method = state.GetMethod(
        "FinishAnalysis",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "Missing RetentionState.FinishAnalysis.");
    method.Invoke(null, null);
}

string previousDiagnosticsDirectory = Environment.GetEnvironmentVariable(
    "MAGICKA_GC_DIAGNOSTICS_DIR") ?? string.Empty;
string recurringDiagnosticsDirectory = Path.Combine(
    Path.GetTempPath(),
    "magicka-gc-recurring-" + Guid.NewGuid().ToString("N"));
try
{
    Environment.SetEnvironmentVariable(
        "MAGICKA_GC_DIAGNOSTICS_DIR",
        recurringDiagnosticsDirectory);
    RetentionRegistry.Configure(true, string.Empty);

    object firstTarget = new();
    RetentionRegistry.BeginEpoch(firstTarget, "First.Begin");
    RetentionRegistry.MarkMustCollect(firstTarget, "First.Dispose");
    RetentionRegistry.Checkpoint("First.Checkpoint");
    Equal(true, RetentionStateField<bool>("TrackingClosed"),
        "first checkpoint closes tracking");

    FinishRetentionAnalysis();
    Equal(true, RetentionStateField<bool>("Enabled"),
        "analysis keeps diagnostics enabled");
    Equal(false, RetentionStateField<bool>("TrackingClosed"),
        "analysis reopens tracking");
    Equal(0, RetentionStateField<int>("AnalysisStarted"),
        "analysis resets its start guard");
    Equal(0, RetentionState.Watches.Count,
        "analysis clears the completed cycle");

    object secondTarget = new();
    RetentionRegistry.BeginEpoch(secondTarget, "Second.Begin");
    RetentionRegistry.MarkMustCollect(secondTarget, "Second.Dispose");
    Equal(1, RetentionState.Watches.Count,
        "second cycle accepts new watches");
    RetentionRegistry.Checkpoint("Second.Checkpoint");
    Equal(2, RetentionStateField<int>("CheckpointNumber"),
        "checkpoint numbers remain monotonic");
    Equal(true, RetentionStateField<bool>("TrackingClosed"),
        "second checkpoint starts another analysis cycle");
    FinishRetentionAnalysis();
    Equal(false, RetentionStateField<bool>("TrackingClosed"),
        "second analysis also reopens tracking");
}
finally
{
    Environment.SetEnvironmentVariable(
        "MAGICKA_GC_DIAGNOSTICS_DIR",
        string.IsNullOrEmpty(previousDiagnosticsDirectory)
            ? null
            : previousDiagnosticsDirectory);
    if (Directory.Exists(recurringDiagnosticsDirectory))
    {
        Directory.Delete(recurringDiagnosticsDirectory, recursive: true);
    }
}

Dictionary<ulong, IReadOnlyList<StaticRootInfo>> roots = new()
{
    [20] = new StaticRootInfo[]
    {
        new(20, "StaticVar", "Z.Root"),
        new(20, "StaticVar", "A.Root"),
    },
    [30] = new StaticRootInfo[]
    {
        new(30, "StaticVar", "Direct.Root"),
    },
};
RootPathSelection? selection = RootPathAlgorithms.SelectPreferredStaticRoot(
    new ulong[] { 10, 20, 30 },
    roots);
Equal(1, selection?.StartIndex, "static root start index");
Equal("StaticVar", selection?.Kind, "static root kind");
Equal("A.Root", selection?.Name, "deterministic static root name");
Equal<RootPathSelection?>(
    null,
    RootPathAlgorithms.SelectPreferredStaticRoot(
        new ulong[] { 1, 2, 3 },
        roots),
    "missing static root");
Equal(
    "[3]",
    RootPathAlgorithms.NormalizeTelemetryReferenceLabel("[3]"),
    "array index is preserved in telemetry");
Equal(
    "element",
    RootPathAlgorithms.NormalizeTelemetryReferenceLabel("[element]"),
    "unknown array element remains bounded");
Equal(
    "element",
    RootPathAlgorithms.NormalizeTelemetryReferenceLabel("offset 0x10"),
    "unknown offset remains bounded");

int[] offsets = [8, 12, 16, 20, 24];
ulong?[] duplicateValues = [1, 2, 3, 2, 5];
int exactIndex = RootPathAlgorithms.FindArrayElementIndex(
    offsets.Length,
    fieldOffset: 20,
    maximumElements: 64,
    elementOffset: index => offsets[index],
    targetAddress: 2,
    elementValue: index => duplicateValues[index]);
Equal(3, exactIndex, "field offset selects the exact duplicate");

int valueIndex = RootPathAlgorithms.FindArrayElementIndex(
    duplicateValues.Length,
    fieldOffset: 99,
    maximumElements: 64,
    elementOffset: _ => null,
    targetAddress: 2,
    elementValue: index => duplicateValues[index]);
Equal(1, valueIndex, "value fallback selects the first duplicate");

int unreadableIndex = RootPathAlgorithms.FindArrayElementIndex(
    length: 5,
    fieldOffset: 16,
    maximumElements: 64,
    elementOffset: index => index == 2 ? 16 : null,
    targetAddress: 4,
    elementValue: _ => null);
Equal(2, unreadableIndex, "offset works when values are unreadable");

int boundedIndex = RootPathAlgorithms.FindArrayElementIndex(
    length: 100,
    fieldOffset: -1,
    maximumElements: 5,
    elementOffset: _ => null,
    targetAddress: 99,
    elementValue: index => index == 10 ? 99UL : null);
Equal(-1, boundedIndex, "array scan stays bounded");

List<TelemetryFindingRecord> groupedFindings = new();
for (int index = 0; index < 12; index++)
{
    groupedFindings.Add(new TelemetryFindingRecord(
        "MustCollect",
        "CharacterTemplate",
        "CharacterTemplate.Dispose",
        "StaticVar",
        "CharacterTemplate.Cache" + index));
}

groupedFindings.Add(new TelemetryFindingRecord(
    "MustCollect",
    "Level",
    "Level.Dispose",
    "StaticVar",
    "Level.Cache"));
groupedFindings.Add(new TelemetryFindingRecord(
    "MustCollect",
    "GameScene",
    "GameScene.Dispose",
    "StaticVar",
    "GameScene.Cache"));
TelemetryFindingSelection fairSelection = FindingSelectionAlgorithms.Select(
    groupedFindings,
    maximumRows: 3,
    maximumCombinedCharacters: 3500);
Equal(3, fairSelection.FindingGroupCount, "finding group count");
Equal(3, fairSelection.SerializedFindingCount, "one row per group");
Equal(
    true,
    fairSelection.Findings.Any(finding => finding.TypeName == "Level"),
    "CharacterTemplate rows do not hide Level");
Equal(
    true,
    fairSelection.Findings.Any(finding => finding.TypeName == "GameScene"),
    "CharacterTemplate rows do not hide GameScene");
Equal(11, fairSelection.OmittedFindingCount, "omitted raw findings");

List<TelemetryFindingRecord> duplicates = Enumerable.Range(0, 12)
    .Select(_ => new TelemetryFindingRecord(
        "MustCollect",
        "CharacterTemplate",
        "CharacterTemplate.Dispose",
        "StaticVar",
        "CharacterTemplate.Cache"))
    .ToList();
TelemetryFindingSelection duplicateSelection = FindingSelectionAlgorithms.Select(
    duplicates,
    maximumRows: 8,
    maximumCombinedCharacters: 3500);
Equal(1, duplicateSelection.SerializedFindingCount, "identical path deduplication");
Equal(12, duplicateSelection.Findings[0].OccurrenceCount, "occurrence count");
Equal(0, duplicateSelection.OmittedFindingCount, "deduplicated rows represent all findings");

TelemetryFindingSelection reverseSelection = FindingSelectionAlgorithms.Select(
    groupedFindings.AsEnumerable().Reverse(),
    maximumRows: 3,
    maximumCombinedCharacters: 3500);
Equal(
    string.Join("\n", fairSelection.Findings.Select(finding => finding.SerializedValue)),
    string.Join("\n", reverseSelection.Findings.Select(finding => finding.SerializedValue)),
    "finding selection is independent of insertion order");

TelemetryFindingSelection boundedFindingSelection =
    FindingSelectionAlgorithms.Select(
        new[]
        {
            new TelemetryFindingRecord("A", "A", "A", "A", "short"),
            new TelemetryFindingRecord("A", "A", "A", "B", "second"),
            new TelemetryFindingRecord("B", "B", "B", "B", new string('x', 80)),
        },
        maximumRows: 8,
        maximumCombinedCharacters: 30);
Equal(1, boundedFindingSelection.SerializedFindingCount, "finding byte boundary");
Equal(
    true,
    boundedFindingSelection.Findings[0].DisplayValue.Length <= 30,
    "serialized finding is complete");
Equal(2, boundedFindingSelection.OmittedFindingCount, "later rounds wait for every group");

StringBuilder runtimeFindings = new();
string repeatedFinding = "MustCollect\tCharacterTemplate\tDispose\tStaticVar\tCache.Path\t12";
Equal(
    true,
    AnalyzerFindingTelemetry.TryAppendFinding(
        runtimeFindings,
        repeatedFinding,
        characterLimit: 3500,
        out int runtimeOccurrenceCount),
    "runtime appends a complete finding");
Equal(12, runtimeOccurrenceCount, "runtime occurrence parsing");
Equal(
    true,
    runtimeFindings.ToString().EndsWith(" x12", StringComparison.Ordinal),
    "runtime occurrence display");
string completeFirstFinding = runtimeFindings.ToString();
Equal(
    false,
    AnalyzerFindingTelemetry.TryAppendFinding(
        runtimeFindings,
        "MustCollect\tLevel\tDispose\tStaticVar\tLevel.Path\t1",
        characterLimit: completeFirstFinding.Length + 2,
        out int omittedOccurrenceCount),
    "runtime rejects an entire row at its boundary");
Equal(1, omittedOccurrenceCount, "runtime omitted occurrence count");
Equal(
    completeFirstFinding,
    runtimeFindings.ToString(),
    "runtime does not cut an existing finding");

Console.WriteLine("GC analyzer tests passed.");
