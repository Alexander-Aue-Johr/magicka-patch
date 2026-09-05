using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

return CommentStripper.Run(args);

internal static class CommentStripper
{
    public static int Run(string[] directoryArguments)
    {
        if (directoryArguments.Length == 0)
        {
            Console.Error.WriteLine("usage: SourceCommentStripper <directory> [directory ...]");
            return 2;
        }

        foreach (string argument in directoryArguments)
            StripDirectory(Path.GetFullPath(argument));

        return 0;
    }

    private static void StripDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        int changedFiles = 0;
        int removedComments = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            int commentsInFile = StripFile(path);
            if (commentsInFile == 0)
                continue;

            changedFiles++;
            removedComments += commentsInFile;
        }

        Console.WriteLine(
            $"comment_stripping={directory}|files={changedFiles}|comments={removedComments}");
    }

    private static int StripFile(string path)
    {
        EncodedSource source = EncodedSource.Read(path);
        SyntaxNode syntaxRoot = CSharpSyntaxTree
            .ParseText(source.Text, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))
            .GetRoot();
        SyntaxTrivia[] comments = syntaxRoot
            .DescendantTrivia(descendIntoTrivia: false)
            .Where(IsComment)
            .OrderBy(trivia => trivia.SpanStart)
            .ToArray();

        if (comments.Length == 0)
            return 0;

        string strippedText = RemoveCommentsButKeepLineBreaks(source.Text, comments);
        source.Write(path, strippedText);
        return comments.Length;
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }

    private static string RemoveCommentsButKeepLineBreaks(
        string text,
        IEnumerable<SyntaxTrivia> comments)
    {
        StringBuilder result = new(text.Length);
        int position = 0;
        foreach (SyntaxTrivia comment in comments)
        {
            result.Append(text, position, comment.SpanStart - position);
            result.Append(ExtractLineBreaks(text.Substring(comment.SpanStart, comment.Span.Length)));
            position = comment.Span.End;
        }

        result.Append(text, position, text.Length - position);
        return result.ToString();
    }

    private static string ExtractLineBreaks(string comment)
    {
        StringBuilder lineBreaks = new();
        for (int index = 0; index < comment.Length; index++)
        {
            if (comment[index] == '\r')
            {
                lineBreaks.Append('\r');
                if (index + 1 < comment.Length && comment[index + 1] == '\n')
                {
                    lineBreaks.Append('\n');
                    index++;
                }
            }
            else if (comment[index] == '\n')
            {
                lineBreaks.Append('\n');
            }
        }

        return lineBreaks.Length == 0 ? " " : lineBreaks.ToString();
    }
}

internal sealed class EncodedSource
{
    public string Text { get; }

    private readonly Encoding encoding;
    private readonly byte[] preamble;

    private EncodedSource(string text, Encoding encoding, byte[] preamble)
    {
        Text = text;
        this.encoding = encoding;
        this.preamble = preamble;
    }

    public static EncodedSource Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return Decode(bytes, new UTF8Encoding(false, true), 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
            return Decode(bytes, new UTF32Encoding(false, false, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
            return Decode(bytes, new UTF32Encoding(true, false, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            return Decode(bytes, new UnicodeEncoding(false, false, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            return Decode(bytes, new UnicodeEncoding(true, false, true), 2);
        return Decode(bytes, new UTF8Encoding(false, true), 0);
    }

    public void Write(string path, string text)
    {
        byte[] body = encoding.GetBytes(text);
        byte[] output = new byte[preamble.Length + body.Length];
        preamble.CopyTo(output, 0);
        body.CopyTo(output, preamble.Length);

        string temporaryPath = path + ".comment-strip-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, output);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static EncodedSource Decode(byte[] bytes, Encoding encoding, int preambleLength)
    {
        return new EncodedSource(
            encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength),
            encoding,
            bytes.AsSpan(0, preambleLength).ToArray());
    }
}
