using ILInspector.Findings;
using ILInspector.Text;

namespace DotnetInspector.Output;

internal static class SourceTextDiffRenderer
{
    const int ReviewContextLines = 3;
    const int MaximumReviewHunks = 5;
    const int MaximumReviewLinesPerHunk = 80;

    static readonly FindingSubject Subject = new("source.text", "Source text");

    public static string CreateUnifiedDiff(
        string? before,
        string? after,
        string beforeLabel,
        string afterLabel,
        bool reviewerSized = false)
    {
        // Null is the caller-level unavailable state. Non-null empty and whitespace-only
        // documents remain valid exact text inputs.
        if (before is null)
            return $"# {beforeLabel} unavailable; source diff requires both {beforeLabel} and {afterLabel}.";
        if (after is null)
            return $"# {afterLabel} unavailable; source diff requires both {beforeLabel} and {afterLabel}.";

        var comparison = TextFindings.Compare(before, after, Subject) switch
        {
            FindingComparison<string>.Complete complete => complete,
            FindingComparison<string>.Failed failed => throw new InvalidOperationException(
                $"The total text producer unexpectedly failed: {failed.Failure}"),
        };
        if (comparison.IsExact)
            return $"# {beforeLabel} and {afterLabel} are identical.";

        var diff = RenderLines(comparison);
        return reviewerSized
            ? RenderReviewerDiff(diff, beforeLabel, afterLabel)
            : RenderCompleteDiff(diff, comparison, beforeLabel, afterLabel);
    }

    static string RenderCompleteDiff(
        IReadOnlyList<(char Prefix, string Text)> diff,
        FindingComparison<string>.Complete comparison,
        string beforeLabel,
        string afterLabel)
    {
        var output = new List<string>(diff.Count + 3)
        {
            $"--- {beforeLabel}",
            $"+++ {afterLabel}",
            $"@@ -{RangeStart(comparison.OldAtoms.Length)},{comparison.OldAtoms.Length} "
            + $"+{RangeStart(comparison.NewAtoms.Length)},{comparison.NewAtoms.Length} @@"
        };
        output.AddRange(diff.Select(item => $"{item.Prefix}{item.Text}"));
        return string.Join("\n", output);
    }

    static string RenderReviewerDiff(
        IReadOnlyList<(char Prefix, string Text)> diff,
        string beforeLabel,
        string afterLabel)
    {
        var hunks = ReviewHunks(diff);
        var output = new List<string>
        {
            $"--- {beforeLabel}",
            $"+++ {afterLabel}",
        };

        int omittedHunks = Math.Max(0, hunks.Count - MaximumReviewHunks);
        int omittedHunkLines = hunks
            .Skip(MaximumReviewHunks)
            .Sum(static hunk => hunk.End - hunk.Start);
        int omittedShownHunkLines = 0;

        foreach (var hunk in hunks.Take(MaximumReviewHunks))
        {
            int length = hunk.End - hunk.Start;
            if (length <= MaximumReviewLinesPerHunk)
            {
                AddHunk(output, diff, hunk.Start, hunk.End);
                continue;
            }

            int leading = MaximumReviewLinesPerHunk / 2;
            int trailing = MaximumReviewLinesPerHunk - leading;
            int omitted = length - MaximumReviewLinesPerHunk;
            AddHunk(output, diff, hunk.Start, hunk.Start + leading);
            output.Add($"# ... {omitted} diff lines omitted from this hunk ...");
            AddHunk(output, diff, hunk.End - trailing, hunk.End);
            omittedShownHunkLines += omitted;
        }

        if (omittedHunks > 0 || omittedShownHunkLines > 0)
        {
            var omissions = new List<string>(2);
            if (omittedHunks > 0)
            {
                omissions.Add(
                    $"{omittedHunks} additional hunk{(omittedHunks == 1 ? "" : "s")} "
                    + $"({omittedHunkLines} line{(omittedHunkLines == 1 ? "" : "s")})");
            }
            if (omittedShownHunkLines > 0)
            {
                omissions.Add(
                    $"{omittedShownHunkLines} line{(omittedShownHunkLines == 1 ? "" : "s")} "
                    + "within shown hunks");
            }
            output.Insert(
                0,
                $"# Source diff status: Partial - {string.Join(" and ", omissions)} omitted; "
                + "use -v:d for complete line evidence.");
        }

        return string.Join("\n", output);
    }

    static List<(int Start, int End)> ReviewHunks(
        IReadOnlyList<(char Prefix, string Text)> diff)
    {
        var hunks = new List<(int Start, int End)>();
        int index = 0;
        while (index < diff.Count)
        {
            while (index < diff.Count && diff[index].Prefix == ' ')
                index++;
            if (index == diff.Count)
                break;

            int changedStart = index;
            while (index < diff.Count && diff[index].Prefix != ' ')
                index++;
            int start = Math.Max(0, changedStart - ReviewContextLines);
            int end = Math.Min(diff.Count, index + ReviewContextLines);

            if (hunks.Count > 0 && start <= hunks[^1].End)
            {
                var previous = hunks[^1];
                hunks[^1] = (previous.Start, Math.Max(previous.End, end));
            }
            else
            {
                hunks.Add((start, end));
            }
        }
        return hunks;
    }

    static (int OldStart, int OldCount, int NewStart, int NewCount) HunkCoordinates(
        IReadOnlyList<(char Prefix, string Text)> diff,
        int start,
        int end)
    {
        int oldStart = 1;
        int newStart = 1;
        for (int index = 0; index < start; index++)
        {
            if (diff[index].Prefix != '+')
                oldStart++;
            if (diff[index].Prefix != '-')
                newStart++;
        }

        int oldCount = 0;
        int newCount = 0;
        for (int index = start; index < end; index++)
        {
            if (diff[index].Prefix != '+')
                oldCount++;
            if (diff[index].Prefix != '-')
                newCount++;
        }
        if (oldCount == 0)
            oldStart--;
        if (newCount == 0)
            newStart--;
        return (oldStart, oldCount, newStart, newCount);
    }

    static int RangeStart(int count) => count == 0 ? 0 : 1;

    static void AddHunk(
        List<string> output,
        IReadOnlyList<(char Prefix, string Text)> diff,
        int start,
        int end)
    {
        var coordinates = HunkCoordinates(diff, start, end);
        output.Add(
            $"@@ -{coordinates.OldStart},{coordinates.OldCount} "
            + $"+{coordinates.NewStart},{coordinates.NewCount} @@");
        for (int index = start; index < end; index++)
            output.Add($"{diff[index].Prefix}{diff[index].Text}");
    }

    static List<(char Prefix, string Text)> RenderLines(
        FindingComparison<string>.Complete comparison)
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
            {
                anchors.Add((
                    RequiredOrdinal(present.Old),
                    RequiredOrdinal(present.New)));
            }
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

    static int RequiredOrdinal(Finding<string> finding)
        => finding.Ordinal
            ?? throw new InvalidOperationException("A text-line finding must retain its stream ordinal.");
}
