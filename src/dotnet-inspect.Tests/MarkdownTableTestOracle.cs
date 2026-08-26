namespace DotnetInspector.Tests;

internal static class MarkdownTableTestOracle
{
    internal static int CountRows(string markdown) =>
        EnumerateTables(markdown).Sum(table => table.Rows);

    internal static Dictionary<string, int> CountRowsBySection(string markdown) =>
        EnumerateTables(markdown)
            .Where(table => table.Section is not null)
            .GroupBy(table => table.Section!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(table => table.Rows),
                StringComparer.Ordinal);

    private static IEnumerable<(string? Section, int Rows)> EnumerateTables(
        string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var inCodeFence = false;
        string? section = null;

        for (var i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
                continue;

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = line[3..].Trim();
                continue;
            }

            if (i + 1 >= lines.Length
                || !IsTableLine(line)
                || !IsSeparatorLine(lines[i + 1]))
            {
                continue;
            }

            i += 2;
            var rows = 0;
            while (i < lines.Length && IsTableLine(lines[i]))
            {
                if (!IsSeparatorLine(lines[i]))
                    rows++;
                i++;
            }
            i--;
            yield return (section, rows);
        }
    }

    private static bool IsTableLine(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Length >= 2
            && trimmed.StartsWith('|')
            && trimmed.EndsWith('|');
    }

    private static bool IsSeparatorLine(string line)
    {
        if (!IsTableLine(line))
            return false;

        var cells = line.Trim().Trim('|').Split(
            '|',
            StringSplitOptions.TrimEntries);
        return cells.Length > 0 && cells.All(cell =>
            cell.Length > 0
            && cell.Any(character => character == '-')
            && cell.All(character => character is '-' or ':' or ' '));
    }
}
