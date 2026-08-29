using System.Globalization;

internal sealed class TelemetryFindingRecord
{
    public TelemetryFindingRecord(
        string expectation,
        string typeName,
        string lifecycle,
        string rootKind,
        string path,
        int occurrenceCount = 1)
    {
        Expectation = expectation;
        TypeName = typeName;
        Lifecycle = lifecycle;
        RootKind = rootKind;
        Path = path;
        OccurrenceCount = occurrenceCount;
    }

    public string Expectation { get; }

    public string TypeName { get; }

    public string Lifecycle { get; }

    public string RootKind { get; }

    public string Path { get; }

    public int OccurrenceCount { get; }

    public string GroupIdentity => EncodeIdentity(
        Expectation,
        TypeName,
        Lifecycle);

    public string FindingIdentity => EncodeIdentity(
        Expectation,
        TypeName,
        Lifecycle,
        RootKind,
        Path);

    public string SerializedValue => FindingText + "\t"
        + OccurrenceCount.ToString(CultureInfo.InvariantCulture);

    public string DisplayValue => OccurrenceCount <= 1
        ? FindingText
        : FindingText + " x"
          + OccurrenceCount.ToString(CultureInfo.InvariantCulture);

    public TelemetryFindingRecord WithOccurrenceCount(int occurrenceCount)
    {
        return new TelemetryFindingRecord(
            Expectation,
            TypeName,
            Lifecycle,
            RootKind,
            Path,
            occurrenceCount);
    }

    private string FindingText => Expectation + "\t"
        + TypeName + "\t"
        + Lifecycle + "\t"
        + RootKind + "\t"
        + Path;

    private static string EncodeIdentity(params string[] values)
    {
        return string.Concat(values.Select(value =>
            value.Length.ToString(CultureInfo.InvariantCulture)
            + ":" + value));
    }
}

internal sealed class TelemetryFindingSelection
{
    public TelemetryFindingSelection(
        IReadOnlyList<TelemetryFindingRecord> findings,
        int findingGroupCount,
        int omittedFindingCount)
    {
        Findings = findings;
        FindingGroupCount = findingGroupCount;
        OmittedFindingCount = omittedFindingCount;
    }

    public IReadOnlyList<TelemetryFindingRecord> Findings { get; }

    public int FindingGroupCount { get; }

    public int SerializedFindingCount => Findings.Count;

    public int OmittedFindingCount { get; }
}

internal static class FindingSelectionAlgorithms
{
    public static TelemetryFindingSelection Select(
        IEnumerable<TelemetryFindingRecord> findings,
        int maximumRows,
        int maximumCombinedCharacters)
    {
        Dictionary<string, TelemetryFindingRecord> unique = new(
            StringComparer.Ordinal);
        foreach (TelemetryFindingRecord finding in findings)
        {
            if (unique.TryGetValue(
                    finding.FindingIdentity,
                    out TelemetryFindingRecord? existing))
            {
                unique[finding.FindingIdentity] =
                    existing.WithOccurrenceCount(
                        existing.OccurrenceCount + finding.OccurrenceCount);
            }
            else
            {
                unique.Add(finding.FindingIdentity, finding);
            }
        }

        List<IReadOnlyList<TelemetryFindingRecord>> groups = unique.Values
            .GroupBy(finding => finding.GroupIdentity, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<TelemetryFindingRecord>)group
                .OrderBy(finding => finding.RootKind, StringComparer.Ordinal)
                .ThenBy(finding => finding.Path, StringComparer.Ordinal)
                .ToArray())
            .ToList();

        int rowLimit = Math.Max(maximumRows, 0);
        int characterLimit = Math.Max(maximumCombinedCharacters, 0);
        int combinedCharacters = 0;
        int round = 0;
        HashSet<string> selectedIdentities = new(StringComparer.Ordinal);
        List<TelemetryFindingRecord> selected = new();
        while (selected.Count < rowLimit)
        {
            bool foundCandidate = false;
            bool omittedCandidate = false;
            foreach (IReadOnlyList<TelemetryFindingRecord> group in groups)
            {
                if (round >= group.Count)
                {
                    continue;
                }

                foundCandidate = true;
                TelemetryFindingRecord candidate = group[round];
                int candidateCharacters = candidate.DisplayValue.Length
                    + (selected.Count == 0 ? 0 : 3);
                if (combinedCharacters + candidateCharacters > characterLimit)
                {
                    omittedCandidate = true;
                    continue;
                }

                selected.Add(candidate);
                selectedIdentities.Add(candidate.FindingIdentity);
                combinedCharacters += candidateCharacters;
                if (selected.Count >= rowLimit)
                {
                    break;
                }
            }

            if (!foundCandidate)
            {
                break;
            }

            if (omittedCandidate)
            {
                break;
            }

            round++;
        }

        int omittedFindingCount = unique.Values
            .Where(finding => !selectedIdentities.Contains(
                finding.FindingIdentity))
            .Sum(finding => finding.OccurrenceCount);
        return new TelemetryFindingSelection(
            selected,
            groups.Count,
            omittedFindingCount);
    }
}
