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
            if (MarkdownScan.IsCodeFence(lines[i]))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
                continue;

            if (!MarkdownScan.IsTableLine(lines[i]) || !MarkdownScan.IsSeparatorLine(lines[i + 1]))
                continue;

            i += 2;
            while (i < lines.Length && MarkdownScan.IsTableLine(lines[i]))
            {
                if (!MarkdownScan.IsSeparatorLine(lines[i]))
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
}
