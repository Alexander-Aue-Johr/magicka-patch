internal sealed class StaticRootInfo
{
    public StaticRootInfo(ulong address, string kind, string name)
    {
        Address = address;
        Kind = kind ?? string.Empty;
        Name = name ?? string.Empty;
    }

    public ulong Address { get; }

    public string Kind { get; }

    public string Name { get; }
}

internal sealed class RootPathSelection
{
    public RootPathSelection(int startIndex, string kind, string name)
    {
        StartIndex = startIndex;
        Kind = kind;
        Name = name;
    }

    public int StartIndex { get; }

    public string Kind { get; }

    public string Name { get; }
}

internal static class RootPathAlgorithms
{
    public static string NormalizeTelemetryReferenceLabel(string? value)
    {
        if (value is null || value.Length == 0)
        {
            return "reference";
        }

        if (value == "[element]"
            || value.StartsWith("offset ", StringComparison.Ordinal))
        {
            return "element";
        }

        return value;
    }

    public static RootPathSelection? SelectPreferredStaticRoot(
        IReadOnlyList<ulong> path,
        IReadOnlyDictionary<ulong, IReadOnlyList<StaticRootInfo>> staticRoots)
    {
        for (int index = 0; index < path.Count; index++)
        {
            if (!staticRoots.TryGetValue(
                    path[index],
                    out IReadOnlyList<StaticRootInfo>? candidates))
            {
                continue;
            }

            StaticRootInfo? selected = candidates
                .Where(candidate => !string.IsNullOrEmpty(candidate.Name))
                .OrderBy(candidate => candidate.Kind, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (selected is not null)
            {
                return new RootPathSelection(
                    index,
                    selected.Kind,
                    selected.Name);
            }
        }

        return null;
    }

    public static int FindArrayElementIndex(
        int length,
        int fieldOffset,
        int maximumElements,
        Func<int, int?> elementOffset,
        ulong targetAddress,
        Func<int, ulong?> elementValue)
    {
        int inspected = Math.Min(Math.Max(length, 0), Math.Max(maximumElements, 0));
        for (int index = 0; index < inspected; index++)
        {
            int? offset = elementOffset(index);
            if (offset.HasValue && offset.Value == fieldOffset)
            {
                return index;
            }
        }

        for (int index = 0; index < inspected; index++)
        {
            ulong? value = elementValue(index);
            if (value.HasValue && value.Value == targetAddress)
            {
                return index;
            }
        }

        return -1;
    }
}
