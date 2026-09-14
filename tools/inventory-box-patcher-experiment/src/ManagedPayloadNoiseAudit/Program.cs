using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        string inventoryReport = Path.Combine(root,
            "assembly-semantic-inventory.csv");
        string methodInventoryReport = Path.Combine(root,
            "assembly-method-inventory.tsv");
        RunProcess("dotnet", new[] { "run", "--project", inventoryProject,
            "--configuration", "Release", "--no-build", "--",
            originalAssembly, patchedAssembly, inventoryReport,
            methodInventoryReport }, repositoryRoot);
        RunProcess("dotnet", new[] { "run", "--project", normalizerProject,
            "--configuration", "Release", "--no-build", "--",
            originalSource, patchedSource,
            Path.Combine(normalized, "original"),
            Path.Combine(normalized, "patched"),
            normalizerReport, methodInventoryReport }, repositoryRoot);
        Dictionary<string, InventoryTotals> inventory =
            ReadInventory(inventoryReport);
        Dictionary<string, NormalizationTotals> normalizations =
            ReadNormalizations(normalizerReport);

        string semanticDiffRoot = Path.Combine(root, "semantic-review-diffs");
        string playStateFeatureRoot = Path.Combine(root, "feature-diffs",
            "playstate-singleton");
        string gcRetentionFeatureRoot = Path.Combine(root, "feature-diffs",
            "gc-retention");
        string cacheCleanupFeatureRoot = Path.Combine(root, "feature-diffs",
            "cache-and-level-reference-cleanup");
        List<string> playStateFeatureFiles = new();
        List<string> gcRetentionFeatureFiles = new();
        List<string> cacheCleanupFeatureFiles = new();
        List<AuditRow> rows = new();
        foreach (string relative in modified)
        {
            string normalizedOriginal = Path.Combine(normalized, "original", relative);
            string normalizedPatched = Path.Combine(normalized, "patched", relative);
            string reviewDiff = Path.Combine(semanticDiffRoot,
                relative + ".diff");
            WriteReviewDiff(normalizedOriginal, normalizedPatched, reviewDiff);
            bool normalizedIdentical = SyntaxEquivalent(normalizedOriginal,
                normalizedPatched);
            if (normalizedIdentical && File.Exists(reviewDiff))
                File.Delete(reviewDiff);
            bool playStateSingletonOnly = !normalizedIdentical &&
                IsExclusivePlayStateSingletonDiff(reviewDiff);
            if (playStateSingletonOnly)
            {
                string featureDiff = Path.Combine(playStateFeatureRoot,
                    relative + ".diff");
                Directory.CreateDirectory(Path.GetDirectoryName(featureDiff)!);
                File.Move(reviewDiff, featureDiff);
                playStateFeatureFiles.Add(relative);
            }
            bool gcRetentionOnly = !normalizedIdentical && !playStateSingletonOnly &&
                IsExclusiveGcRetentionDiff(normalizedOriginal, normalizedPatched);
            if (gcRetentionOnly)
            {
                string featureDiff = Path.Combine(gcRetentionFeatureRoot,
                    relative + ".diff");
                Directory.CreateDirectory(Path.GetDirectoryName(featureDiff)!);
                File.Move(reviewDiff, featureDiff);
                gcRetentionFeatureFiles.Add(relative);
            }
            bool cacheCleanupOnly = !normalizedIdentical &&
                !playStateSingletonOnly && !gcRetentionOnly &&
                IsExclusiveCacheAndLevelReferenceCleanup(normalizedOriginal,
                    normalizedPatched);
            if (cacheCleanupOnly)
            {
                string featureDiff = Path.Combine(cacheCleanupFeatureRoot,
                    relative + ".diff");
                Directory.CreateDirectory(Path.GetDirectoryName(featureDiff)!);
                File.Move(reviewDiff, featureDiff);
                cacheCleanupFeatureFiles.Add(relative);
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
                fixes.RemovedCaptureAliases, fixes.RestoredTemporaries,
                fixes.RestoredCompoundAssignments, fixes.RestoredSwitchOrders,
                fixes.RestoredBaseOrders, fixes.RestoredLayoutOnlyCallables,
                playStateSingletonOnly, gcRetentionOnly, cacheCleanupOnly,
                normalizedIdentical));
        }
        Directory.CreateDirectory(playStateFeatureRoot);
        File.WriteAllLines(Path.Combine(playStateFeatureRoot, "files.txt"),
            playStateFeatureFiles.OrderBy(value => value,
                StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
        Directory.CreateDirectory(gcRetentionFeatureRoot);
        File.WriteAllLines(Path.Combine(gcRetentionFeatureRoot, "files.txt"),
            gcRetentionFeatureFiles.OrderBy(value => value,
                StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
        Directory.CreateDirectory(cacheCleanupFeatureRoot);
        File.WriteAllLines(Path.Combine(cacheCleanupFeatureRoot, "files.txt"),
            cacheCleanupFeatureFiles.OrderBy(value => value,
                StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
        WriteAuditCsv(Path.Combine(root, "noise-audit.csv"), rows);
        File.WriteAllLines(Path.Combine(analysis, "analysis-summary.txt"), new[]
        {
            "identical_pairs_removed=" + identical.Count,
            "modified_files=" + modified.Length,
            "added_files_excluded=" + added.Length,
            "removed_files=" + removed.Length,
            "playstate_singleton_only_diffs=" + playStateFeatureFiles.Count,
            "gc_retention_only_diffs=" + gcRetentionFeatureFiles.Count,
            "cache_and_level_reference_cleanup_only_diffs=" +
                cacheCleanupFeatureFiles.Count
        }, new UTF8Encoding(false));
        return rows;
    }

    private static bool IsExclusiveGcRetentionDiff(string originalPath,
        string patchedPath)
    {
        string original = File.ReadAllText(originalPath);
        SyntaxNode originalRoot = CSharpSyntaxTree.ParseText(original,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3)).GetRoot();
        SyntaxNode patchedRoot = CSharpSyntaxTree.ParseText(File.ReadAllText(patchedPath),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3)).GetRoot();
        GcRetentionRemovalRewriter rewriter = new();
        SyntaxNode stripped = rewriter.Visit(patchedRoot)!;
        return rewriter.Removed != 0 && originalRoot.WithoutTrivia()
            .IsEquivalentTo(stripped.WithoutTrivia());
    }

    private static bool SyntaxEquivalent(string originalPath, string patchedPath)
    {
        SyntaxNode original = CSharpSyntaxTree.ParseText(File.ReadAllText(originalPath),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3)).GetRoot();
        SyntaxNode patched = CSharpSyntaxTree.ParseText(File.ReadAllText(patchedPath),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3)).GetRoot();
        return original.WithoutTrivia().IsEquivalentTo(patched.WithoutTrivia());
    }

    private static bool IsExclusiveCacheAndLevelReferenceCleanup(
        string originalPath, string patchedPath)
    {
        SyntaxNode original = CSharpSyntaxTree.ParseText(File.ReadAllText(originalPath),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3)).GetRoot();
        SyntaxNode patched = CSharpSyntaxTree.ParseText(File.ReadAllText(patchedPath),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3)).GetRoot();
        HashSet<string> originalUsings = original.DescendantNodes()
            .OfType<UsingDirectiveSyntax>().Select(UsingKey)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> originalMethods = original.DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Select(MethodKey)
            .ToHashSet(StringComparer.Ordinal);
        CacheAndLevelReferenceCleanupRewriter rewriter = new(originalUsings,
            originalMethods);
        SyntaxNode stripped = rewriter.Visit(patched)!;
        return rewriter.RemovedCleanupMethod && original.WithoutTrivia()
            .IsEquivalentTo(stripped.WithoutTrivia());
    }

    private static string UsingKey(UsingDirectiveSyntax directive) =>
        directive.WithoutTrivia().ToFullString();

    private static string MethodKey(MethodDeclarationSyntax method)
    {
        string owner = string.Join("/", method.Ancestors()
            .OfType<TypeDeclarationSyntax>().Reverse()
            .Select(type => type.Identifier.ValueText));
        return owner + "|" + method.Identifier.ValueText + "|" +
            method.TypeParameterList?.Parameters.Count + "|" +
            string.Join(",", method.ParameterList.Parameters.Select(parameter =>
                parameter.Type?.WithoutTrivia().ToFullString() ?? ""));
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
            if (values.Length < 11) continue;
            result[values[0]] = new NormalizationTotals(
                int.Parse(values[1], CultureInfo.InvariantCulture),
                int.Parse(values[4], CultureInfo.InvariantCulture),
                int.Parse(values[5], CultureInfo.InvariantCulture),
                int.Parse(values[6], CultureInfo.InvariantCulture),
                int.Parse(values[7], CultureInfo.InvariantCulture),
                int.Parse(values[8], CultureInfo.InvariantCulture),
                int.Parse(values[9], CultureInfo.InvariantCulture),
                int.Parse(values[10], CultureInfo.InvariantCulture));
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
        writer.WriteLine("Assembly,File,RawChangedLines,NormalizedChangedLines,Status,ChangedExistingMethods,LayoutOnlyMethods,AddedMethods,AddedFields,MetadataChanges,MatchedLocals,RestoredInitializers,RemovedCaptureAliases,RestoredTemporaries,RestoredCompoundAssignments,RestoredSwitchOrders,RestoredBaseOrders,RestoredLayoutOnlyCallables,Feature");
        foreach (AuditRow row in rows)
            writer.WriteLine(string.Join(",", Csv(row.Assembly), Csv(row.File),
                row.RawChangedLines, row.NormalizedChangedLines, Csv(row.Status),
                row.ChangedMethods, row.LayoutOnlyMethods, row.AddedMethods,
                row.AddedFields, row.MetadataChanges, row.MatchedLocals,
                row.RestoredInitializers,
                row.RemovedCaptureAliases,
                row.RestoredTemporaries, row.RestoredCompoundAssignments,
                row.RestoredSwitchOrders, row.RestoredBaseOrders,
                row.RestoredLayoutOnlyCallables,
                Csv(row.PlayStateSingletonOnly ? "playstate-singleton" :
                    row.GcRetentionOnly ? "gc-retention" :
                    row.CacheCleanupOnly ?
                        "cache-and-level-reference-cleanup" : "")));
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
                    ", temporaries " + row.RestoredTemporaries +
                    ", compound assignments " + row.RestoredCompoundAssignments +
                    ", switch orders " + row.RestoredSwitchOrders +
                    ", base orders " + row.RestoredBaseOrders +
                    ", layout-only callables " + row.RestoredLayoutOnlyCallables +
                    (row.PlayStateSingletonOnly
                        ? "; feature `playstate-singleton`" :
                    row.GcRetentionOnly ? "; feature `gc-retention`" :
                    row.CacheCleanupOnly ?
                        "; feature `cache-and-level-reference-cleanup`" :
                        row.NormalizedIdentical ?
                            "; all differences normalized as noise" : ""));
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
        int RemovedCaptureAliases, int RestoredTemporaries,
        int RestoredCompoundAssignments, int RestoredSwitchOrders,
        int RestoredBaseOrders, int RestoredLayoutOnlyCallables,
        bool PlayStateSingletonOnly, bool GcRetentionOnly,
        bool CacheCleanupOnly,
        bool NormalizedIdentical);
    private readonly record struct InventoryTotals(int Changed, int LayoutOnly,
        int AddedMethods, int AddedFields, int MetadataChanges);
    private readonly record struct NormalizationTotals(int MatchedLocals,
        int RestoredInitializers, int RemovedCaptureAliases,
        int RestoredTemporaries, int RestoredCompoundAssignments,
        int RestoredSwitchOrders, int RestoredBaseOrders,
        int RestoredLayoutOnlyCallables);
    private sealed record ProcessResult(string StandardOutput,
        string StandardError, int ExitCode);
}

internal sealed class GcRetentionRemovalRewriter : CSharpSyntaxRewriter
{
    public int Removed { get; private set; }

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name?.ToString() == "Magicka.GcDiagnostics")
        {
            Removed++;
            return null;
        }
        return base.VisitUsingDirective(node);
    }

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax member &&
            member.Expression.ToString() == "RetentionRegistry")
        {
            Removed++;
            return node.Parent is BlockSyntax or SwitchSectionSyntax
                ? null
                : SyntaxFactory.EmptyStatement().WithTriviaFrom(node);
        }
        return base.VisitExpressionStatement(node);
    }
}

internal sealed class CacheAndLevelReferenceCleanupRewriter : CSharpSyntaxRewriter
{
    private static readonly HashSet<string> CleanupMethodNames = new(
        StringComparer.Ordinal)
    {
        "Clear", "ClearCache", "ClearInstances", "DisposeCache",
        "DisposeCaches", "DisposePickableCache", "ReleaseLevelReferences",
        "ResetForLevelUnload"
    };

    private readonly HashSet<string> originalUsings;
    private readonly HashSet<string> originalMethods;
    public bool RemovedCleanupMethod { get; private set; }

    public CacheAndLevelReferenceCleanupRewriter(
        HashSet<string> originalUsings, HashSet<string> originalMethods)
    {
        this.originalUsings = originalUsings;
        this.originalMethods = originalMethods;
    }

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (!originalUsings.Contains(node.WithoutTrivia().ToFullString()))
            return null;
        return base.VisitUsingDirective(node);
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (CleanupMethodNames.Contains(node.Identifier.ValueText) &&
            !originalMethods.Contains(MethodKey(node)))
        {
            RemovedCleanupMethod = true;
            return null;
        }
        return base.VisitMethodDeclaration(node);
    }

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax member &&
            member.Expression.ToString() == "RetentionRegistry")
            return node.Parent is BlockSyntax or SwitchSectionSyntax
                ? null
                : SyntaxFactory.EmptyStatement().WithTriviaFrom(node);
        return base.VisitExpressionStatement(node);
    }

    private static string MethodKey(MethodDeclarationSyntax method)
    {
        string owner = string.Join("/", method.Ancestors()
            .OfType<TypeDeclarationSyntax>().Reverse()
            .Select(type => type.Identifier.ValueText));
        return owner + "|" + method.Identifier.ValueText + "|" +
            method.TypeParameterList?.Parameters.Count + "|" +
            string.Join(",", method.ParameterList.Parameters.Select(parameter =>
                parameter.Type?.WithoutTrivia().ToFullString() ?? ""));
    }
}
