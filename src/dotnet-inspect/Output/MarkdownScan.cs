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

    /// <summary>
    /// The line ending the text already uses, so a pass that splits on '\n' can rejoin
    /// without changing them. Row limiting selects which rows survive; it is not
    /// licensed to rewrite CRLF to LF, which would make the same output differ byte
    /// for byte depending on whether a row window was supplied.
    /// </summary>
    public static string DetectNewline(string text)
        => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

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
