using System.Globalization;

namespace DotnetInspector.Output;

/// <summary>
/// Helpers for the --count output mode.
/// </summary>
public static class CountOutput
{
    public const string SingleSectionRequiredMessage = "--count requires -S/--select to match exactly one section.";
    public const string SectionRequiredMessage = "--count requires -S/--select to match at least one section.";

    public static bool ValidateSingleSection(HashSet<string>? includeSections)
    {
        if (includeSections is { Count: 1 })
            return true;

        CommandError.Write(SingleSectionRequiredMessage);
        return false;
    }

    /// <summary>
    /// Validates that <c>--count</c> has selected at least one section. Unlike
    /// <see cref="ValidateSingleSection"/> this permits multi-section selection (e.g. a
    /// category such as <c>@Performance</c>), which renders a per-section count map.
    /// </summary>
    /// <param name="fixedOverview">
    /// Whether bare <c>-S</c> selected the fixed overview. That route carries its selection as a
    /// flag rather than as <paramref name="includeSections"/>, so reading the include set alone
    /// reports a selection the user did make as no selection at all (#3547).
    /// </param>
    public static bool ValidateSectionsSelected(HashSet<string>? includeSections, bool fixedOverview)
    {
        if (includeSections is { Count: >= 1 } || fixedOverview)
            return true;

        CommandError.Write(SectionRequiredMessage);
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
    public static void WriteCount(int count) => WriteCount(count, null);

    /// <summary>
    /// Writes a count to <paramref name="outputPath"/>, or to stdout when it is null. A count is
    /// still the command's payload, so --out has to apply to it as it does to a full render.
    /// </summary>
    public static void WriteCount(
        int count,
        string? outputPath,
        bool applyLineWindow = false)
    {
        // Invariant: a count is machine-readable output, so it must not pick up
        // culture-specific digits or grouping from the ambient locale.
        WriteCountResult(
            count.ToString(CultureInfo.InvariantCulture),
            outputPath,
            applyLineWindow);
    }

    /// <summary>
    /// Writes an already-rendered scalar or per-section count to <paramref name="outputPath"/>,
    /// or to stdout when it is null.
    /// </summary>
    public static void WriteCountResult(
        string result,
        string? outputPath,
        bool applyLineWindow = false)
    {
        ProjectionAudit.MarkHonored(ProjectionAudit.Count);
        var text = result.TrimEnd('\r', '\n') + '\n';
        if (string.IsNullOrEmpty(outputPath))
            Console.Write(text);
        else
            OutputPathWriter.Write(outputPath, text, applyLineWindow);
    }

    public static void WriteCountFromMarkdown(string markdown, string? outputPath = null)
    {
        WriteCount(CountMarkdownTableRows(markdown), outputPath);
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
    /// Renders a per-section count map (<c>| Section | Count |</c>) over
    /// <paramref name="orderedSections"/>, reporting 0 for sections absent from the rendered
    /// markdown. A category selection counts every member, including empty ones, which is why
    /// the zero rows are kept rather than filtered.
    /// </summary>
    public static string RenderCountMapFromMarkdown(string markdown, IReadOnlyList<string> orderedSections)
    {
        var counts = CountMarkdownTableRowsBySection(markdown);
        return RenderCountMap(counts, orderedSections);
    }

    /// <summary>
    /// Renders a per-section count map from counts that were already aggregated across one or
    /// more independently rendered documents.
    /// </summary>
    public static string RenderCountMap(
        IReadOnlyDictionary<string, int> counts,
        IReadOnlyList<string> orderedSections)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("| Section | Count |\n");
        builder.Append("| ------- | ----- |\n");
        foreach (var section in orderedSections)
        {
            counts.TryGetValue(section, out var count);
            builder.Append($"| {section} | {count} |\n");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Emits a per-section count map (<c>| Section | Count |</c>) over <paramref name="orderedSections"/>,
    /// reporting 0 for sections absent from the rendered markdown.
    /// </summary>
    public static void WriteCountMapFromMarkdown(
        string markdown,
        IReadOnlyList<string> orderedSections,
        string? outputPath = null)
    {
        WriteCountMap(CountMarkdownTableRowsBySection(markdown), orderedSections, outputPath);
    }

    /// <summary>
    /// Emits a per-section count map from counts that were already aggregated across one or more
    /// independently rendered documents.
    /// </summary>
    public static void WriteCountMap(
        IReadOnlyDictionary<string, int> counts,
        IReadOnlyList<string> orderedSections,
        string? outputPath = null,
        bool applyLineWindow = false)
    {
        WriteCountResult(
            RenderCountMap(counts, orderedSections),
            outputPath,
            applyLineWindow);
    }
}
