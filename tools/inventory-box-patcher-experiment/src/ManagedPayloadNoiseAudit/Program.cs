using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

return ManagedPayloadNoiseAudit.Run(args);

internal static class ManagedPayloadNoiseAudit
{
    private static string repositoryRoot = "";
    private static string experimentRoot = "";
    private static string outputRoot = "";
    private static string commentStripperProject = "";
    private static string normalizerProject = "";
    private static string inventoryProject = "";

    public static int Run(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine(
                "usage: ManagedPayloadNoiseAudit <original-Magicka.exe> " +
                "<patched-Magicka.exe> <original-PolygonHead.dll> " +
                "<patched-PolygonHead.dll> <output-directory>");
            return 2;
        }

        repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        experimentRoot = Path.Combine(repositoryRoot,
            "tools", "inventory-box-patcher-experiment");
        commentStripperProject = Path.Combine(experimentRoot, "src",
            "SourceCommentStripper", "SourceCommentStripper.csproj");
        normalizerProject = Path.Combine(experimentRoot, "src",
            "SourceNoiseNormalizer", "SourceNoiseNormalizer.csproj");
        inventoryProject = Path.Combine(experimentRoot, "src",
            "AssemblySemanticInventory", "AssemblySemanticInventory.csproj");

        string originalMagicka = RequireFile(args[0], "Original Magicka.exe");
        string patchedMagicka = RequireFile(args[1], "Patched Magicka.exe");
        string originalPolygonHead = RequireFile(args[2], "Original PolygonHead.dll");
        string patchedPolygonHead = RequireFile(args[3], "Patched PolygonHead.dll");
        outputRoot = Path.GetFullPath(args[4]);
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new IOException("Refusing to overwrite existing audit path: " + outputRoot);
        Directory.CreateDirectory(outputRoot);

        WriteInputs(originalMagicka, patchedMagicka,
            originalPolygonHead, patchedPolygonHead);
        RunProcess("dotnet", new[] { "tool", "restore" }, experimentRoot);
        RunProcess("dotnet", new[] { "build", commentStripperProject,
            "--configuration", "Release" }, repositoryRoot);
        RunProcess("dotnet", new[] { "build", normalizerProject,
            "--configuration", "Release" }, repositoryRoot);
        RunProcess("dotnet", new[] { "build", inventoryProject,
            "--configuration", "Release" }, repositoryRoot);

        List<AuditRow> rows = new();
        rows.AddRange(AuditAssembly("magicka", originalMagicka, patchedMagicka));
        rows.AddRange(AuditAssembly("polygonhead", originalPolygonHead,
            patchedPolygonHead));
        WriteChecklist(rows);
        Console.WriteLine("checklist=" + Path.Combine(outputRoot,
            "DENOISE_CHECKLIST.md"));
        return 0;
    }

    private static IEnumerable<AuditRow> AuditAssembly(
        string name, string originalAssembly, string patchedAssembly)
    {
        string root = Path.Combine(outputRoot, name);
        string analysis = Path.Combine(root, "analysis");
        string originalInput = Path.Combine(analysis, "inputs", "original");
        string patchedInput = Path.Combine(analysis, "inputs", "current-patch");
        string originalReferences = Path.Combine(analysis, "references", "original");
        string patchedReferences = Path.Combine(analysis, "references", "current-patch");
        string originalSource = Path.Combine(analysis, "decompiled", "original");
        string patchedSource = Path.Combine(analysis, "decompiled", "current-patch");
        string rawDiffRoot = Path.Combine(analysis, "file-diffs");
        foreach (string directory in new[] { originalInput, patchedInput,
            originalReferences, patchedReferences, originalSource,
            patchedSource, rawDiffRoot })
            Directory.CreateDirectory(directory);

        string originalCopy = Path.Combine(originalInput,
            Path.GetFileName(originalAssembly));
        string patchedCopy = Path.Combine(patchedInput,
            Path.GetFileName(patchedAssembly));
        File.Copy(originalAssembly, originalCopy);
        File.Copy(patchedAssembly, patchedCopy);
        CopyReferences(Path.GetDirectoryName(originalAssembly)!, originalReferences);
        CopyReferences(Path.GetDirectoryName(originalAssembly)!, patchedReferences);
        CopyReferences(Path.GetDirectoryName(patchedAssembly)!, patchedReferences);

        Decompile(originalCopy, originalReferences, originalSource);
        Decompile(patchedCopy, patchedReferences, patchedSource);
        RunProcess("dotnet", new[] { "run", "--project", commentStripperProject,
            "--configuration", "Release", "--no-build", "--",
            originalSource, patchedSource }, repositoryRoot);

        Dictionary<string, string> originals = FilesByRelativePath(originalSource);
        Dictionary<string, string> patched = FilesByRelativePath(patchedSource);
        List<string> identical = new();
        foreach (string relative in originals.Keys.Intersect(patched.Keys,
            StringComparer.OrdinalIgnoreCase))
        {
            if (!FilesEqual(originals[relative], patched[relative]))
                continue;
            identical.Add(relative);
            File.Delete(originals[relative]);
            File.Delete(patched[relative]);
        }
        File.WriteAllLines(Path.Combine(analysis, "identical-files-removed.txt"),
            identical.OrderBy(value => value), new UTF8Encoding(false));

        originals = FilesByRelativePath(originalSource);
        patched = FilesByRelativePath(patchedSource);
        string[] modified = originals.Keys.Intersect(patched.Keys,
            StringComparer.OrdinalIgnoreCase).OrderBy(value => value,
            StringComparer.OrdinalIgnoreCase).ToArray();
        string[] added = patched.Keys.Except(originals.Keys,
            StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray();
        string[] removed = originals.Keys.Except(patched.Keys,
            StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray();
        File.WriteAllLines(Path.Combine(analysis, "added-files-excluded.txt"),
            added, new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(analysis, "removed-files.txt"),
            removed, new UTF8Encoding(false));

        foreach (string relative in modified)
            WriteDiff(originals[relative], patched[relative],
                Path.Combine(rawDiffRoot, relative + ".diff"));

        string normalized = Path.Combine(root, "normalized");
        string normalizerReport = Path.Combine(normalized, "normalizations.csv");
        RunProcess("dotnet", new[] { "run", "--project", normalizerProject,
            "--configuration", "Release", "--no-build", "--",
            originalSource, patchedSource,
            Path.Combine(normalized, "original"),
            Path.Combine(normalized, "patched"),
            normalizerReport }, repositoryRoot);
        string inventoryReport = Path.Combine(root,
            "assembly-semantic-inventory.csv");
        RunProcess("dotnet", new[] { "run", "--project", inventoryProject,
            "--configuration", "Release", "--no-build", "--",
            originalAssembly, patchedAssembly, inventoryReport }, repositoryRoot);
        Dictionary<string, InventoryTotals> inventory =
            ReadInventory(inventoryReport);
        Dictionary<string, NormalizationTotals> normalizations =
            ReadNormalizations(normalizerReport);

        string semanticDiffRoot = Path.Combine(root, "semantic-review-diffs");
        string playStateFeatureRoot = Path.Combine(root, "feature-diffs",
            "playstate-singleton");
        List<string> playStateFeatureFiles = new();
        List<AuditRow> rows = new();
        foreach (string relative in modified)
        {
            string normalizedOriginal = Path.Combine(normalized, "original", relative);
            string normalizedPatched = Path.Combine(normalized, "patched", relative);
            string reviewDiff = Path.Combine(semanticDiffRoot,
                relative + ".diff");
            WriteReviewDiff(normalizedOriginal, normalizedPatched, reviewDiff);
            bool playStateSingletonOnly =
                IsExclusivePlayStateSingletonDiff(reviewDiff);
            if (playStateSingletonOnly)
            {
                string featureDiff = Path.Combine(playStateFeatureRoot,
                    relative + ".diff");
                Directory.CreateDirectory(Path.GetDirectoryName(featureDiff)!);
                File.Move(reviewDiff, featureDiff);
                playStateFeatureFiles.Add(relative);
            }
            int rawLines = ChangedLines(originals[relative], patched[relative]);
            int normalizedLines = ChangedReviewLines(normalizedOriginal,
                normalizedPatched);
            InventoryTotals methods = inventory.GetValueOrDefault(relative);
            NormalizationTotals fixes = normalizations.GetValueOrDefault(relative);
            string status = normalizedLines <= rawLines
                ? "automated-semantic-review-complete"
                : "normalizer-regression";
            rows.Add(new AuditRow(name, relative, rawLines,
                normalizedLines, status, methods.Changed, methods.LayoutOnly,
                methods.AddedMethods, methods.AddedFields,
                methods.MetadataChanges,
                fixes.MatchedLocals, fixes.RestoredInitializers,
                fixes.RemovedCaptureAliases, playStateSingletonOnly));
        }
        Directory.CreateDirectory(playStateFeatureRoot);
        File.WriteAllLines(Path.Combine(playStateFeatureRoot, "files.txt"),
            playStateFeatureFiles.OrderBy(value => value,
                StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
        WriteAuditCsv(Path.Combine(root, "noise-audit.csv"), rows);
        File.WriteAllLines(Path.Combine(analysis, "analysis-summary.txt"), new[]
        {
            "identical_pairs_removed=" + identical.Count,
            "modified_files=" + modified.Length,
            "added_files_excluded=" + added.Length,
            "removed_files=" + removed.Length,
            "playstate_singleton_only_diffs=" + playStateFeatureFiles.Count
        }, new UTF8Encoding(false));
        return rows;
    }

    private static bool IsExclusivePlayStateSingletonDiff(string path)
    {
        List<string> changes = File.ReadLines(path).Where(line =>
            (line.StartsWith("+", StringComparison.Ordinal) ||
             line.StartsWith("-", StringComparison.Ordinal)) &&
            !line.StartsWith("+++", StringComparison.Ordinal) &&
            !line.StartsWith("---", StringComparison.Ordinal) &&
            line[1..].Trim().Length != 0).ToList();
        Regex fieldPattern = new(
            @"^\s*(?:private|protected|internal|public)\s+(?:readonly\s+)?PlayState\s+(\w+)\s*;\s*$",
            RegexOptions.CultureInvariant);
        string[] fields = changes.Where(line => line[0] == '-')
            .Select(line => fieldPattern.Match(line[1..]))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (fields.Length == 0) return false;

        List<string> additions = changes.Where(line => line[0] == '+')
            .Select(line => line[1..]).ToList();
        foreach (string removed in changes.Where(line => line[0] == '-')
            .Select(line => line[1..]))
        {
            bool accepted = false;
            foreach (string field in fields)
            {
                if (fieldPattern.IsMatch(removed) || Regex.IsMatch(removed,
                    @"^\s*(?:this\.)?" + Regex.Escape(field) +
                    @"\s*=\s*[^;]+;\s*$", RegexOptions.CultureInvariant))
                {
                    accepted = true;
                    break;
                }
                if (!Regex.IsMatch(removed, @"\b" + Regex.Escape(field) +
                    @"\b", RegexOptions.CultureInvariant))
                    continue;
                string replacement = Regex.Replace(removed,
                    @"\b(?:this\.)?" + Regex.Escape(field) + @"\b",
                    "PlayState.RecentPlayState", RegexOptions.CultureInvariant);
                int addition = additions.FindIndex(line => line == replacement);
                if (addition < 0) continue;
                additions.RemoveAt(addition);
                accepted = true;
                break;
            }
            if (!accepted) return false;
        }
        return additions.Count == 0;
    }

    private static Dictionary<string, InventoryTotals> ReadInventory(string path)
    {
        Dictionary<string, InventoryTotals> result =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            string[] values = ParseCsv(line);
            if (values.Length < 8) continue;
            InventoryTotals current = result.GetValueOrDefault(values[0]);
            result[values[0]] = new InventoryTotals(
                current.Changed + int.Parse(values[2], CultureInfo.InvariantCulture),
                current.LayoutOnly + int.Parse(values[3], CultureInfo.InvariantCulture),
                current.AddedMethods + int.Parse(values[5], CultureInfo.InvariantCulture),
                current.AddedFields + int.Parse(values[6], CultureInfo.InvariantCulture),
                current.MetadataChanges + int.Parse(values[7], CultureInfo.InvariantCulture));
        }
        return result;
    }

    private static Dictionary<string, NormalizationTotals> ReadNormalizations(
        string path)
    {
        Dictionary<string, NormalizationTotals> result =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            string[] values = ParseCsv(line);
            if (values.Length < 6) continue;
            result[values[0]] = new NormalizationTotals(
                int.Parse(values[1], CultureInfo.InvariantCulture),
                int.Parse(values[4], CultureInfo.InvariantCulture),
                int.Parse(values[5], CultureInfo.InvariantCulture));
        }
        return result;
    }

    private static string[] ParseCsv(string line)
    {
        List<string> values = new();
        StringBuilder value = new();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                { value.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            { values.Add(value.ToString()); value.Clear(); }
            else value.Append(character);
        }
        values.Add(value.ToString());
        return values.ToArray();
    }

    private static void Decompile(string assembly, string references, string output)
    {
        RunProcess("dotnet", new[] { "tool", "run", "ilspycmd", "--",
            "--disable-updatecheck", "--nested-directories", "--project",
            "--languageversion", "CSharp3", "--referencepath", references,
            "--outputdir", output, assembly }, experimentRoot);
    }

    private static void CopyReferences(string source, string destination)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*.dll"))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }

    private static Dictionary<string, string> FilesByRelativePath(string root)
    {
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => path,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool FilesEqual(string left, string right)
    {
        FileInfo a = new(left);
        FileInfo b = new(right);
        if (a.Length != b.Length)
            return false;
        return SHA256.HashData(File.ReadAllBytes(left)).AsSpan()
            .SequenceEqual(SHA256.HashData(File.ReadAllBytes(right)));
    }

    private static void WriteDiff(string before, string after, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        ProcessResult result = RunProcess("git", new[] { "-c",
            "core.safecrlf=false", "diff", "--no-index", "--output=" + output,
            "--", before, after }, repositoryRoot, 0, 1);
        _ = result;
    }

    private static int ChangedLines(string before, string after)
    {
        ProcessResult result = RunProcessQuiet("git", new[] { "-c",
            "core.safecrlf=false", "diff", "--no-index", "--numstat", "--",
            before, after }, repositoryRoot, 0, 1);
        string? line = result.StandardOutput.Split('\n',
            StringSplitOptions.RemoveEmptyEntries).LastOrDefault(value =>
                char.IsDigit(value.TrimStart().FirstOrDefault()));
        if (line == null)
            return 0;
        string[] parts = line.Trim().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);
        return int.Parse(parts[0], CultureInfo.InvariantCulture) +
            int.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    private static void WriteReviewDiff(string before, string after, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        RunProcess("git", new[] { "-c", "core.safecrlf=false", "diff",
            "--no-index", "--ignore-blank-lines", "--ignore-space-change",
            "--output=" + output, "--", before, after }, repositoryRoot, 0, 1);
    }

    private static int ChangedReviewLines(string before, string after)
    {
        ProcessResult result = RunProcessQuiet("git", new[] { "-c",
            "core.safecrlf=false", "diff", "--no-index", "--ignore-blank-lines",
            "--ignore-space-change", "--numstat", "--", before, after },
            repositoryRoot, 0, 1);
        string? line = result.StandardOutput.Split('\n',
            StringSplitOptions.RemoveEmptyEntries).LastOrDefault(value =>
                char.IsDigit(value.TrimStart().FirstOrDefault()));
        if (line == null) return 0;
        string[] parts = line.Trim().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);
        return int.Parse(parts[0], CultureInfo.InvariantCulture) +
            int.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    private static void WriteAuditCsv(string path, IEnumerable<AuditRow> rows)
    {
        using StreamWriter writer = new(path, false, new UTF8Encoding(false));
        writer.WriteLine("Assembly,File,RawChangedLines,NormalizedChangedLines,Status,ChangedExistingMethods,LayoutOnlyMethods,AddedMethods,AddedFields,MetadataChanges,MatchedLocals,RestoredInitializers,RemovedCaptureAliases,Feature");
        foreach (AuditRow row in rows)
            writer.WriteLine(string.Join(",", Csv(row.Assembly), Csv(row.File),
                row.RawChangedLines, row.NormalizedChangedLines, Csv(row.Status),
                row.ChangedMethods, row.LayoutOnlyMethods, row.AddedMethods,
                row.AddedFields, row.MetadataChanges, row.MatchedLocals,
                row.RestoredInitializers,
                row.RemovedCaptureAliases,
                Csv(row.PlayStateSingletonOnly ? "playstate-singleton" : "")));
    }

    private static void WriteChecklist(IEnumerable<AuditRow> allRows)
    {
        List<string> lines = new()
        {
            "# Manual payload denoise checklist", "",
            "Legend: `[x]` passed the paired-source and stable-signature IL review; " +
                "`[!]` indicates that normalization enlarged the diff.", ""
        };
        foreach (string assembly in new[] { "magicka", "polygonhead" })
        {
            lines.Add("## " + assembly);
            lines.Add("");
            foreach (AuditRow row in allRows.Where(row => row.Assembly == assembly)
                .OrderBy(row => row.File, StringComparer.OrdinalIgnoreCase))
            {
                string marker = row.Status ==
                    "automated-semantic-review-complete" ? "x" : "!";
                lines.Add("- [" + marker + "] `" + row.File + "` - " + row.Status +
                    "; raw " + row.RawChangedLines + ", normalized " +
                    row.NormalizedChangedLines + " changed lines; existing bodies " +
                    row.ChangedMethods + " semantic/compiler-shaped, " +
                    row.LayoutOnlyMethods + " IL-layout-only; added methods " +
                    row.AddedMethods + ", fields " + row.AddedFields +
                    ", metadata changes " + row.MetadataChanges +
                    "; normalized locals " + row.MatchedLocals +
                    ", static initializers " + row.RestoredInitializers +
                    ", capture aliases " + row.RemovedCaptureAliases +
                    (row.PlayStateSingletonOnly
                        ? "; feature `playstate-singleton`" : ""));
            }
            lines.Add("");
        }
        File.WriteAllLines(Path.Combine(outputRoot, "DENOISE_CHECKLIST.md"),
            lines, new UTF8Encoding(false));
    }

    private static void WriteInputs(params string[] paths)
    {
        string[] names = { "original_magicka", "patched_magicka",
            "original_polygonhead", "patched_polygonhead" };
        File.WriteAllLines(Path.Combine(outputRoot, "inputs.txt"),
            paths.Select((path, index) => names[index] + "=" + path +
                Environment.NewLine + names[index] + "_sha256=" + Hash(path)),
            Encoding.ASCII);
    }

    private static string Hash(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static string Csv(object value)
    {
        return "\"" + Convert.ToString(value, CultureInfo.InvariantCulture)!
            .Replace("\"", "\"\"") + "\"";
    }

    private static string RequireFile(string path, string label)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(label + " does not exist.", fullPath);
        return fullPath;
    }

    private static string FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new(start);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static ProcessResult RunProcess(string fileName,
        IEnumerable<string> arguments, string workingDirectory,
        params int[] acceptedExitCodes)
    {
        return RunProcessCore(fileName, arguments, workingDirectory, true,
            acceptedExitCodes);
    }

    private static ProcessResult RunProcessQuiet(string fileName,
        IEnumerable<string> arguments, string workingDirectory,
        params int[] acceptedExitCodes)
    {
        return RunProcessCore(fileName, arguments, workingDirectory, false,
            acceptedExitCodes);
    }

    private static ProcessResult RunProcessCore(string fileName,
        IEnumerable<string> arguments, string workingDirectory, bool echo,
        params int[] acceptedExitCodes)
    {
        if (acceptedExitCodes.Length == 0)
            acceptedExitCodes = new[] { 0 };
        ProcessStartInfo start = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException("Failed to start " + fileName);
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (echo)
        {
            Console.Write(standardOutput);
            if (standardError.Length != 0)
                Console.Error.Write(standardError);
        }
        if (!acceptedExitCodes.Contains(process.ExitCode))
            throw new InvalidOperationException(fileName + " exited with " +
                process.ExitCode + ".");
        return new ProcessResult(standardOutput, standardError, process.ExitCode);
    }

    private sealed record AuditRow(string Assembly, string File,
        int RawChangedLines, int NormalizedChangedLines, string Status,
        int ChangedMethods, int LayoutOnlyMethods, int AddedMethods,
        int AddedFields, int MetadataChanges, int MatchedLocals, int RestoredInitializers,
        int RemovedCaptureAliases, bool PlayStateSingletonOnly);
    private readonly record struct InventoryTotals(int Changed, int LayoutOnly,
        int AddedMethods, int AddedFields, int MetadataChanges);
    private readonly record struct NormalizationTotals(int MatchedLocals,
        int RestoredInitializers, int RemovedCaptureAliases);
    private sealed record ProcessResult(string StandardOutput,
        string StandardError, int ExitCode);
}
