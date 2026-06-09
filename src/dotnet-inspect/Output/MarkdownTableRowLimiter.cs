namespace DotnetInspector.Output;

internal static class MarkdownTableRowLimiter
{
    public static string Apply(string markdown, int? maxRows)
    {
        if (maxRows is null or < 0)
            return markdown;

        var normalized = markdown.ReplaceLineEndings("\n");
        var lines = normalized.Split('\n');
        List<string> output = new(lines.Length);
        var inCodeFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (MarkdownScan.IsCodeFence(line))
            {
                inCodeFence = !inCodeFence;
                output.Add(line);
                continue;
            }

            if (inCodeFence || !MarkdownScan.IsTableLine(line) || i + 1 >= lines.Length || !MarkdownScan.IsSeparatorLine(lines[i + 1]))
            {
                output.Add(line);
                continue;
            }

            output.Add(line);
            output.Add(lines[++i]);

            var rows = 0;
            while (i + 1 < lines.Length && MarkdownScan.IsTableLine(lines[i + 1]))
            {
                i++;
                if (MarkdownScan.IsSeparatorLine(lines[i]))
                {
                    output.Add(lines[i]);
                    continue;
                }

                if (rows < maxRows.Value)
                {
                    output.Add(lines[i]);
                    rows++;
                }
            }
        }

        return string.Join('\n', output);
    }
}
