using DotnetInspector.Options;

namespace DotnetInspector.Services;

public static class MarkdownContent
{
    public static string ApplyScope(string content, PackageFileContentScope scope)
    {
        if (scope == PackageFileContentScope.Full)
            return content;

        if (!TryFindYamlFrontmatter(content, out var frontmatterEnd, out var bodyStart))
            return scope == PackageFileContentScope.Frontmatter ? "" : content;

        return scope == PackageFileContentScope.Frontmatter
            ? content[..frontmatterEnd]
            : content[bodyStart..];
    }

    public static IReadOnlyDictionary<string, string> ParseYamlFrontmatter(string content)
    {
        if (!TryFindYamlFrontmatter(content, out var frontmatterEnd, out _))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var firstLineStart = content.Length > 0 && content[0] == '\uFEFF' ? 1 : 0;
        var firstLineEnd = FindLineEnd(content, firstLineStart);
        var lineStart = NextLineStart(content, firstLineEnd);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (lineStart < frontmatterEnd)
        {
            var lineEnd = FindLineEnd(content, lineStart);
            var line = content[lineStart..lineEnd].Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    var key = line[..colon].Trim();
                    var value = TrimYamlScalar(line[(colon + 1)..].Trim());
                    if (key.Length > 0)
                        values[key] = value;
                }
            }

            lineStart = NextLineStart(content, lineEnd);
        }

        return values;
    }

    private static string TrimYamlScalar(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\''))
            {
                return value[1..^1];
            }
        }

        return value;
    }

    private static bool TryFindYamlFrontmatter(string content, out int frontmatterEnd, out int bodyStart)
    {
        frontmatterEnd = 0;
        bodyStart = 0;
        if (content.Length == 0)
            return false;

        var firstLineStart = content[0] == '\uFEFF' ? 1 : 0;
        var firstLineEnd = FindLineEnd(content, firstLineStart);
        if (!LineEquals(content, firstLineStart, firstLineEnd, "---"))
            return false;

        var lineStart = NextLineStart(content, firstLineEnd);
        while (lineStart < content.Length)
        {
            var lineEnd = FindLineEnd(content, lineStart);
            if (LineEquals(content, lineStart, lineEnd, "---"))
            {
                frontmatterEnd = lineEnd;
                bodyStart = NextLineStart(content, lineEnd);
                return true;
            }

            lineStart = NextLineStart(content, lineEnd);
        }

        return false;
    }

    private static int FindLineEnd(string content, int start)
    {
        var newline = content.IndexOf('\n', start);
        return newline >= 0 ? newline : content.Length;
    }

    private static int NextLineStart(string content, int lineEnd)
        => lineEnd < content.Length ? lineEnd + 1 : lineEnd;

    private static bool LineEquals(string content, int lineStart, int lineEnd, string value)
    {
        if (lineEnd > lineStart && content[lineEnd - 1] == '\r')
            lineEnd--;
        return content.AsSpan(lineStart, lineEnd - lineStart).SequenceEqual(value);
    }
}
