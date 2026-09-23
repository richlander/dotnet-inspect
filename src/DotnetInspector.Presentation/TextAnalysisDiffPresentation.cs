using System.Collections.Immutable;
using Inspector.Findings;
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
