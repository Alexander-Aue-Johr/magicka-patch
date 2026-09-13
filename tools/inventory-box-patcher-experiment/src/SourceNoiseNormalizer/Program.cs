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
        report.WriteLine("file,original_locals,patched_locals");
        foreach (string relativePath in common)
        {
            NormalizedSource before = Normalize(File.ReadAllText(originals[relativePath]));
            NormalizedSource after = Normalize(File.ReadAllText(patched[relativePath]));
            Write(normalizedOriginalRoot, relativePath, before.Text);
            Write(normalizedPatchedRoot, relativePath, after.Text);
            report.WriteLine(Csv(relativePath) + "," +
                before.LocalCount.ToString(CultureInfo.InvariantCulture) + "," +
                after.LocalCount.ToString(CultureInfo.InvariantCulture));
        }

        Console.WriteLine("normalized_pairs=" + common.Length);
        return 0;
    }

    private static NormalizedSource Normalize(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp3))
            .GetRoot();
        LocalRenameRewriter rewriter = new();
        SyntaxNode normalized = rewriter.Visit(root)!;
        return new NormalizedSource(normalized.ToFullString(), rewriter.LocalCount);
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

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private readonly record struct NormalizedSource(string Text, int LocalCount);
}

internal sealed class LocalRenameRewriter : CSharpSyntaxRewriter
{
    private readonly Stack<Dictionary<string, string>> methodMaps = new();
    public int LocalCount { get; private set; }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        return VisitMethodLike(node, () => base.VisitMethodDeclaration(node));
    }

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        return VisitMethodLike(node, () => base.VisitConstructorDeclaration(node));
    }

    public override SyntaxNode? VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        return VisitMethodLike(node, () => base.VisitDestructorDeclaration(node));
    }

    public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        return VisitMethodLike(node, () => base.VisitOperatorDeclaration(node));
    }

    public override SyntaxNode? VisitConversionOperatorDeclaration(
        ConversionOperatorDeclarationSyntax node)
    {
        return VisitMethodLike(node,
            () => base.VisitConversionOperatorDeclaration(node));
    }

    public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        return VisitMethodLike(node, () => base.VisitAccessorDeclaration(node));
    }

    public override SyntaxNode? VisitAnonymousMethodExpression(
        AnonymousMethodExpressionSyntax node)
    {
        return VisitMethodLike(node,
            () => base.VisitAnonymousMethodExpression(node));
    }

    public override SyntaxNode? VisitSimpleLambdaExpression(
        SimpleLambdaExpressionSyntax node)
    {
        return VisitMethodLike(node,
            () => base.VisitSimpleLambdaExpression(node));
    }

    public override SyntaxNode? VisitParenthesizedLambdaExpression(
        ParenthesizedLambdaExpressionSyntax node)
    {
        return VisitMethodLike(node,
            () => base.VisitParenthesizedLambdaExpression(node));
    }

    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        if (methodMaps.Count == 0 || !IsLocal(node))
            return base.VisitVariableDeclarator(node);
        Dictionary<string, string> map = methodMaps.Peek();
        string oldName = node.Identifier.ValueText;
        if (!map.TryGetValue(oldName, out string? newName))
        {
            newName = "local_" + map.Count.ToString("D4", CultureInfo.InvariantCulture);
            map.Add(oldName, newName);
            LocalCount++;
        }
        VariableDeclaratorSyntax renamed = node.WithIdentifier(
            SyntaxFactory.Identifier(node.Identifier.LeadingTrivia, newName,
                node.Identifier.TrailingTrivia));
        return base.VisitVariableDeclarator(renamed);
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (methodMaps.Count == 0)
            return base.VisitForEachStatement(node);
        ForEachStatementSyntax renamed = node.WithIdentifier(
            RenameDeclaration(node.Identifier));
        return base.VisitForEachStatement(renamed);
    }

    public override SyntaxNode? VisitCatchDeclaration(CatchDeclarationSyntax node)
    {
        if (methodMaps.Count == 0 || node.Identifier.IsKind(SyntaxKind.None))
            return base.VisitCatchDeclaration(node);
        CatchDeclarationSyntax renamed = node.WithIdentifier(
            RenameDeclaration(node.Identifier));
        return base.VisitCatchDeclaration(renamed);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (methodMaps.Count == 0)
            return base.VisitIdentifierName(node);
        foreach (Dictionary<string, string> map in methodMaps)
        {
            if (!map.TryGetValue(node.Identifier.ValueText, out string? replacement))
                continue;
            return node.WithIdentifier(SyntaxFactory.Identifier(
                node.Identifier.LeadingTrivia, replacement,
                node.Identifier.TrailingTrivia));
        }
        return base.VisitIdentifierName(node);
    }

    private T VisitMethodLike<T>(T node, Func<SyntaxNode?> visit)
        where T : SyntaxNode
    {
        methodMaps.Push(new Dictionary<string, string>(StringComparer.Ordinal));
        try
        {
            return (T)visit()!;
        }
        finally
        {
            methodMaps.Pop();
        }
    }

    private SyntaxToken RenameDeclaration(SyntaxToken identifier)
    {
        Dictionary<string, string> map = methodMaps.Peek();
        string oldName = identifier.ValueText;
        if (!map.TryGetValue(oldName, out string? newName))
        {
            newName = "local_" + map.Count.ToString("D4", CultureInfo.InvariantCulture);
            map.Add(oldName, newName);
            LocalCount++;
        }
        return SyntaxFactory.Identifier(identifier.LeadingTrivia, newName,
            identifier.TrailingTrivia);
    }

    private static bool IsLocal(VariableDeclaratorSyntax node)
    {
        return node.Parent?.Parent is LocalDeclarationStatementSyntax or
            ForStatementSyntax or UsingStatementSyntax or FixedStatementSyntax;
    }
}
