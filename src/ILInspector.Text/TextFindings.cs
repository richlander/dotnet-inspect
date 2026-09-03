using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.Text;

/// <summary>An exact text-line census exceeded its caller-selected limit.</summary>
public sealed class TextFindingComplexityException(int limit)
    : InvalidOperationException(
        $"Text exceeds the finding complexity limit of {limit:N0} lines.")
{
    public int Limit { get; } = limit;
}

/// <summary>Projects arbitrary text onto the ordered finding spine.</summary>
public static class TextFindings
{
    /// <summary>The finding descriptor for one logical text line.</summary>
    public static readonly FindingDescriptor LineDescriptor = new("text.line", "Text line");

    /// <summary>
    /// Lazily yields an exact line census. Each string payload is the line content and
    /// <see cref="Finding{T}.Ordinal"/> is its zero-based position in the logical line stream.
    /// CRLF, CR, and LF are equivalent boundaries.
    /// Empty text has zero lines. A terminating boundary produces a final empty line, preserving
    /// the distinction between a document with and without a final newline.
    /// </summary>
    public static IEnumerable<Finding<string>> Inspect(string text, FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(subject);

        return ProjectAtoms(SplitLines(text), subject);
    }

    /// <summary>
    /// Lazily yields an exact line census after refusing text that exceeds
    /// <paramref name="maxLineCount"/>.
    /// </summary>
    public static IEnumerable<Finding<string>> Inspect(
        string text,
        FindingSubject subject,
        int maxLineCount)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineCount);

        if (CountLines(text) > maxLineCount)
            throw new TextFindingComplexityException(maxLineCount);

        return ProjectAtoms(SplitLines(text), subject);
    }

    /// <summary>Compares two non-null text documents with exact, ordered line identity.</summary>
    public static FindingComparison<string> Compare(
        string oldText,
        string newText,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentNullException.ThrowIfNull(subject);

        var oldAtoms = Inspect(oldText, subject).ToImmutableArray();
        var newAtoms = Inspect(newText, subject).ToImmutableArray();
        FindingInspection<string> oldInspection =
            new FindingInspection<string>.Complete(oldAtoms);
        FindingInspection<string> newInspection =
            new FindingInspection<string>.Complete(newAtoms);
        return FindingComparison.Compare(
            oldInspection,
            newInspection,
            acceptanceThreshold: acceptanceThreshold);
    }

    /// <summary>
    /// Produces a complete analytical relation partition for two source-text line sequences.
    /// Exact matched lines retain movement; unmatched populations within one stable-anchor gap
    /// form one producer-issued changed correspondence.
    /// </summary>
    public static AnalysisDiff<string> CreateAnalysisDiff(
        string beforeText,
        string afterText,
        FindingSubject subject,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(beforeText);
        ArgumentNullException.ThrowIfNull(afterText);
        ArgumentNullException.ThrowIfNull(subject);

        ImmutableArray<string> before = SplitAnalysisLines(beforeText);
        ImmutableArray<string> after = SplitAnalysisLines(afterText);
        FindingInspection<string> beforeInspection =
            new FindingInspection<string>.Complete(ProjectAtoms(before, subject).ToImmutableArray());
        FindingInspection<string> afterInspection =
            new FindingInspection<string>.Complete(ProjectAtoms(after, subject).ToImmutableArray());
        FindingComparison<string> comparison = FindingComparison.Compare(
            beforeInspection,
            afterInspection,
            acceptanceThreshold: acceptanceThreshold);

        if (comparison is not FindingComparison<string>.Complete complete)
        {
            throw new InvalidOperationException(
                comparison.Failure ?? "Source-text comparison did not complete.");
        }

        return BuildAnalysisDiff(
            before,
            after,
            complete.Pairs,
            HasFinalLineTerminator(beforeText),
            HasFinalLineTerminator(afterText));
    }

    static IEnumerable<string> SplitLines(string text)
    {
        if (text.Length == 0)
            yield break;

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character is not ('\r' or '\n'))
                continue;

            yield return text[start..i];

            if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;

            start = i + 1;
        }

        yield return text[start..];
    }

    static int CountLines(string text)
    {
        if (text.Length == 0)
            return 0;

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
                continue;

            count++;
            if (text[i] == '\r'
                && i + 1 < text.Length
                && text[i + 1] == '\n')
            {
                i++;
            }
        }

        return count;
    }

    static AnalysisDiff<string> BuildAnalysisDiff(
        ImmutableArray<string> before,
        ImmutableArray<string> after,
        ImmutableArray<PairFinding<string>> pairs,
        bool beforeHasFinalLineTerminator,
        bool afterHasFinalLineTerminator)
    {
        var relations = ImmutableArray.CreateBuilder<AnalysisDiffRelation>();
        var additions = new List<int>();
        var removals = new List<int>();
        var stableAnchors = new List<(int Before, int After)>();

        foreach (PairFinding<string> pair in pairs)
        {
            switch (pair.Value)
            {
                case PairFinding<string>.Added added:
                    additions.Add(RequiredOrdinal(added.New));
                    break;

                case PairFinding<string>.Removed removed:
                    removals.Add(RequiredOrdinal(removed.Old));
                    break;

                case PairFinding<string>.Present present:
                {
                    int beforeCoordinate = RequiredOrdinal(present.Old);
                    int afterCoordinate = RequiredOrdinal(present.New);
                    bool lineTerminatorChanged =
                        IsTerminatedLine(
                            beforeCoordinate,
                            before.Length,
                            beforeHasFinalLineTerminator)
                        != IsTerminatedLine(
                            afterCoordinate,
                            after.Length,
                            afterHasFinalLineTerminator);
                    var content = lineTerminatorChanged
                        ? AnalysisDiffContentKind.Changed
                        : AnalysisDiffContentKind.Unchanged;
                    var placement = present.Difference == FindingDifferenceKind.Moved
                        ? AnalysisDiffPlacementKind.Moved
                        : AnalysisDiffPlacementKind.Stable;
                    relations.Add(Correspondence(
                        beforeCoordinate,
                        afterCoordinate,
                        content,
                        placement));

                    if (content == AnalysisDiffContentKind.Unchanged
                        && placement == AnalysisDiffPlacementKind.Stable)
                    {
                        stableAnchors.Add((beforeCoordinate, afterCoordinate));
                    }
                    break;
                }

                case PairFinding<string>.Changed changed:
                    relations.Add(Correspondence(
                        RequiredOrdinal(changed.Old),
                        RequiredOrdinal(changed.New),
                        AnalysisDiffContentKind.Changed,
                        changed.Difference == FindingDifferenceKind.Moved
                            ? AnalysisDiffPlacementKind.Moved
                            : AnalysisDiffPlacementKind.Stable));
                    break;

                default:
                    throw new InvalidOperationException(
                        "Source-text comparison produced an unknown pair kind.");
            }
        }

        stableAnchors.Sort(static (left, right) => left.Before.CompareTo(right.Before));

        int beforeStart = 0;
        int afterStart = 0;
        foreach ((int beforeAnchor, int afterAnchor) in stableAnchors)
        {
            if (beforeAnchor < beforeStart || afterAnchor < afterStart)
            {
                throw new InvalidOperationException(
                    "Stable source-text correspondences must preserve endpoint order.");
            }

            AddUnmatchedGapRelations(
                relations,
                removals,
                additions,
                beforeStart,
                beforeAnchor,
                afterStart,
                afterAnchor);
            beforeStart = beforeAnchor + 1;
            afterStart = afterAnchor + 1;
        }

        AddUnmatchedGapRelations(
            relations,
            removals,
            additions,
            beforeStart,
            before.Length,
            afterStart,
            after.Length);

        return new AnalysisDiff<string>(before, after, relations.ToImmutable());
    }

    static void AddUnmatchedGapRelations(
        ImmutableArray<AnalysisDiffRelation>.Builder relations,
        List<int> removals,
        List<int> additions,
        int beforeStart,
        int beforeEnd,
        int afterStart,
        int afterEnd)
    {
        ImmutableArray<int> beforeCoordinates = removals
            .Where(coordinate => coordinate >= beforeStart && coordinate < beforeEnd)
            .ToImmutableArray();
        ImmutableArray<int> afterCoordinates = additions
            .Where(coordinate => coordinate >= afterStart && coordinate < afterEnd)
            .ToImmutableArray();

        if (!beforeCoordinates.IsEmpty && !afterCoordinates.IsEmpty)
        {
            relations.Add(new AnalysisDiffRelation.Correspondence(
                beforeCoordinates,
                afterCoordinates,
                AnalysisDiffContentKind.Changed,
                AnalysisDiffPlacementKind.Stable));
            return;
        }

        foreach (int coordinate in beforeCoordinates)
            relations.Add(new AnalysisDiffRelation.Removal([coordinate]));
        foreach (int coordinate in afterCoordinates)
            relations.Add(new AnalysisDiffRelation.Addition([coordinate]));
    }

    static AnalysisDiffRelation.Correspondence Correspondence(
        int beforeCoordinate,
        int afterCoordinate,
        AnalysisDiffContentKind content,
        AnalysisDiffPlacementKind placement)
        => new([beforeCoordinate], [afterCoordinate], content, placement);

    static int RequiredOrdinal(Finding<string> finding)
        => finding.Ordinal
            ?? throw new InvalidOperationException("Source-text findings require line ordinals.");

    static ImmutableArray<string> SplitAnalysisLines(string text)
    {
        if (text.Length == 0)
            return [];

        var lines = ImmutableArray.CreateBuilder<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character is not ('\r' or '\n'))
                continue;

            lines.Add(text[start..i]);
            if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);

        return lines.ToImmutable();
    }

    static bool HasFinalLineTerminator(string text)
        => text.Length > 0 && text[^1] is '\r' or '\n';

    static bool IsTerminatedLine(
        int coordinate,
        int lineCount,
        bool hasFinalLineTerminator)
        => coordinate < lineCount - 1 || hasFinalLineTerminator;

    static IEnumerable<Finding<string>> ProjectAtoms(
        IEnumerable<string> lines,
        FindingSubject subject)
    {
        int position = 0;
        foreach (string content in lines)
        {
            yield return new Finding<string>(
                subject,
                LineDescriptor,
                new FindingKey(content),
                content,
                Ordinal: position++);
        }
    }
}
