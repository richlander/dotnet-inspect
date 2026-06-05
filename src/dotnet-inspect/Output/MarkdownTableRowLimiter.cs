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
            if (IsCodeFence(line))
            {
                inCodeFence = !inCodeFence;
                output.Add(line);
                continue;
            }

            if (inCodeFence || !IsTableLine(line) || i + 1 >= lines.Length || !IsSeparatorLine(lines[i + 1]))
            {
                output.Add(line);
                continue;
            }

            output.Add(line);
            output.Add(lines[++i]);

            var rows = 0;
            while (i + 1 < lines.Length && IsTableLine(lines[i + 1]))
            {
                i++;
                if (IsSeparatorLine(lines[i]))
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

    private static bool IsTableLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('|') && trimmed.EndsWith('|');
    }

    private static bool IsCodeFence(string line)
        => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    private static bool IsSeparatorLine(string line)
    {
        if (!IsTableLine(line))
            return false;

        var cells = line.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries);
        return cells.Length > 0 && cells.All(IsSeparatorCell);
    }

    private static bool IsSeparatorCell(string cell)
        => cell.Length > 0
           && cell.Any(c => c == '-')
           && cell.All(c => c is '-' or ':' or ' ');
}
