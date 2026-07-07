namespace DotnetInspector.Output;

internal static class SourceTextDiff
{
    public static string CreateUnifiedDiff(string? before, string? after, string beforeLabel, string afterLabel)
    {
        if (string.IsNullOrWhiteSpace(before))
            return $"# {beforeLabel} unavailable; source diff requires both {beforeLabel} and {afterLabel}.";
        if (string.IsNullOrWhiteSpace(after))
            return $"# {afterLabel} unavailable; source diff requires both {beforeLabel} and {afterLabel}.";

        var beforeLines = SplitLines(before);
        var afterLines = SplitLines(after);
        if (beforeLines.SequenceEqual(afterLines))
            return $"# {beforeLabel} and {afterLabel} are identical.";

        var diff = DiffLines(beforeLines, afterLines);
        var output = new List<string>(diff.Count + 3)
        {
            $"--- {beforeLabel}",
            $"+++ {afterLabel}",
            $"@@ -1,{beforeLines.Length} +1,{afterLines.Length} @@"
        };
        output.AddRange(diff.Select(item => $"{item.Prefix}{item.Text}"));
        return string.Join(Environment.NewLine, output);
    }

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static List<(char Prefix, string Text)> DiffLines(string[] before, string[] after)
    {
        var lcs = new int[before.Length + 1, after.Length + 1];
        for (int i = before.Length - 1; i >= 0; i--)
        {
            for (int j = after.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = before[i] == after[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<(char Prefix, string Text)>();
        int left = 0;
        int right = 0;
        while (left < before.Length && right < after.Length)
        {
            if (before[left] == after[right])
            {
                result.Add((' ', before[left]));
                left++;
                right++;
            }
            else if (lcs[left + 1, right] >= lcs[left, right + 1])
            {
                result.Add(('-', before[left++]));
            }
            else
            {
                result.Add(('+', after[right++]));
            }
        }

        while (left < before.Length)
            result.Add(('-', before[left++]));
        while (right < after.Length)
            result.Add(('+', after[right++]));

        return result;
    }
}
