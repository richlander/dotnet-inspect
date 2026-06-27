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
            var indent = CountIndent(content, lineStart, lineEnd);
            var line = content[lineStart..lineEnd].Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    var key = line[..colon].Trim();
                    var rawValue = line[(colon + 1)..].Trim();
                    if (key.Length > 0)
                    {
                        if (TryGetBlockScalarStyle(rawValue, out var folded))
                        {
                            var blockStart = NextLineStart(content, lineEnd);
                            values[key] = ReadBlockScalar(content, frontmatterEnd, blockStart, indent, folded, out lineStart);
                            continue;
                        }

                        values[key] = TrimYamlScalar(rawValue);
                    }
                }
            }

            lineStart = NextLineStart(content, lineEnd);
        }

        return values;
    }

    /// <summary>
    /// Recognizes a YAML block scalar header (<c>|</c> literal or <c>&gt;</c> folded), optionally with
    /// chomping/indentation indicators (e.g. <c>|-</c>, <c>&gt;-</c>, <c>|+</c>, <c>&gt;2</c>). Returns true
    /// for both styles; <paramref name="folded"/> is true for <c>&gt;</c>.
    /// </summary>
    private static bool TryGetBlockScalarStyle(string value, out bool folded)
    {
        folded = false;
        if (value.Length == 0 || (value[0] != '|' && value[0] != '>'))
            return false;

        // Everything after the indicator must be chomping/indent indicators (-, +, digits) only,
        // ignoring a trailing comment.
        var rest = value[1..];
        var commentStart = rest.IndexOf('#');
        if (commentStart >= 0)
            rest = rest[..commentStart];
        foreach (var ch in rest.Trim())
        {
            if (ch is not ('-' or '+') && !char.IsDigit(ch))
                return false;
        }

        folded = value[0] == '>';
        return true;
    }

    /// <summary>
    /// Reads the body of a YAML block scalar. Lines indented more than <paramref name="parentIndent"/>
    /// (and blank lines) belong to the block; the first line at or below the parent indent ends it.
    /// Folded blocks join wrapped text lines with a single space and blank lines with a newline; literal
    /// blocks preserve newlines. Trailing whitespace is trimmed (sufficient for the table/field consumers).
    /// <paramref name="next"/> receives the line start where parsing should resume.
    /// </summary>
    private static string ReadBlockScalar(string content, int end, int start, int parentIndent, bool folded, out int next)
    {
        var lines = new List<string>();
        var blockIndent = -1;
        var lineStart = start;
        while (lineStart < end)
        {
            var lineEnd = FindLineEnd(content, lineStart);
            var rawEnd = lineEnd > lineStart && content[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            var isBlank = IsBlank(content, lineStart, rawEnd);
            if (!isBlank)
            {
                var indent = CountIndent(content, lineStart, rawEnd);
                if (indent <= parentIndent)
                    break;

                if (blockIndent < 0)
                    blockIndent = indent;

                var stripStart = lineStart + Math.Min(blockIndent, indent);
                lines.Add(content[stripStart..rawEnd]);
            }
            else
            {
                lines.Add("");
            }

            lineStart = NextLineStart(content, lineEnd);
        }

        next = lineStart;

        // Drop trailing blank lines.
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        if (folded)
        {
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length == 0)
                {
                    builder.Append('\n');
                }
                else
                {
                    if (builder.Length > 0 && builder[^1] != '\n')
                        builder.Append(' ');
                    builder.Append(lines[i]);
                }
            }

            return builder.ToString();
        }

        return string.Join('\n', lines);
    }

    private static int CountIndent(string content, int start, int end)
    {
        var i = start;
        while (i < end && content[i] == ' ')
            i++;
        return i - start;
    }

    private static bool IsBlank(string content, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (content[i] is not (' ' or '\t'))
                return false;
        }

        return true;
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
