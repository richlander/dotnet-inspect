namespace DotnetInspector.Output;

/// <summary>
/// Reorders rendered Markdown H2 sections while preserving the document preamble.
/// </summary>
internal static class MarkdownSectionOrderer
{
    public static string Apply(string markdown, IReadOnlyList<string> sectionOrder)
    {
        if (string.IsNullOrEmpty(markdown) || sectionOrder.Count == 0)
            return markdown;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sectionStarts = new List<int>();
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (MarkdownScan.IsCodeFence(lines[i]))
                inFence = !inFence;

            if (!inFence && lines[i].StartsWith("## ", StringComparison.Ordinal))
                sectionStarts.Add(i);
        }

        if (sectionStarts.Count < 2)
            return markdown;

        var prefix = lines[..sectionStarts[0]];
        var sections = new List<(string Name, int Ordinal, string[] Lines)>();
        for (var i = 0; i < sectionStarts.Count; i++)
        {
            var start = sectionStarts[i];
            var end = i + 1 < sectionStarts.Count ? sectionStarts[i + 1] : lines.Length;
            var name = lines[start][3..].Trim();
            sections.Add((name, i, lines[start..end]));
        }

        var order = sectionOrder
            .Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        var orderedSections = sections
            .OrderBy(section => order.TryGetValue(section.Name, out var index) ? index : int.MaxValue)
            .ThenBy(section => order.ContainsKey(section.Name) ? 0 : 1)
            .ThenBy(section => order.ContainsKey(section.Name) ? section.Name : "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(section => section.Ordinal);

        var output = prefix.ToList();
        foreach (var section in orderedSections)
        {
            if (output.Count > 0 && output[^1].Length > 0)
                output.Add("");
            output.AddRange(section.Lines);
        }

        return string.Join('\n', output);
    }
}
