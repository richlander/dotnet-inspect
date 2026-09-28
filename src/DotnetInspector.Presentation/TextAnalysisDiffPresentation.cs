using System.Collections.Immutable;
using Inspector.Findings;
using Inspector.Text;
using Markout;

namespace DotnetInspector.Presentation;

/// <summary>Host-neutral presentation lowering for analytical text diffs.</summary>
public static class TextAnalysisDiffPresentation
{
    /// <summary>
    /// Projects a completed producer-issued line comparison into the shared analytical shape
    /// without running another matcher. The supplied texts establish the complete ordered endpoint
    /// sequences and must be LF-normalized without final line terminators.
    /// </summary>
    public static AnalysisDiff<string> CreateAnalysisDiff(
        FindingComparison<string>.Complete comparison,
        string beforeText,
        string afterText)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(beforeText);
        ArgumentNullException.ThrowIfNull(afterText);

        ImmutableArray<string> before = SplitUnterminatedLines(beforeText, nameof(beforeText));
        ImmutableArray<string> after = SplitUnterminatedLines(afterText, nameof(afterText));
        ValidateAtoms(comparison.OldAtoms, before, "Before");
        ValidateAtoms(comparison.NewAtoms, after, "After");

        var relations = ImmutableArray.CreateBuilder<AnalysisDiffRelation>(
            comparison.Pairs.Length);
        foreach (PairFinding<string> pair in comparison.Pairs)
        {
            relations.Add(pair switch
            {
                PairFinding<string>.Added added =>
                    new AnalysisDiffRelation.Addition(
                        [RequiredOrdinal(added.New, after.Length, "After")]),
                PairFinding<string>.Removed removed =>
                    new AnalysisDiffRelation.Removal(
                        [RequiredOrdinal(removed.Old, before.Length, "Before")]),
                PairFinding<string>.Present present =>
                    new AnalysisDiffRelation.Correspondence(
                        [RequiredOrdinal(present.Old, before.Length, "Before")],
                        [RequiredOrdinal(present.New, after.Length, "After")],
                        AnalysisDiffContentKind.Unchanged,
                        Placement(present.Difference)),
                PairFinding<string>.Changed changed =>
                    new AnalysisDiffRelation.Correspondence(
                        [RequiredOrdinal(changed.Old, before.Length, "Before")],
                        [RequiredOrdinal(changed.New, after.Length, "After")],
                        AnalysisDiffContentKind.Changed,
                        Placement(changed.Difference)),
            });
        }

        return new AnalysisDiff<string>(before, after, relations.ToImmutable());
    }

    /// <summary>
    /// Lowers producer-issued analytical line relations into a conventional mapped text diff.
    /// Stable unchanged one-to-one correspondences become anchors; every other relation is
    /// intentionally presented as removal and addition text.
    /// </summary>
    public static MappedTextDiff CreateMappedTextDiff(
        AnalysisDiff<string> analysis,
        string beforeLabel,
        TextDiffLineTerminator beforeTerminator,
        string afterLabel,
        TextDiffLineTerminator afterTerminator)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(beforeLabel);
        ArgumentNullException.ThrowIfNull(afterLabel);

        var anchors = analysis.Relations
            .OfType<AnalysisDiffRelation.Correspondence>()
            .Where(relation =>
                relation.Content == AnalysisDiffContentKind.Unchanged
                && relation.Placement == AnalysisDiffPlacementKind.Stable
                && relation.BeforeCoordinates.Length == 1
                && relation.AfterCoordinates.Length == 1)
            .Select(relation => (
                Before: relation.BeforeCoordinates[0],
                After: relation.AfterCoordinates[0]))
            .OrderBy(anchor => anchor.Before)
            .ToArray();

        var changes = new List<TextDiffChange>();
        int beforePosition = 0;
        int afterPosition = 0;
        foreach ((int beforeAnchor, int afterAnchor) in anchors)
        {
            if (beforeAnchor < beforePosition || afterAnchor < afterPosition)
            {
                throw new InvalidOperationException(
                    "Stable text-diff anchors must preserve endpoint order.");
            }

            AddChange(
                changes,
                beforePosition,
                beforeAnchor,
                afterPosition,
                afterAnchor);
            beforePosition = beforeAnchor + 1;
            afterPosition = afterAnchor + 1;
        }
        AddChange(
            changes,
            beforePosition,
            analysis.Before.Length,
            afterPosition,
            analysis.After.Length);

        return new MappedTextDiff(
            new TextDiffSequence(analysis.Before, beforeLabel, beforeTerminator),
            new TextDiffSequence(analysis.After, afterLabel, afterTerminator),
            changes);
    }

    /// <summary>
    /// Lowers a characterized line diff into a mapped text diff with one change per issued
    /// homogeneous change. Whitespace-only changes carry a subdued label naming their edit kinds,
    /// request visible whitespace, and lower single-line edits to inner mappings; the two ends of a
    /// move carry linked <c>moved (n) to/from</c> labels; changed changes are unlabeled.
    /// </summary>
    public static MappedTextDiff CreateLabeledMappedTextDiff(
        AnalysisDiff<string> analysis,
        TextDiffCharacterization characterization,
        string beforeLabel,
        TextDiffLineTerminator beforeTerminator,
        string afterLabel,
        TextDiffLineTerminator afterTerminator)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(characterization);
        ArgumentNullException.ThrowIfNull(beforeLabel);
        ArgumentNullException.ThrowIfNull(afterLabel);

        TextChange[] issued = [.. characterization.Regions.SelectMany(region => region.Changes)];
        var addresses = new Dictionary<int, List<int>>();
        for (int address = 0; address < issued.Length; address++)
        {
            if (issued[address].MoveId is { } id)
            {
                if (!addresses.TryGetValue(id, out List<int>? ends))
                    addresses[id] = ends = [];
                ends.Add(address);
            }
        }

        var moves = characterization.Moves.ToDictionary(move => move.Id);
        var changes = new List<TextDiffChange>(issued.Length);
        for (int address = 0; address < issued.Length; address++)
        {
            TextChange change = issued[address];
            var before = new TextDiffRange(change.Before.Start, change.Before.Count);
            var after = new TextDiffRange(change.After.Start, change.After.Count);
            changes.Add(change.Outcome switch
            {
                TextChangeOutcome.WhitespaceOnly => new TextDiffChange(
                    before,
                    after,
                    before.IsEmpty || after.IsEmpty ? null : InnerMappings(change, analysis.Before, analysis.After),
                    label: new TextDiffChangeLabel(
                        "whitespace-only: " + DescribeKinds(change.Edits),
                        TextDiffLabelEmphasis.Subdued,
                        showWhitespace: true)),
                TextChangeOutcome.Moved => new TextDiffChange(
                    before,
                    after,
                    label: MoveLabel(change, moves[change.MoveId!.Value], addresses[change.MoveId.Value], address)),
                _ => new TextDiffChange(before, after),
            });
        }

        return new MappedTextDiff(
            new TextDiffSequence(analysis.Before, beforeLabel, beforeTerminator),
            new TextDiffSequence(analysis.After, afterLabel, afterTerminator),
            changes);
    }

    static TextDiffChangeLabel MoveLabel(TextChange change, TextMove move, List<int> ends, int address)
    {
        int related = ends.Single(end => end != address);
        string text = change.Before.Count > 0
            ? $"moved ({move.Id}) to +{move.After.Start + 1}"
            : $"moved ({move.Id}) from -{move.Before.Start + 1}";
        if (move.Content == TextMoveContent.WhitespaceOnly)
            text += "; whitespace-only: " + DescribeKinds(move.Edits);
        else if (move.Content == TextMoveContent.Changed)
            text += "; changed";
        return new TextDiffChangeLabel(text, relatedChange: related);
    }

    /// <summary>
    /// Lowers whitespace edits to single-line inner mappings. An edit whose two sides span the same
    /// number of lines is split into one span per line; an edit that adds or removes line
    /// boundaries has no single-line form and stays conveyed by the change label.
    /// </summary>
    static IEnumerable<TextDiffInnerMapping> InnerMappings(
        TextChange change,
        ImmutableArray<string> beforeLines,
        ImmutableArray<string> afterLines)
    {
        foreach (TextWhitespaceEdit edit in change.Edits)
        {
            int span = edit.BeforeEnd.Line - edit.BeforeStart.Line;
            if (span != edit.AfterEnd.Line - edit.AfterStart.Line)
                continue;

            for (int offset = 0; offset <= span; offset++)
            {
                int beforeLine = edit.BeforeStart.Line + offset;
                int afterLine = edit.AfterStart.Line + offset;
                if (beforeLine < change.Before.Start
                    || beforeLine >= change.Before.End
                    || afterLine < change.After.Start
                    || afterLine >= change.After.End)
                {
                    continue;
                }

                int beforeFrom = offset == 0 ? edit.BeforeStart.Column : 0;
                int beforeTo = offset == span ? edit.BeforeEnd.Column : beforeLines[beforeLine].Length;
                int afterFrom = offset == 0 ? edit.AfterStart.Column : 0;
                int afterTo = offset == span ? edit.AfterEnd.Column : afterLines[afterLine].Length;
                if (beforeFrom == beforeTo && afterFrom == afterTo)
                    continue;
                if (beforeLines[beforeLine].AsSpan(beforeFrom, beforeTo - beforeFrom)
                    .SequenceEqual(afterLines[afterLine].AsSpan(afterFrom, afterTo - afterFrom)))
                {
                    continue;
                }

                yield return new TextDiffInnerMapping(
                    new TextDiffSpan(beforeLine, beforeFrom, beforeTo - beforeFrom),
                    new TextDiffSpan(afterLine, afterFrom, afterTo - afterFrom));
            }
        }
    }

    static string DescribeKinds(IEnumerable<TextWhitespaceEdit> edits)
    {
        TextWhitespaceEditKinds kinds = edits.Aggregate(
            TextWhitespaceEditKinds.None,
            (combined, edit) => combined | edit.Kinds);
        var names = new List<string>();
        if (kinds.HasFlag(TextWhitespaceEditKinds.Indentation)) names.Add("indentation");
        if (kinds.HasFlag(TextWhitespaceEditKinds.LineBreaks)) names.Add("line breaks");
        if (kinds.HasFlag(TextWhitespaceEditKinds.BlankLineContent)) names.Add("blank-line content");
        if (kinds.HasFlag(TextWhitespaceEditKinds.Trailing)) names.Add("trailing");
        if (kinds.HasFlag(TextWhitespaceEditKinds.Spacing)) names.Add("spacing");
        if (kinds.HasFlag(TextWhitespaceEditKinds.Separation)) names.Add("separation");
        if (kinds.HasFlag(TextWhitespaceEditKinds.TerminatorSpelling)) names.Add("line-ending style");
        if (kinds.HasFlag(TextWhitespaceEditKinds.FinalLineTerminator)) names.Add("final newline");
        return names.Count == 0 ? "whitespace" : string.Join(", ", names);
    }


    static void AddChange(
        List<TextDiffChange> changes,
        int beforeStart,
        int beforeEnd,
        int afterStart,
        int afterEnd)
    {
        int beforeCount = beforeEnd - beforeStart;
        int afterCount = afterEnd - afterStart;
        if (beforeCount == 0 && afterCount == 0)
            return;

        changes.Add(new TextDiffChange(
            new TextDiffRange(beforeStart, beforeCount),
            new TextDiffRange(afterStart, afterCount)));
    }

    static ImmutableArray<string> SplitUnterminatedLines(
        string text,
        string parameterName)
    {
        if (text.Length == 0)
            return [];
        if (text[^1] is '\r' or '\n')
        {
            throw new ArgumentException(
                "Producer-issued text must not carry a final line terminator.",
                parameterName);
        }
        if (text.Contains('\r', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Producer-issued text must use LF line boundaries.",
                parameterName);
        }

        return [.. text.Split('\n')];
    }

    static void ValidateAtoms(
        ImmutableArray<Finding<string>> atoms,
        ImmutableArray<string> lines,
        string side)
    {
        if (atoms.Length != lines.Length)
        {
            throw new ArgumentException(
                $"{side} comparison atoms do not cover the supplied text.");
        }

        for (int index = 0; index < atoms.Length; index++)
        {
            Finding<string> atom = atoms[index];
            if (atom.Ordinal != index || atom.Payload != lines[index])
            {
                throw new ArgumentException(
                    $"{side} comparison atom {index} does not match the supplied text.");
            }
        }
    }

    static int RequiredOrdinal(
        Finding<string> finding,
        int lineCount,
        string side)
    {
        if (finding.Ordinal is not int ordinal
            || ordinal < 0
            || ordinal >= lineCount)
        {
            throw new ArgumentException(
                $"{side} comparison atom has no valid line ordinal.");
        }
        return ordinal;
    }

    static AnalysisDiffPlacementKind Placement(FindingDifferenceKind difference)
        => difference switch
        {
            FindingDifferenceKind.None => AnalysisDiffPlacementKind.Stable,
            FindingDifferenceKind.Moved => AnalysisDiffPlacementKind.Moved,
            _ => throw new ArgumentOutOfRangeException(
                nameof(difference),
                difference,
                "Unknown line placement classification."),
        };
}
