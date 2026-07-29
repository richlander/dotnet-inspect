namespace DotnetInspector.Output;

internal static class MarkdownTableRowLimiter
{
    public static string Apply(string markdown, RowWindow? window)
    {
        if (window is not { IsUnlimited: false } limit)
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

            // Collect the table body (data rows + any interior separators) in order,
            // then emit it preserving original positions: separators pass through in
            // place and only the head/tail window of data rows is kept.
            List<string> body = [];
            while (i + 1 < lines.Length && MarkdownScan.IsTableLine(lines[i + 1]))
                body.Add(lines[++i]);

            var dataCount = body.Count(static l => !MarkdownScan.IsSeparatorLine(l));
            var (keepStart, keepEnd) = limit.Resolve(dataCount);
            var dataIndex = 0;
            foreach (var bodyLine in body)
            {
                if (MarkdownScan.IsSeparatorLine(bodyLine))
                {
                    output.Add(bodyLine);
                    continue;
                }

                if (dataIndex >= keepStart && dataIndex < keepEnd)
                    output.Add(bodyLine);
                dataIndex++;
            }
        }

        return string.Join('\n', output);
    }
}
