static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            name + ": expected " + expected + ", got " + actual + ".");
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

Console.WriteLine("GC analyzer root-path tests passed.");
