namespace DotnetInspector.Output;

internal static class MarkdownTableRowLimiter
{
    public static string Apply(string markdown, RowWindow? window)
    {
        if (window is not { Count: >= 0 } limit)
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

            // Collect the table's data rows (separator lines pass through), then
            // keep the leading or trailing window before emitting.
            List<string> dataRows = [];
            List<string> separators = [];
            while (i + 1 < lines.Length && MarkdownScan.IsTableLine(lines[i + 1]))
            {
                i++;
                if (MarkdownScan.IsSeparatorLine(lines[i]))
                {
                    separators.Add(lines[i]);
                    continue;
                }

                dataRows.Add(lines[i]);
            }

            foreach (var kept in Window(dataRows, limit))
                output.Add(kept);
            output.AddRange(separators);
        }

        return string.Join('\n', output);
    }

    private static IEnumerable<string> Window(List<string> rows, RowWindow limit) =>
        limit.FromEnd
            ? rows.Skip(Math.Max(0, rows.Count - limit.Count))
            : rows.Take(limit.Count);
}
