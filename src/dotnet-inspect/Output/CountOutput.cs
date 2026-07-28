using System.Globalization;

namespace DotnetInspector.Output;

/// <summary>
/// Helpers for the --count output mode.
/// </summary>
public static class CountOutput
{
    public const string SingleSectionRequiredMessage = "Error: --count requires -S/--select to match exactly one section.";
    public const string SectionRequiredMessage = "Error: --count requires -S/--select to match at least one section.";

    public static bool ValidateSingleSection(HashSet<string>? includeSections)
    {
        if (includeSections is { Count: 1 })
            return true;

        Console.Error.WriteLine(SingleSectionRequiredMessage);
        return false;
    }

    /// <summary>
    /// Validates that <c>--count</c> has selected at least one section. Unlike
    /// <see cref="ValidateSingleSection"/> this permits multi-section selection (e.g. a
    /// category such as <c>@Performance</c>), which renders a per-section count map.
    /// </summary>
    public static bool ValidateSectionsSelected(HashSet<string>? includeSections)
    {
        if (includeSections is { Count: >= 1 })
            return true;

        Console.Error.WriteLine(SectionRequiredMessage);
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

    /// <summary>
    /// Writes a single count and records that the <c>--count</c> projection was honored.
    /// Count-emitting paths should route through this rather than writing to the console
    /// directly, so the payload-projection audit can tell a rendered count from a dropped one.
    /// </summary>
    public static void WriteCount(int count)
    {
        ProjectionAudit.MarkHonored();
        // Invariant: a count is machine-readable output, so it must not pick up
        // culture-specific digits or grouping from the ambient locale.
        Console.WriteLine(count.ToString(CultureInfo.InvariantCulture));
    }

    public static void WriteCountFromMarkdown(string markdown)
    {
        ProjectionAudit.MarkHonored();
        Console.WriteLine(CountMarkdownTableRows(markdown));
    }

    /// <summary>
    /// Counts markdown table rows attributed to the nearest preceding <c>## Section</c> heading.
    /// Empty (absent) sections do not appear in the returned map.
    /// </summary>
    public static Dictionary<string, int> CountMarkdownTableRowsBySection(string markdown)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var inCodeFence = false;
        string? currentSection = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (MarkdownScan.IsCodeFence(line))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
                continue;

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentSection = line[3..].Trim();
                continue;
            }

            if (currentSection is null || i + 1 >= lines.Length)
                continue;

            if (!MarkdownScan.IsTableLine(line) || !MarkdownScan.IsSeparatorLine(lines[i + 1]))
                continue;

            i += 2;
            var rows = 0;
            while (i < lines.Length && MarkdownScan.IsTableLine(lines[i]))
            {
                if (!MarkdownScan.IsSeparatorLine(lines[i]))
                    rows++;
                i++;
            }
            i--;

            counts[currentSection] = counts.GetValueOrDefault(currentSection) + rows;
        }

        return counts;
    }

    /// <summary>
    /// Emits a per-section count map (<c>| Section | Count |</c>) over <paramref name="orderedSections"/>,
    /// reporting 0 for sections absent from the rendered markdown.
    /// </summary>
    public static void WriteCountMapFromMarkdown(string markdown, IReadOnlyList<string> orderedSections)
    {
        ProjectionAudit.MarkHonored();
        var counts = CountMarkdownTableRowsBySection(markdown);
        Console.WriteLine("| Section | Count |");
        Console.WriteLine("| ------- | ----- |");
        foreach (var section in orderedSections)
            Console.WriteLine($"| {section} | {counts.GetValueOrDefault(section)} |");
    }
}
