using ILInspector.Findings;
using ILInspector.Text;

namespace DotnetInspector.Output;

internal static class SourceTextDiff
{
    static readonly FindingSubject Subject = new("source.text", "Source text");

    public static string CreateUnifiedDiff(string? before, string? after, string beforeLabel, string afterLabel)
    {
        if (before is null)
            return $"# {beforeLabel} unavailable; source diff requires both {beforeLabel} and {afterLabel}.";
        if (after is null)
            return $"# {afterLabel} unavailable; source diff requires both {beforeLabel} and {afterLabel}.";

        var comparison = TextFindings.Compare(before, after, Subject);
        if (comparison.IsExact)
            return $"# {beforeLabel} and {afterLabel} are identical.";

        var diff = RenderLines(comparison);
        var output = new List<string>(diff.Count + 3)
        {
            $"--- {beforeLabel}",
            $"+++ {afterLabel}",
            $"@@ -1,{comparison.OldAtoms.Length} +1,{comparison.NewAtoms.Length} @@"
        };
        output.AddRange(diff.Select(item => $"{item.Prefix}{item.Text}"));
        return string.Join(Environment.NewLine, output);
    }

    static List<(char Prefix, string Text)> RenderLines(TextFindingsResult comparison)
    {
        // Unchanged Present pairs are the ordered anchors. Added, Removed, Changed, and Moved
        // pairs remain gaps, so a typed move renders conventionally as '-' at its old position
        // and '+' at its new position without rematching in the presentation layer.
        var anchors = new List<(int OldPosition, int NewPosition)>();
        foreach (var pair in comparison.Pairs)
        {
            if (pair is PairFinding<string>.Present
                {
                    Difference: FindingDifferenceKind.None
                } present)
                anchors.Add((present.Old.Position, present.New.Position));
        }

        anchors.Sort(static (left, right) => left.OldPosition.CompareTo(right.OldPosition));

        var lines = new List<(char Prefix, string Text)>(
            comparison.OldAtoms.Length + comparison.NewAtoms.Length);
        int oldPosition = 0;
        int newPosition = 0;
        foreach (var anchor in anchors)
        {
            while (oldPosition < anchor.OldPosition)
                lines.Add(('-', comparison.OldAtoms[oldPosition++].Payload));
            while (newPosition < anchor.NewPosition)
                lines.Add(('+', comparison.NewAtoms[newPosition++].Payload));

            lines.Add((' ', comparison.NewAtoms[newPosition].Payload));
            oldPosition++;
            newPosition++;
        }

        while (oldPosition < comparison.OldAtoms.Length)
            lines.Add(('-', comparison.OldAtoms[oldPosition++].Payload));
        while (newPosition < comparison.NewAtoms.Length)
            lines.Add(('+', comparison.NewAtoms[newPosition++].Payload));

        return lines;
    }
}
