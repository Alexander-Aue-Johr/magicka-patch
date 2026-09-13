using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

return SourceNoiseNormalizer.Run(args);

internal static class SourceNoiseNormalizer
{
    public static int Run(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine(
                "usage: SourceNoiseNormalizer <original-root> <patched-root> " +
                "<normalized-original-root> <normalized-patched-root> <report.csv>");
            return 2;
        }

        string originalRoot = FullDirectory(args[0]);
        string patchedRoot = FullDirectory(args[1]);
        string normalizedOriginalRoot = PrepareOutput(args[2]);
        string normalizedPatchedRoot = PrepareOutput(args[3]);
        string reportPath = Path.GetFullPath(args[4]);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        Dictionary<string, string> originals = FilesByRelativePath(originalRoot);
        Dictionary<string, string> patched = FilesByRelativePath(patchedRoot);
        string[] common = originals.Keys.Intersect(patched.Keys,
            StringComparer.OrdinalIgnoreCase).OrderBy(path => path,
            StringComparer.OrdinalIgnoreCase).ToArray();

        using StreamWriter report = new(reportPath, false, new UTF8Encoding(false));
        report.WriteLine("file,matched_locals,original_only_locals,patched_only_locals,restored_initializers,removed_capture_aliases");
        foreach (string relativePath in common)
        {
            SourcePair pair = NormalizePair(File.ReadAllText(originals[relativePath]),
                File.ReadAllText(patched[relativePath]));
            Write(normalizedOriginalRoot, relativePath, pair.Original);
            Write(normalizedPatchedRoot, relativePath, pair.Patched);
            report.WriteLine(Csv(relativePath) + "," + pair.MatchedLocals + "," +
                pair.OriginalOnlyLocals + "," + pair.PatchedOnlyLocals + "," +
                pair.RestoredInitializers + "," + pair.RemovedCaptureAliases);
        }
        Console.WriteLine("normalized_pairs=" + common.Length);
        return 0;
    }

    private static SourcePair NormalizePair(string original, string patched)
    {
        SyntaxNode originalRoot = Parse(original);
        SyntaxNode patchedRoot = Parse(patched);
        PairMaps maps = PairMaps.Create(originalRoot, patchedRoot);
        SyntaxNode normalizedOriginal = originalRoot;
        SyntaxNode normalizedPatched = new LocalRenameRewriter(
            maps.PatchedTokenRenames).Visit(patchedRoot)!;
        CaptureAliasNormalizer aliasNormalizer = new();
        normalizedPatched = aliasNormalizer.Visit(normalizedPatched)!;
        StaticInitializerNormalizer initializerNormalizer = new(
            normalizedOriginal);
        normalizedPatched = initializerNormalizer.Visit(normalizedPatched)!;
        return new SourcePair(normalizedOriginal.ToFullString(),
            normalizedPatched.ToFullString(), maps.MatchedLocals,
            maps.OriginalOnlyLocals, maps.PatchedOnlyLocals,
            initializerNormalizer.RestoredInitializers,
            aliasNormalizer.RemovedAliases);
    }

    private static SyntaxNode Parse(string source)
    {
        return CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3))
            .GetRoot();
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
        int RestoredInitializers, int RemovedCaptureAliases);
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

    private static Dictionary<string, List<SyntaxNode>> IndexCallables(SyntaxNode root)
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
