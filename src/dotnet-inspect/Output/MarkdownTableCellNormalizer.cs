namespace DotnetInspector.Output;

internal static class MarkdownTableCellNormalizer
{
    private const string EscapedPipe = @"\|";
    private const string PipeEntity = "&#124;";

    public static string Apply(string markdown)
    {
        if (!markdown.Contains(EscapedPipe, StringComparison.Ordinal))
            return markdown;

        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var changed = false;
        var inCodeFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (IsCodeFence(line))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (!inCodeFence && IsTableLine(line) && line.Contains(EscapedPipe, StringComparison.Ordinal))
            {
                lines[i] = line.Replace(EscapedPipe, PipeEntity, StringComparison.Ordinal);
                changed = true;
            }
        }

        return changed ? string.Join('\n', lines) : markdown;
    }

    public static bool IsTableLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('|') && trimmed.EndsWith('|');
    }

    public static bool IsCodeFence(string line)
        => line.TrimStart().StartsWith("```", StringComparison.Ordinal);
}
