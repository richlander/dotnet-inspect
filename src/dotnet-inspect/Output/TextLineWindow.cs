namespace DotnetInspector.Output;

internal static class TextLineWindow
{
    public static string Head(string content, int count)
    {
        if (count < 1 || content.Length == 0)
            return string.Empty;

        var lines = GetLines(content);
        return count >= lines.Count
            ? content
            : content[..lines[count - 1].End];
    }

    public static string Tail(string content, int count)
    {
        if (count < 1 || content.Length == 0)
            return string.Empty;

        var lines = GetLines(content);
        return count >= lines.Count
            ? content
            : content[lines[^count].Start..];
    }

    private static List<(int Start, int End)> GetLines(string content)
    {
        List<(int Start, int End)> lines = [];
        var start = 0;
        for (var i = 0; i < content.Length;)
        {
            if (content[i] is not ('\r' or '\n'))
            {
                i++;
                continue;
            }

            var end = i + 1;
            if (content[i] == '\r'
                && end < content.Length
                && content[end] == '\n')
            {
                end++;
            }

            lines.Add((start, end));
            start = end;
            i = end;
        }

        if (start < content.Length)
            lines.Add((start, content.Length));

        return lines;
    }
}
