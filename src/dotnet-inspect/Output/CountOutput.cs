namespace DotnetInspector.Output;

/// <summary>
/// Helpers for the --count output mode.
/// </summary>
public static class CountOutput
{
    public const string SingleSectionRequiredMessage = "Error: --count requires -S/--select to match exactly one section.";

    public static bool ValidateSingleSection(HashSet<string>? includeSections)
    {
        if (includeSections is { Count: 1 })
            return true;

        Console.Error.WriteLine(SingleSectionRequiredMessage);
        return false;
    }

    public static int CountMarkdownTableRows(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var count = 0;

        var inCodeFence = false;

        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (IsCodeFence(lines[i]))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
                continue;

            if (!IsTableLine(lines[i]) || !IsSeparatorLine(lines[i + 1]))
                continue;

            i += 2;
            while (i < lines.Length && IsTableLine(lines[i]))
            {
                if (!IsSeparatorLine(lines[i]))
                    count++;
                i++;
            }
        }

        return count;
    }

    public static void WriteCountFromMarkdown(string markdown)
    {
        Console.WriteLine(CountMarkdownTableRows(markdown));
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
