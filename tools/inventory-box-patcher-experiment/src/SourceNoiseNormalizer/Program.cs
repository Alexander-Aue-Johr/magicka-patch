using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

return SourceNoiseNormalizer.Run(args);

internal static class SourceNoiseNormalizer
{
    public static int Run(string[] args)
    {
        if (args.Length is < 5 or > 7)
        {
            Console.Error.WriteLine(
                "usage: SourceNoiseNormalizer <original-root> <patched-root> " +
                "<normalized-original-root> <normalized-patched-root> <report.csv> " +
                "[methods.tsv] [All|Exclude|Only]");
            return 2;
        }

        string originalRoot = FullDirectory(args[0]);
        string patchedRoot = FullDirectory(args[1]);
        string normalizedOriginalRoot = PrepareOutput(args[2]);
        string normalizedPatchedRoot = PrepareOutput(args[3]);
        string reportPath = Path.GetFullPath(args[4]);
        Dictionary<string, HashSet<CallableDescriptor>> layoutOnly = args.Length == 6
            || args.Length == 7 ? ReadLayoutOnlyMethods(args[5])
                : new(StringComparer.OrdinalIgnoreCase);
        GcDiagnosticsMode diagnosticsMode = args.Length == 7
            ? Enum.Parse<GcDiagnosticsMode>(args[6], true)
            : GcDiagnosticsMode.All;
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        Dictionary<string, string> originals = FilesByRelativePath(originalRoot);
        Dictionary<string, string> patched = FilesByRelativePath(patchedRoot);
        string[] common = originals.Keys.Intersect(patched.Keys,
            StringComparer.OrdinalIgnoreCase).OrderBy(path => path,
            StringComparer.OrdinalIgnoreCase).ToArray();

        using StreamWriter report = new(reportPath, false, new UTF8Encoding(false));
        report.WriteLine("file,matched_locals,original_only_locals,patched_only_locals,restored_initializers,removed_capture_aliases,restored_temporaries,restored_compound_assignments,restored_switch_orders,restored_base_orders,restored_layout_only_callables");
        foreach (string relativePath in common)
        {
            SourcePair pair = NormalizePair(File.ReadAllText(originals[relativePath]),
                File.ReadAllText(patched[relativePath]),
                layoutOnly.GetValueOrDefault(relativePath), diagnosticsMode);
            Write(normalizedOriginalRoot, relativePath, pair.Original);
            Write(normalizedPatchedRoot, relativePath, pair.Patched);
            report.WriteLine(Csv(relativePath) + "," + pair.MatchedLocals + "," +
                pair.OriginalOnlyLocals + "," + pair.PatchedOnlyLocals + "," +
                pair.RestoredInitializers + "," + pair.RemovedCaptureAliases + "," +
                pair.RestoredTemporaries + "," + pair.RestoredCompoundAssignments +
                "," + pair.RestoredSwitchOrders + "," + pair.RestoredBaseOrders +
                "," + pair.RestoredLayoutOnlyCallables);
        }
        Console.WriteLine("normalized_pairs=" + common.Length);
        return 0;
    }

    private static SourcePair NormalizePair(string original, string patched,
        HashSet<CallableDescriptor>? layoutOnly, GcDiagnosticsMode diagnosticsMode)
    {
        SyntaxNode originalRoot = Parse(original);
        SyntaxNode patchedRoot = Parse(patched);
        if (diagnosticsMode == GcDiagnosticsMode.Only)
        {
            (string emptyProjection, string diagnosticsProjection) =
                GcDiagnosticsProjection.Create(patchedRoot);
            ValidateOutput(emptyProjection, "empty GC diagnostics projection");
            ValidateOutput(diagnosticsProjection, "GC diagnostics projection");
            return new SourcePair(emptyProjection, diagnosticsProjection,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        if (diagnosticsMode == GcDiagnosticsMode.Exclude)
            patchedRoot = new GcDiagnosticsRemovalRewriter().Visit(patchedRoot)!;
        LayoutOnlyCallableNormalizer layoutNormalizer = new(originalRoot,
            layoutOnly ?? new HashSet<CallableDescriptor>());
        patchedRoot = layoutNormalizer.Normalize(patchedRoot);
        PairMaps maps = PairMaps.Create(originalRoot, patchedRoot);
        SyntaxNode normalizedOriginal = originalRoot;
        SyntaxNode normalizedPatched = new LocalRenameRewriter(
            maps.PatchedTokenRenames).Visit(patchedRoot)!;
        CaptureAliasNormalizer aliasNormalizer = new();
        normalizedPatched = aliasNormalizer.Visit(normalizedPatched)!;
        PairSyntaxNoiseNormalizer syntaxNormalizer = new(normalizedOriginal);
        normalizedPatched = syntaxNormalizer.Normalize(normalizedPatched);
        StaticInitializerNormalizer initializerNormalizer = new(
            normalizedOriginal);
        normalizedPatched = initializerNormalizer.Visit(normalizedPatched)!;
        string normalizedOriginalText = normalizedOriginal.ToFullString();
        string normalizedPatchedText = normalizedPatched.ToFullString();
        ValidateOutput(normalizedOriginalText, "normalized original source");
        ValidateOutput(normalizedPatchedText, "normalized patched source");
        return new SourcePair(normalizedOriginalText,
            normalizedPatchedText, maps.MatchedLocals,
            maps.OriginalOnlyLocals, maps.PatchedOnlyLocals,
            initializerNormalizer.RestoredInitializers,
            aliasNormalizer.RemovedAliases,
            syntaxNormalizer.RestoredTemporaries,
            syntaxNormalizer.RestoredCompoundAssignments,
            syntaxNormalizer.RestoredSwitchOrders,
            syntaxNormalizer.RestoredBaseOrders,
            layoutNormalizer.RestoredCallables);
    }

    private static SyntaxNode Parse(string source)
    {
        return CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3))
            .GetRoot();
    }

    private static void ValidateOutput(string source, string description)
    {
        Diagnostic[] errors = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3))
            .GetDiagnostics().Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
            throw new InvalidDataException(description + " is not valid C#: " +
                string.Join(" | ", errors.Take(5).Select(error => error.ToString())));
    }

    private static string FullDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }

    private static string PrepareOutput(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static Dictionary<string, string> FilesByRelativePath(string root)
    {
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => path,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, HashSet<CallableDescriptor>> ReadLayoutOnlyMethods(
        string path)
    {
        Dictionary<string, HashSet<CallableDescriptor>> result =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(Path.GetFullPath(path)).Skip(1))
        {
            string[] values = line.Split('\t');
            if (values.Length != 7 || values[6] != "layout-only")
                continue;
            if (!result.TryGetValue(values[0], out HashSet<CallableDescriptor>? methods))
                result.Add(values[0], methods = new HashSet<CallableDescriptor>());
            methods.Add(new CallableDescriptor(values[1], values[2],
                int.Parse(values[3], CultureInfo.InvariantCulture),
                int.Parse(values[4], CultureInfo.InvariantCulture),
                int.Parse(values[5], CultureInfo.InvariantCulture)));
        }
        return result;
    }

    private static void Write(string root, string relativePath, string text)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static string Csv(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";

    private readonly record struct SourcePair(string Original, string Patched,
        int MatchedLocals, int OriginalOnlyLocals, int PatchedOnlyLocals,
        int RestoredInitializers, int RemovedCaptureAliases,
        int RestoredTemporaries, int RestoredCompoundAssignments,
        int RestoredSwitchOrders, int RestoredBaseOrders,
        int RestoredLayoutOnlyCallables);
}

internal readonly record struct CallableDescriptor(string Type, string Name,
    int ParameterCount, int GenericCount, int Ordinal);

internal enum GcDiagnosticsMode
{
    All,
    Exclude,
    Only
}

internal sealed class GcDiagnosticsRemovalRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node) =>
        node.Name?.ToString() == "Magicka.GcDiagnostics"
            ? null
            : base.VisitUsingDirective(node);

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (GcDiagnosticsProjection.IsRetentionRegistryCall(node.Expression))
            return node.Parent is BlockSyntax or SwitchSectionSyntax
                ? null
                : SyntaxFactory.EmptyStatement().WithTriviaFrom(node);
        return base.VisitExpressionStatement(node);
    }
}

internal static class GcDiagnosticsProjection
{
    public static (string Empty, string Diagnostics) Create(SyntaxNode patchedRoot)
    {
        ExpressionStatementSyntax[] calls = patchedRoot.DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Where(statement => IsRetentionRegistryCall(statement.Expression))
            .ToArray();
        string body = string.Join(Environment.NewLine, calls.Select(call =>
            "\t\t\t" + Regex.Replace(call.WithoutTrivia().NormalizeWhitespace()
                .ToFullString(), @"\s+", " ")));
        string shellStart = "namespace ManagedPayloadGcDiagnosticsProjection" +
            Environment.NewLine + "{" + Environment.NewLine +
            "\tinternal static class Entries" + Environment.NewLine + "\t{" +
            Environment.NewLine + "\t\tinternal static void Record()" +
            Environment.NewLine + "\t\t{" + Environment.NewLine;
        string shellEnd = "\t\t}" + Environment.NewLine + "\t}" +
            Environment.NewLine + "}" + Environment.NewLine;
        string empty = shellStart + shellEnd;
        if (calls.Length == 0)
            return (empty, empty);
        string diagnostics = "using Magicka.GcDiagnostics;" + Environment.NewLine +
            shellStart + body + Environment.NewLine + shellEnd;
        return (empty, diagnostics);
    }

    public static bool IsRetentionRegistryCall(ExpressionSyntax expression) =>
        expression is InvocationExpressionSyntax invocation &&
        invocation.Expression is MemberAccessExpressionSyntax member &&
        member.Expression.ToString() == "RetentionRegistry";
}

internal sealed class LayoutOnlyCallableNormalizer
{
    private readonly Dictionary<CallableDescriptor, SyntaxNode> originals;
    private readonly HashSet<CallableDescriptor> layoutOnly;
    public int RestoredCallables { get; private set; }

    public LayoutOnlyCallableNormalizer(SyntaxNode originalRoot,
        HashSet<CallableDescriptor> layoutOnly)
    {
        this.layoutOnly = layoutOnly;
        originals = Describe(originalRoot).ToDictionary(value => value.Descriptor,
            value => value.Node);
    }

    public SyntaxNode Normalize(SyntaxNode patchedRoot)
    {
        Dictionary<SyntaxNode, SyntaxNode> replacements = new();
        foreach ((CallableDescriptor descriptor, SyntaxNode node) in Describe(patchedRoot))
        {
            if (!layoutOnly.Contains(descriptor) ||
                !originals.TryGetValue(descriptor, out SyntaxNode? original))
                continue;
            SyntaxNode restored = RestoreBody(node, original);
            if (ReferenceEquals(restored, node))
                continue;
            replacements[node] = restored;
            RestoredCallables++;
        }
        return patchedRoot.ReplaceNodes(replacements.Keys,
            (node, _) => replacements[node]);
    }

    private static SyntaxNode RestoreBody(SyntaxNode patched, SyntaxNode original) =>
        (patched, original) switch
        {
            (MethodDeclarationSyntax after, MethodDeclarationSyntax before) => after
                .WithBody(before.Body?.WithTriviaFrom(after.Body ?? before.Body!))
                .WithExpressionBody(before.ExpressionBody)
                .WithSemicolonToken(before.SemicolonToken),
            (ConstructorDeclarationSyntax after, ConstructorDeclarationSyntax before) => after
                .WithBody(before.Body?.WithTriviaFrom(after.Body ?? before.Body!))
                .WithExpressionBody(before.ExpressionBody)
                .WithSemicolonToken(before.SemicolonToken),
            (DestructorDeclarationSyntax after, DestructorDeclarationSyntax before)
                when before.Body != null && after.Body != null => after
                    .WithBody(before.Body.WithTriviaFrom(after.Body)),
            (AccessorDeclarationSyntax after, AccessorDeclarationSyntax before) => after
                .WithBody(before.Body?.WithTriviaFrom(after.Body ?? before.Body!))
                .WithExpressionBody(before.ExpressionBody)
                .WithSemicolonToken(before.SemicolonToken),
            _ => patched
        };

    private static IEnumerable<(CallableDescriptor Descriptor, SyntaxNode Node)> Describe(
        SyntaxNode root)
    {
        Dictionary<(string Type, string Name, int Parameters, int Generic), int> ordinals = new();
        foreach (SyntaxNode node in root.DescendantNodes().Where(IsRestorable))
        {
            string type = FullTypeName(node);
            (string Name, int Parameters, int Generic) signature = Signature(node);
            var key = (type, signature.Name, signature.Parameters, signature.Generic);
            int ordinal = ordinals.GetValueOrDefault(key);
            ordinals[key] = ordinal + 1;
            yield return (new CallableDescriptor(type, signature.Name,
                signature.Parameters, signature.Generic, ordinal), node);
        }
    }

    private static bool IsRestorable(SyntaxNode node) =>
        node is MethodDeclarationSyntax or ConstructorDeclarationSyntax or
            DestructorDeclarationSyntax or AccessorDeclarationSyntax;

    private static (string Name, int Parameters, int Generic) Signature(SyntaxNode node) =>
        node switch
        {
            MethodDeclarationSyntax method => (method.Identifier.ValueText,
                method.ParameterList.Parameters.Count,
                method.TypeParameterList?.Parameters.Count ?? 0),
            ConstructorDeclarationSyntax constructor =>
                (constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor",
                    constructor.ParameterList.Parameters.Count, 0),
            DestructorDeclarationSyntax => ("Finalize", 0, 0),
            AccessorDeclarationSyntax accessor => AccessorSignature(accessor),
            _ => ("", 0, 0)
        };

    private static (string Name, int Parameters, int Generic) AccessorSignature(
        AccessorDeclarationSyntax accessor)
    {
        SyntaxNode? member = accessor.Parent?.Parent;
        string memberName = member switch
        {
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            IndexerDeclarationSyntax => "Item",
            EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.ValueText,
            _ => ""
        };
        int parameters = member is IndexerDeclarationSyntax indexer
            ? indexer.ParameterList.Parameters.Count : 0;
        if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
            accessor.IsKind(SyntaxKind.AddAccessorDeclaration) ||
            accessor.IsKind(SyntaxKind.RemoveAccessorDeclaration))
            parameters++;
        string prefix = accessor.Kind() switch
        {
            SyntaxKind.GetAccessorDeclaration => "get_",
            SyntaxKind.SetAccessorDeclaration => "set_",
            SyntaxKind.AddAccessorDeclaration => "add_",
            _ => "remove_"
        };
        return (prefix + memberName, parameters, 0);
    }

    private static string FullTypeName(SyntaxNode node)
    {
        TypeDeclarationSyntax[] types = node.Ancestors().OfType<TypeDeclarationSyntax>()
            .Reverse().ToArray();
        string ns = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString() ?? "";
        string type = string.Join("/", types.Select(value => value.Identifier.ValueText +
            (value.TypeParameterList == null ? "" : "`" + value.TypeParameterList.Parameters.Count)));
        return ns.Length == 0 ? type : ns + "." + type;
    }
}

internal sealed class PairSyntaxNoiseNormalizer
{
    private readonly SyntaxNode originalRoot;
    public int RestoredTemporaries { get; private set; }
    public int RestoredCompoundAssignments { get; private set; }
    public int RestoredSwitchOrders { get; private set; }
    public int RestoredBaseOrders { get; private set; }

    public PairSyntaxNoiseNormalizer(SyntaxNode originalRoot)
    {
        this.originalRoot = originalRoot;
    }

    public SyntaxNode Normalize(SyntaxNode patchedRoot)
    {
        patchedRoot = RestoreBaseOrder(patchedRoot);
        Dictionary<string, List<SyntaxNode>> originals = PairMaps.IndexCallables(originalRoot);
        Dictionary<string, List<SyntaxNode>> patched = PairMaps.IndexCallables(patchedRoot);
        List<(SyntaxNode Old, SyntaxNode New)> replacements = new();
        foreach (string key in originals.Keys.Intersect(patched.Keys, StringComparer.Ordinal))
        {
            int count = Math.Min(originals[key].Count, patched[key].Count);
            for (int index = 0; index < count; index++)
            {
                SyntaxNode before = originals[key][index];
                SyntaxNode after = patched[key][index];
                CallableNoiseRewriter rewriter = new(before);
                SyntaxNode changed = rewriter.Visit(after)!;
                RestoredTemporaries += rewriter.RestoredTemporaries;
                RestoredCompoundAssignments += rewriter.RestoredCompoundAssignments;
                RestoredSwitchOrders += rewriter.RestoredSwitchOrders;
                if (!ReferenceEquals(changed, after))
                    replacements.Add((after, changed));
            }
        }
        Dictionary<SyntaxNode, SyntaxNode> map = replacements.ToDictionary(
            value => value.Old, value => value.New);
        return patchedRoot.ReplaceNodes(map.Keys, (node, _) => map[node]);
    }

    private SyntaxNode RestoreBaseOrder(SyntaxNode patchedRoot)
    {
        Dictionary<string, TypeDeclarationSyntax> originals = originalRoot.DescendantNodes()
            .OfType<TypeDeclarationSyntax>().ToDictionary(TypeKey, StringComparer.Ordinal);
        TypeDeclarationSyntax[] candidates = patchedRoot.DescendantNodes()
            .OfType<TypeDeclarationSyntax>().Where(type => type.BaseList != null &&
                originals.TryGetValue(TypeKey(type), out TypeDeclarationSyntax? before) &&
                before.BaseList != null && SameBaseSet(before.BaseList, type.BaseList) &&
                !before.BaseList.Types.Select(BaseKey).SequenceEqual(
                    type.BaseList.Types.Select(BaseKey), StringComparer.Ordinal)).ToArray();
        RestoredBaseOrders = candidates.Length;
        return patchedRoot.ReplaceNodes(candidates, (type, _) =>
            type.WithBaseList(originals[TypeKey(type)].BaseList!.WithTriviaFrom(type.BaseList!)));
    }

    private static bool SameBaseSet(BaseListSyntax left, BaseListSyntax right) =>
        left.Types.Select(BaseKey).OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(right.Types.Select(BaseKey).OrderBy(value => value,
                StringComparer.Ordinal), StringComparer.Ordinal);
    private static string BaseKey(BaseTypeSyntax type) => Canonical(type.Type);
    private static string TypeKey(TypeDeclarationSyntax type)
    {
        string ns = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString() ?? "";
        string owners = string.Join("/", type.Ancestors().OfType<TypeDeclarationSyntax>()
            .Reverse().Select(owner => owner.Identifier.ValueText));
        return ns + "|" + owners + "|" + type.Identifier.ValueText;
    }

    internal static string Canonical(SyntaxNode node)
    {
        StringBuilder value = new();
        foreach (SyntaxToken token in node.DescendantTokens())
            value.Append(token.ValueText).Append('|');
        return value.ToString();
    }

    private sealed class CallableNoiseRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, AssignmentExpressionSyntax> compounds;
        private readonly List<TemporaryPattern> temporaries;
        private readonly List<SwitchStatementSyntax> switches;
        private int switchIndex;
        public int RestoredTemporaries { get; private set; }
        public int RestoredCompoundAssignments { get; private set; }
        public int RestoredSwitchOrders { get; private set; }

        public CallableNoiseRewriter(SyntaxNode original)
        {
            compounds = original.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Where(IsCompound).GroupBy(ExpandedCompoundKey)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
            temporaries = FindTemporaryPatterns(original);
            switches = original.DescendantNodes().OfType<SwitchStatementSyntax>().ToList();
        }

        public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            AssignmentExpressionSyntax visited =
                (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node)!;
            if (!visited.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                !compounds.TryGetValue(Canonical(visited), out AssignmentExpressionSyntax? original))
                return visited;
            RestoredCompoundAssignments++;
            return original.WithTriviaFrom(visited);
        }

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            BlockSyntax visited = (BlockSyntax)base.VisitBlock(node)!;
            List<StatementSyntax> statements = visited.Statements.ToList();
            for (int index = 0; index < statements.Count; index++)
            {
                string key = Canonical(statements[index]);
                TemporaryPattern[] matches = temporaries.Where(pattern =>
                    pattern.InlinedKey == key).ToArray();
                if (matches.Length != 1)
                    continue;
                TemporaryPattern match = matches[0];
                SyntaxTriviaList leading = statements[index].GetLeadingTrivia();
                statements.RemoveAt(index);
                statements.Insert(index, match.Use.WithLeadingTrivia(leading));
                statements.Insert(index, match.Declaration.WithLeadingTrivia(leading));
                RestoredTemporaries++;
                index++;
            }
            return visited.WithStatements(SyntaxFactory.List(statements));
        }

        public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
        {
            SwitchStatementSyntax visited =
                (SwitchStatementSyntax)base.VisitSwitchStatement(node)!;
            if (switchIndex >= switches.Count)
                return visited;
            SwitchStatementSyntax original = switches[switchIndex++];
            Dictionary<string, SwitchSectionSyntax> sections = visited.Sections
                .GroupBy(SectionKey).Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
            string[] order = original.Sections.Select(SectionKey).ToArray();
            if (order.Length != visited.Sections.Count || order.Distinct().Count() != order.Length ||
                order.Any(key => !sections.ContainsKey(key)))
                return visited;
            if (order.SequenceEqual(visited.Sections.Select(SectionKey), StringComparer.Ordinal))
                return visited;
            RestoredSwitchOrders++;
            return visited.WithSections(SyntaxFactory.List(order.Select(key => sections[key])));
        }

        private static string SectionKey(SwitchSectionSyntax section) => string.Join(",",
            section.Labels.Select(Canonical));
        private static bool IsCompound(AssignmentExpressionSyntax expression) =>
            expression.Kind() is SyntaxKind.AddAssignmentExpression or
                SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or
                SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression or
                SyntaxKind.AndAssignmentExpression or SyntaxKind.ExclusiveOrAssignmentExpression or
                SyntaxKind.OrAssignmentExpression or SyntaxKind.LeftShiftAssignmentExpression or
                SyntaxKind.RightShiftAssignmentExpression;
        private static string ExpandedCompoundKey(AssignmentExpressionSyntax expression)
        {
            SyntaxKind binaryKind = expression.Kind() switch
            {
                SyntaxKind.AddAssignmentExpression => SyntaxKind.AddExpression,
                SyntaxKind.SubtractAssignmentExpression => SyntaxKind.SubtractExpression,
                SyntaxKind.MultiplyAssignmentExpression => SyntaxKind.MultiplyExpression,
                SyntaxKind.DivideAssignmentExpression => SyntaxKind.DivideExpression,
                SyntaxKind.ModuloAssignmentExpression => SyntaxKind.ModuloExpression,
                SyntaxKind.AndAssignmentExpression => SyntaxKind.BitwiseAndExpression,
                SyntaxKind.ExclusiveOrAssignmentExpression => SyntaxKind.ExclusiveOrExpression,
                SyntaxKind.OrAssignmentExpression => SyntaxKind.BitwiseOrExpression,
                SyntaxKind.LeftShiftAssignmentExpression => SyntaxKind.LeftShiftExpression,
                _ => SyntaxKind.RightShiftExpression
            };
            return Canonical(SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression, expression.Left,
                SyntaxFactory.BinaryExpression(binaryKind, expression.Left, expression.Right)));
        }

        private static List<TemporaryPattern> FindTemporaryPatterns(SyntaxNode original)
        {
            List<TemporaryPattern> result = new();
            foreach (BlockSyntax block in original.DescendantNodesAndSelf().OfType<BlockSyntax>())
            {
                for (int index = 0; index + 1 < block.Statements.Count; index++)
                {
                    if (block.Statements[index] is not LocalDeclarationStatementSyntax declaration ||
                        declaration.Declaration.Variables.Count != 1)
                        continue;
                    VariableDeclaratorSyntax variable = declaration.Declaration.Variables[0];
                    if (variable.Initializer == null)
                        continue;
                    IdentifierNameSyntax[] uses = block.Statements[index + 1].DescendantNodes()
                        .OfType<IdentifierNameSyntax>().Where(identifier =>
                            identifier.Identifier.ValueText == variable.Identifier.ValueText).ToArray();
                    int laterUses = block.Statements.Skip(index + 2).SelectMany(statement =>
                        statement.DescendantNodes().OfType<IdentifierNameSyntax>()).Count(identifier =>
                            identifier.Identifier.ValueText == variable.Identifier.ValueText);
                    if (uses.Length != 1 || laterUses != 0)
                        continue;
                    StatementSyntax inlined = block.Statements[index + 1].ReplaceNode(uses[0],
                        ParenthesizeIfNeeded(variable.Initializer.Value, uses[0]));
                    result.Add(new TemporaryPattern(declaration, block.Statements[index + 1],
                        Canonical(inlined)));
                }
            }
            return result;
        }

        private static ExpressionSyntax ParenthesizeIfNeeded(ExpressionSyntax expression,
            IdentifierNameSyntax use) => use.Parent is MemberAccessExpressionSyntax &&
                expression is CastExpressionSyntax
                ? SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia())
                : expression.WithoutTrivia();
    }

    private sealed record TemporaryPattern(LocalDeclarationStatementSyntax Declaration,
        StatementSyntax Use, string InlinedKey);
}

internal sealed class CaptureAliasNormalizer : CSharpSyntaxRewriter
{
    public int RemovedAliases { get; private set; }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        MethodDeclarationSyntax visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        return visited.Body == null ? visited : visited.WithBody(
            Normalize(visited.Body, visited.ParameterList.Parameters));
    }

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        ConstructorDeclarationSyntax visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
        return visited.Body == null ? visited : visited.WithBody(
            Normalize(visited.Body, visited.ParameterList.Parameters));
    }

    private BlockSyntax Normalize(BlockSyntax body,
        SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        HashSet<string> parameterNames = parameters.Select(parameter =>
            parameter.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        BlockSyntax current = body;
        foreach (LocalDeclarationStatementSyntax declaration in body.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>().ToArray())
        {
            if (declaration.Declaration.Variables.Count != 1)
                continue;
            VariableDeclaratorSyntax variable = declaration.Declaration.Variables[0];
            if (variable.Initializer?.Value is not IdentifierNameSyntax source ||
                !parameterNames.Contains(source.Identifier.ValueText))
                continue;
            string alias = variable.Identifier.ValueText;
            string parameter = source.Identifier.ValueText;
            if (IsWritten(body, alias) || IsWritten(body, parameter))
                continue;
            LocalDeclarationStatementSyntax? currentDeclaration = current.DescendantNodes()
                .OfType<LocalDeclarationStatementSyntax>()
                .FirstOrDefault(candidate => candidate.SpanStart == declaration.SpanStart);
            if (currentDeclaration == null)
                continue;
            IdentifierNameSyntax[] uses = current.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(identifier => identifier.Identifier.ValueText == alias &&
                    !currentDeclaration.Span.Contains(identifier.Span))
                .ToArray();
            if (uses.Length == 0 || !uses.Any(identifier => identifier.Ancestors()
                .Any(ancestor => ancestor is AnonymousMethodExpressionSyntax or
                    SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax)))
                continue;
            current = current.RemoveNode(currentDeclaration,
                SyntaxRemoveOptions.KeepExteriorTrivia)!;
            uses = current.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => identifier.Identifier.ValueText == alias)
                .ToArray();
            current = current.ReplaceNodes(uses, (identifier, _) =>
                SyntaxFactory.IdentifierName(parameter).WithTriviaFrom(identifier));
            RemovedAliases++;
        }
        return current;
    }

    private static bool IsWritten(BlockSyntax body, string name)
    {
        foreach (IdentifierNameSyntax identifier in body.DescendantNodes()
            .OfType<IdentifierNameSyntax>().Where(identifier =>
                identifier.Identifier.ValueText == name))
        {
            SyntaxNode? parent = identifier.Parent;
            if (parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier ||
                parent is PrefixUnaryExpressionSyntax prefix &&
                    (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)) ||
                parent is PostfixUnaryExpressionSyntax postfix &&
                    (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)) ||
                parent is ArgumentSyntax argument &&
                    !argument.RefOrOutKeyword.IsKind(SyntaxKind.None))
                return true;
        }
        return false;
    }
}

internal sealed class PairMaps
{
    public Dictionary<int, string> PatchedTokenRenames { get; } = new();
    public int MatchedLocals { get; private set; }
    public int OriginalOnlyLocals { get; private set; }
    public int PatchedOnlyLocals { get; private set; }

    public static PairMaps Create(SyntaxNode originalRoot, SyntaxNode patchedRoot)
    {
        PairMaps result = new();
        SemanticModel originalModel = CreateSemanticModel(originalRoot);
        SemanticModel patchedModel = CreateSemanticModel(patchedRoot);
        Dictionary<string, List<SyntaxNode>> originals = IndexCallables(originalRoot);
        Dictionary<string, List<SyntaxNode>> patched = IndexCallables(patchedRoot);
        foreach (string key in originals.Keys.Union(patched.Keys,
            StringComparer.Ordinal))
        {
            originals.TryGetValue(key, out List<SyntaxNode>? beforeList);
            patched.TryGetValue(key, out List<SyntaxNode>? afterList);
            beforeList ??= new List<SyntaxNode>();
            afterList ??= new List<SyntaxNode>();
            int common = Math.Min(beforeList.Count, afterList.Count);
            for (int index = 0; index < common; index++)
                result.PairCallable(beforeList[index], afterList[index],
                    originalModel, patchedModel);
            for (int index = common; index < beforeList.Count; index++)
                result.AddUnpaired(beforeList[index], true);
            for (int index = common; index < afterList.Count; index++)
                result.AddUnpaired(afterList[index], false);
        }
        return result;
    }

    private void PairCallable(SyntaxNode original, SyntaxNode patched,
        SemanticModel originalModel, SemanticModel patchedModel)
    {
        List<LocalDeclaration> before = LocalDeclarations(original);
        List<LocalDeclaration> after = LocalDeclarations(patched);
        List<(int Before, int After)> matches = LongestCommonSubsequence(before, after);
        HashSet<int> exactBefore = matches.Select(match => match.Before).ToHashSet();
        HashSet<int> exactAfter = matches.Select(match => match.After).ToHashSet();
        List<(int Index, LocalDeclaration Local)> remainingBefore = before
            .Select((local, index) => (index, local))
            .Where(value => !exactBefore.Contains(value.index)).ToList();
        List<(int Index, LocalDeclaration Local)> remainingAfter = after
            .Select((local, index) => (index, local))
            .Where(value => !exactAfter.Contains(value.index)).ToList();
        matches.AddRange(LongestCommonShapeSubsequence(
            remainingBefore, remainingAfter));
        matches.Sort((left, right) => left.Before.CompareTo(right.Before));
        HashSet<int> matchedBefore = new();
        HashSet<int> matchedAfter = new();
        string[] targets = after.Select(local => local.Name).ToArray();
        foreach ((int beforeIndex, int afterIndex) in matches)
        {
            string originalName = before[beforeIndex].Name;
            SyntaxNode patchedDeclaration = after[afterIndex].Node;
            if (!ParameterNames(patched).Contains(originalName,
                    StringComparer.Ordinal) &&
                !after.Select((local, index) => (local, index)).Any(value =>
                    value.index != afterIndex && value.local.Name == originalName &&
                    ScopesOverlap(patchedDeclaration, value.local.Node)))
                targets[afterIndex] = originalName;
            matchedBefore.Add(beforeIndex);
            matchedAfter.Add(afterIndex);
            MatchedLocals++;
        }
        for (int index = 0; index < before.Count; index++)
        {
            if (matchedBefore.Contains(index))
                continue;
            OriginalOnlyLocals++;
        }
        for (int index = 0; index < after.Count; index++)
        {
            if (matchedAfter.Contains(index))
                continue;
            PatchedOnlyLocals++;
        }
        for (int index = 0; index < after.Count; index++)
            if (targets[index] != after[index].Name)
                AddSymbolRename(patchedModel, patched, after[index],
                    targets[index], PatchedTokenRenames);
    }

    private void AddUnpaired(SyntaxNode callable, bool original)
    {
        List<LocalDeclaration> locals = LocalDeclarations(callable);
        if (original)
        {
            OriginalOnlyLocals += locals.Count;
        }
        else
        {
            PatchedOnlyLocals += locals.Count;
        }
    }

    private static SemanticModel CreateSemanticModel(SyntaxNode root)
    {
        CSharpCompilation compilation = CSharpCompilation.Create("NoiseNormalization")
            .AddSyntaxTrees(root.SyntaxTree);
        return compilation.GetSemanticModel(root.SyntaxTree, true);
    }

    private static void AddSymbolRename(SemanticModel model, SyntaxNode callable,
        LocalDeclaration local, string target, Dictionary<int, string> renames)
    {
        ISymbol? symbol = local.Node switch
        {
            VariableDeclaratorSyntax variable => model.GetDeclaredSymbol(variable),
            ForEachStatementSyntax statement => model.GetDeclaredSymbol(statement),
            CatchDeclarationSyntax declaration => model.GetDeclaredSymbol(declaration),
            _ => null
        };
        if (symbol == null) return;
        SyntaxToken declarationToken = DeclarationToken(local.Node);
        renames[declarationToken.SpanStart] = target;
        foreach (IdentifierNameSyntax identifier in callable.DescendantNodes()
            .OfType<IdentifierNameSyntax>())
        {
            if (SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(identifier).Symbol, symbol))
                renames[identifier.Identifier.SpanStart] = target;
        }
    }

    private static SyntaxToken DeclarationToken(SyntaxNode node) => node switch
    {
        VariableDeclaratorSyntax variable => variable.Identifier,
        ForEachStatementSyntax statement => statement.Identifier,
        CatchDeclarationSyntax declaration => declaration.Identifier,
        _ => default
    };

    private static IEnumerable<string> ParameterNames(SyntaxNode callable) =>
        callable switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList.Parameters
                .Select(parameter => parameter.Identifier.ValueText),
            SimpleLambdaExpressionSyntax lambda =>
                new[] { lambda.Parameter.Identifier.ValueText },
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters
                .Select(parameter => parameter.Identifier.ValueText),
            AnonymousMethodExpressionSyntax anonymous when anonymous.ParameterList != null =>
                anonymous.ParameterList.Parameters.Select(parameter =>
                    parameter.Identifier.ValueText),
            _ => Enumerable.Empty<string>()
        };

    private static bool ScopesOverlap(SyntaxNode left, SyntaxNode right)
    {
        SyntaxNode leftScope = DeclarationScope(left);
        SyntaxNode rightScope = DeclarationScope(right);
        return leftScope == rightScope || leftScope.AncestorsAndSelf().Contains(rightScope) ||
            rightScope.AncestorsAndSelf().Contains(leftScope);
    }

    private static SyntaxNode DeclarationScope(SyntaxNode node) =>
        node.AncestorsAndSelf().FirstOrDefault(candidate =>
            candidate is BlockSyntax or SwitchSectionSyntax or BaseMethodDeclarationSyntax or
                AccessorDeclarationSyntax or AnonymousFunctionExpressionSyntax) ?? node;

    internal static Dictionary<string, List<SyntaxNode>> IndexCallables(SyntaxNode root)
    {
        Dictionary<string, List<SyntaxNode>> result = new(StringComparer.Ordinal);
        foreach (SyntaxNode node in root.DescendantNodes().Where(IsCallable))
        {
            string key = CallableKey(node);
            if (!result.TryGetValue(key, out List<SyntaxNode>? list))
                result.Add(key, list = new List<SyntaxNode>());
            list.Add(node);
        }
        return result;
    }

    private static string CallableKey(SyntaxNode node)
    {
        string owner = string.Join("/", node.Ancestors().OfType<TypeDeclarationSyntax>()
            .Reverse().Select(type => type.Identifier.ValueText));
        if (node is MethodDeclarationSyntax method)
            return owner + "|method|" + method.Identifier.ValueText + "|" +
                method.TypeParameterList?.Parameters.Count + "|" + Parameters(method.ParameterList);
        if (node is ConstructorDeclarationSyntax constructor)
            return owner + "|ctor|" + Parameters(constructor.ParameterList);
        if (node is DestructorDeclarationSyntax)
            return owner + "|dtor";
        if (node is OperatorDeclarationSyntax op)
            return owner + "|operator|" + op.OperatorToken.ValueText + "|" + Parameters(op.ParameterList);
        if (node is ConversionOperatorDeclarationSyntax conversion)
            return owner + "|conversion|" + conversion.Type.WithoutTrivia() + "|" + Parameters(conversion.ParameterList);
        if (node is AccessorDeclarationSyntax accessor)
            return owner + "|accessor|" + MemberKey(accessor.Parent?.Parent) + "|" + accessor.Keyword.ValueText;
        return owner + "|" + node.Kind() + "|" + AnonymousOrdinal(node);
    }

    private static string Parameters(BaseParameterListSyntax list) => string.Join(",",
        list.Parameters.Select(parameter => string.Concat(parameter.Modifiers.Select(
            modifier => modifier.ValueText + " ")) + parameter.Type?.WithoutTrivia().ToString()));

    private static string MemberKey(SyntaxNode? member) => member switch
    {
        PropertyDeclarationSyntax property => "property:" + property.Identifier.ValueText,
        IndexerDeclarationSyntax indexer => "indexer:" + Parameters(indexer.ParameterList),
        EventDeclarationSyntax eventDeclaration => "event:" + eventDeclaration.Identifier.ValueText,
        _ => member?.Kind().ToString() ?? "unknown"
    };

    private static int AnonymousOrdinal(SyntaxNode node)
    {
        SyntaxNode? parentCallable = node.Ancestors().FirstOrDefault(IsCallable);
        IEnumerable<SyntaxNode> siblings = parentCallable == null
            ? node.SyntaxTree.GetRoot().DescendantNodes().Where(IsAnonymousCallable)
            : parentCallable.DescendantNodes(descendIntoChildren: child =>
                child == parentCallable || !IsCallable(child)).Where(IsAnonymousCallable);
        return siblings.TakeWhile(sibling => sibling.SpanStart < node.SpanStart).Count();
    }

    private static List<LocalDeclaration> LocalDeclarations(SyntaxNode callable)
    {
        List<(string Name, SyntaxNode Node, string Kind)> raw = new();
        foreach (VariableDeclaratorSyntax variable in callable.DescendantNodes(
            descendIntoChildren: node => node == callable || !IsCallable(node))
            .OfType<VariableDeclaratorSyntax>().Where(IsLocal))
        {
            string type = (variable.Parent as VariableDeclarationSyntax)?.Type
                .WithoutTrivia().ToString() ?? "unknown";
            raw.Add((variable.Identifier.ValueText, variable,
                (variable.Parent?.Parent?.Kind().ToString() ?? "variable") +
                ":" + type));
        }
        foreach (ForEachStatementSyntax statement in callable.DescendantNodes(
            descendIntoChildren: node => node == callable || !IsCallable(node))
            .OfType<ForEachStatementSyntax>())
            raw.Add((statement.Identifier.ValueText, statement, "foreach:" +
                statement.Type.WithoutTrivia()));
        foreach (CatchDeclarationSyntax declaration in callable.DescendantNodes(
            descendIntoChildren: node => node == callable || !IsCallable(node))
            .OfType<CatchDeclarationSyntax>().Where(value =>
                !value.Identifier.IsKind(SyntaxKind.None)))
            raw.Add((declaration.Identifier.ValueText, declaration, "catch:" +
                declaration.Type.WithoutTrivia()));
        HashSet<string> names = raw.Select(value => value.Name).ToHashSet(
            StringComparer.Ordinal);
        foreach (ParameterSyntax parameter in callable.DescendantNodes(
            descendIntoChildren: node => node == callable || !IsCallable(node))
            .OfType<ParameterSyntax>())
            names.Add(parameter.Identifier.ValueText);
        return raw.OrderBy(value => value.Node.SpanStart).Select(value =>
            new LocalDeclaration(value.Name, value.Node, value.Kind, value.Kind + "|" +
                NormalizeTokens(value.Node, names))).ToList();
    }

    private static string NormalizeTokens(SyntaxNode node, HashSet<string> localNames)
    {
        StringBuilder result = new();
        foreach (SyntaxToken token in node.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) &&
                localNames.Contains(token.ValueText))
                result.Append("$local");
            else
                result.Append(token.ValueText);
            result.Append('|');
        }
        return result.ToString();
    }

    private static List<(int Before, int After)> LongestCommonSubsequence(
        IReadOnlyList<LocalDeclaration> before, IReadOnlyList<LocalDeclaration> after)
    {
        int[,] lengths = new int[before.Count + 1, after.Count + 1];
        for (int i = before.Count - 1; i >= 0; i--)
            for (int j = after.Count - 1; j >= 0; j--)
                lengths[i, j] = before[i].Descriptor == after[j].Descriptor
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        List<(int, int)> result = new();
        for (int i = 0, j = 0; i < before.Count && j < after.Count;)
        {
            if (before[i].Descriptor == after[j].Descriptor)
            {
                result.Add((i++, j++));
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
                i++;
            else
                j++;
        }
        return result;
    }

    private static List<(int Before, int After)> LongestCommonShapeSubsequence(
        IReadOnlyList<(int Index, LocalDeclaration Local)> before,
        IReadOnlyList<(int Index, LocalDeclaration Local)> after)
    {
        int[,] lengths = new int[before.Count + 1, after.Count + 1];
        for (int i = before.Count - 1; i >= 0; i--)
            for (int j = after.Count - 1; j >= 0; j--)
                lengths[i, j] = before[i].Local.Shape == after[j].Local.Shape
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        List<(int, int)> result = new();
        for (int i = 0, j = 0; i < before.Count && j < after.Count;)
        {
            if (before[i].Local.Shape == after[j].Local.Shape)
            {
                result.Add((before[i++].Index, after[j++].Index));
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
                i++;
            else
                j++;
        }
        return result;
    }

    internal static bool IsCallable(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax ||
        IsAnonymousCallable(node);
    private static bool IsAnonymousCallable(SyntaxNode node) =>
        node is AnonymousMethodExpressionSyntax or SimpleLambdaExpressionSyntax or
            ParenthesizedLambdaExpressionSyntax;
    private static bool IsLocal(VariableDeclaratorSyntax node) =>
        node.Parent?.Parent is LocalDeclarationStatementSyntax or ForStatementSyntax or
            UsingStatementSyntax or FixedStatementSyntax;

    private readonly record struct LocalDeclaration(string Name, SyntaxNode Node,
        string Shape, string Descriptor);
}

internal sealed class LocalRenameRewriter : CSharpSyntaxRewriter
{
    private readonly Dictionary<int, string> tokenRenames;

    public LocalRenameRewriter(Dictionary<int, string> tokenRenames)
    {
        this.tokenRenames = tokenRenames;
    }

    public override SyntaxToken VisitToken(SyntaxToken token)
    {
        if (!tokenRenames.TryGetValue(token.SpanStart, out string? name))
            return base.VisitToken(token);
        return SyntaxFactory.Identifier(token.LeadingTrivia, name,
            token.TrailingTrivia);
    }
}

internal sealed class StaticInitializerNormalizer : CSharpSyntaxRewriter
{
    private readonly Dictionary<string, TypeDeclarationSyntax> originalTypes;
    public int RestoredInitializers { get; private set; }

    public StaticInitializerNormalizer(SyntaxNode originalRoot)
    {
        originalTypes = originalRoot.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .ToDictionary(TypeKey, StringComparer.Ordinal);
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) =>
        NormalizeType((ClassDeclarationSyntax)base.VisitClassDeclaration(node)!);

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) =>
        NormalizeType((StructDeclarationSyntax)base.VisitStructDeclaration(node)!);

    private T NormalizeType<T>(T node) where T : TypeDeclarationSyntax
    {
        if (!originalTypes.TryGetValue(TypeKey(node), out TypeDeclarationSyntax? original))
            return node;
        ConstructorDeclarationSyntax? staticConstructor = node.Members
            .OfType<ConstructorDeclarationSyntax>().FirstOrDefault(constructor =>
                constructor.Modifiers.Any(SyntaxKind.StaticKeyword));
        if (staticConstructor?.Body == null)
            return node;

        Dictionary<string, VariableDeclaratorSyntax> initializedVariables = original.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Where(variable => variable.Initializer != null)
            .ToDictionary(variable => variable.Identifier.ValueText,
                variable => variable, StringComparer.Ordinal);
        Dictionary<string, ExpressionStatementSyntax> assignments =
            new(StringComparer.Ordinal);
        foreach (ExpressionStatementSyntax statement in staticConstructor.Body.Statements
            .OfType<ExpressionStatementSyntax>())
        {
            if (statement.Expression is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                continue;
            string? fieldName = assignment.Left switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                _ => null
            };
            if (fieldName != null && initializedVariables.TryGetValue(fieldName,
                out VariableDeclaratorSyntax? initializedVariable) &&
                Equivalent(initializedVariable.Initializer!.Value, assignment.Right))
                assignments[fieldName] = statement;
        }
        if (assignments.Count == 0)
            return node;

        Dictionary<FieldDeclarationSyntax, FieldDeclarationSyntax> fieldReplacements = new();
        foreach (FieldDeclarationSyntax field in node.Members.OfType<FieldDeclarationSyntax>())
        {
            FieldDeclarationSyntax changed = field;
            foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
            {
                if (variable.Initializer != null ||
                    !assignments.ContainsKey(variable.Identifier.ValueText))
                    continue;
                changed = changed.ReplaceNode(variable,
                    initializedVariables[variable.Identifier.ValueText]);
                RestoredInitializers++;
            }
            if (!ReferenceEquals(changed, field))
                fieldReplacements[field] = changed;
        }
        TypeDeclarationSyntax updated = node.ReplaceNodes(fieldReplacements.Keys,
            (field, _) => fieldReplacements[field]);

        staticConstructor = updated.Members.OfType<ConstructorDeclarationSyntax>()
            .First(constructor => constructor.Modifiers.Any(SyntaxKind.StaticKeyword));
        SyntaxList<StatementSyntax> remaining = SyntaxFactory.List(
            staticConstructor.Body!.Statements.Where(statement =>
                !assignments.Values.Any(removed =>
                    removed.WithoutTrivia().IsEquivalentTo(statement.WithoutTrivia()))));
        bool originalHasStaticConstructor = original.Members
            .OfType<ConstructorDeclarationSyntax>().Any(constructor =>
                constructor.Modifiers.Any(SyntaxKind.StaticKeyword));
        if (remaining.Count == 0 && !originalHasStaticConstructor)
            return (T)updated.RemoveNode(staticConstructor,
                SyntaxRemoveOptions.KeepExteriorTrivia)!;
        return (T)updated.ReplaceNode(staticConstructor,
            staticConstructor.WithBody(staticConstructor.Body.WithStatements(remaining)));
    }

    private static bool Equivalent(ExpressionSyntax left, ExpressionSyntax right) =>
        left.WithoutTrivia().IsEquivalentTo(right.WithoutTrivia());

    private static string TypeKey(TypeDeclarationSyntax type)
    {
        string namespaceName = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString() ?? "";
        string owners = string.Join("/", type.Ancestors()
            .OfType<TypeDeclarationSyntax>().Reverse()
            .Select(owner => owner.Identifier.ValueText));
        return namespaceName + "|" + owners + "|" + type.Identifier.ValueText;
    }
}
