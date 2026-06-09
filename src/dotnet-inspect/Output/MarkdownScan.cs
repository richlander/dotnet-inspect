namespace DotnetInspector.Output;

/// <summary>
/// Shared line-level classification for post-processing rendered Markdown (counting table rows,
/// limiting rows, reordering sections). One source of truth so fence/table detection can't drift
/// between the passes that consume Markout's output.
/// </summary>
internal static class MarkdownScan
{
    /// <summary>True for a pipe-table line: trimmed, starts and ends with '|'.</summary>
    public static bool IsTableLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('|') && trimmed.EndsWith('|');
    }

    /// <summary>True for a fenced-code-block delimiter line (```), toggling fence state.</summary>
    public static bool IsCodeFence(string line)
        => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    /// <summary>True for a table separator row (e.g. <c>| --- | :--: |</c>).</summary>
    public static bool IsSeparatorLine(string line)
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
