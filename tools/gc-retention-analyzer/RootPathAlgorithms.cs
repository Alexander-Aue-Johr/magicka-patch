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
    public static string ShortenManagedTypeName(string? value)
    {
        if (value is null || value.Length == 0)
        {
            return "<unknown>";
        }

        return ShortenTypeExpression(value);
    }

    public static string ShortenLifecycleName(
        string? lifecycle,
        IReadOnlyList<string> declaringTypeNames)
    {
        if (lifecycle is null || lifecycle.Length == 0)
        {
            return string.Empty;
        }

        string? declaringType = declaringTypeNames
            .Where(typeName => !string.IsNullOrEmpty(typeName)
                && (string.Equals(
                        lifecycle,
                        typeName,
                        StringComparison.Ordinal)
                    || lifecycle.StartsWith(
                        typeName + ".",
                        StringComparison.Ordinal)))
            .OrderByDescending(typeName => typeName.Length)
            .FirstOrDefault();
        if (declaringType is null)
        {
            return lifecycle;
        }

        return ShortenManagedTypeName(declaringType)
               + lifecycle.Substring(declaringType.Length);
    }

    public static string ShortenStaticMemberName(string? value)
    {
        if (value is null || value.Length == 0)
        {
            return string.Empty;
        }

        int separator = value.LastIndexOf('.');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return value;
        }

        return ShortenManagedTypeName(value.Substring(0, separator))
               + value.Substring(separator);
    }

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

    private static string ShortenTypeExpression(string value)
    {
        int genericStart = value.IndexOf('<');
        if (genericStart < 0)
        {
            return ShortenSimpleTypeName(value);
        }

        int genericEnd = FindMatchingGenericEnd(value, genericStart);
        if (genericEnd < 0)
        {
            return ShortenSimpleTypeName(value);
        }

        string genericArguments = value.Substring(
            genericStart + 1,
            genericEnd - genericStart - 1);
        return ShortenSimpleTypeName(value.Substring(0, genericStart))
               + "<"
               + string.Join(
                   ",",
                   SplitGenericArguments(genericArguments)
                       .Select(argument => ShortenTypeExpression(argument.Trim())))
               + ">"
               + value.Substring(genericEnd + 1);
    }

    private static string ShortenSimpleTypeName(string value)
    {
        int separator = value.LastIndexOf('.');
        string shortened = separator < 0
            ? value
            : value.Substring(separator + 1);
        int arity = shortened.IndexOf('`');
        return arity < 0 ? shortened : shortened.Substring(0, arity);
    }

    private static int FindMatchingGenericEnd(string value, int start)
    {
        int depth = 0;
        for (int index = start; index < value.Length; index++)
        {
            if (value[index] == '<')
            {
                depth++;
            }
            else if (value[index] == '>' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitGenericArguments(string value)
    {
        int angleDepth = 0;
        int squareDepth = 0;
        int start = 0;
        for (int index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    angleDepth--;
                    break;
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    squareDepth--;
                    break;
                case ',' when angleDepth == 0 && squareDepth == 0:
                    yield return value.Substring(start, index - start);
                    start = index + 1;
                    break;
            }
        }

        yield return value.Substring(start);
    }
}
