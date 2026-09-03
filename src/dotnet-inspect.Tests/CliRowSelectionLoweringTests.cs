using DotnetInspector.CommandLine;
using DotnetInspector.RowSelection;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

public sealed class CliRowSelectionLoweringTests
{
    [Fact]
    public void CliRowSelectionExplicitValueTests()
    {
        CliRowSelectionLowering<string> value =
            Success(
                All,
                Limit(0, "12"),
                Rows(1, "2..5"));

        Assert.Equal(2, value.SemanticIntent.Operations.Count);
        Assert.Equal(
            RowSelectionStageKind.Head,
            value.SemanticIntent.Operations[0].Kind);
        Assert.Equal(
            12,
            value.SemanticIntent.Operations[0].Count);
        Assert.Equal(
            RowSelectionStageKind.Window,
            value.SemanticIntent.Operations[1].Kind);
        Assert.Equal(
            2,
            value.SemanticIntent.Operations[1].Start);
        Assert.Equal(
            5,
            value.SemanticIntent.Operations[1].End);

        AssertWindow("..5", null, 5);
        AssertWindow("3..", 3, null);
        AssertWindow("7..7", 7, 7);
        AssertCount(Top(0, "4"), 4);

        AssertFailure(
            Limit(0, ""),
            CliRowSelectionFailureReason.MalformedValue);
        AssertFailure(
            Limit(0, "-1"),
            CliRowSelectionFailureReason.MalformedValue);
        AssertFailure(
            Limit(0, "٠١"),
            CliRowSelectionFailureReason.MalformedValue);
        AssertFailure(
            Limit(0, "0"),
            CliRowSelectionFailureReason.NonPositiveValue);
        AssertFailure(
            Limit(0, "99999999999999999999"),
            CliRowSelectionFailureReason.OverflowValue);
        AssertFailure(
            Rows(0, "5"),
            CliRowSelectionFailureReason.InvalidWindowForm);
        AssertFailure(
            Rows(0, "2+3"),
            CliRowSelectionFailureReason.InvalidWindowForm);
        AssertFailure(
            Rows(0, ".."),
            CliRowSelectionFailureReason.InvalidWindowForm);
        AssertFailure(
            Rows(0, "1..2..3"),
            CliRowSelectionFailureReason.InvalidWindowForm);
        AssertFailure(
            Rows(0, "4..2"),
            CliRowSelectionFailureReason.ReversedWindow);
        AssertFailure(
            Rows(0, "0..2"),
            CliRowSelectionFailureReason.NonPositiveValue);
        AssertFailure(
            Rows(0, "1.. 2"),
            CliRowSelectionFailureReason.MalformedValue);
    }

    [Fact]
    public void CliRowSelectionExplicitOrderTests()
    {
        CliRowSelectionLowering<string> ordered =
            Success(
                All,
                Top(8, "2"),
                Rows(2, "3..6"),
                Limit(5, "4"));

        Assert.Equal(
            [
                RowSelectionStageKind.Window,
                RowSelectionStageKind.Head,
                RowSelectionStageKind.Top
            ],
            ordered.SemanticIntent.Operations.Select(
                operation => operation.Kind));

        CliRowSelectionLowering<string> ranked =
            Success(
                All,
                OrderBy(9, "confidence"),
                Top(2, "3"));
        RowSelectionIntentOperation<string> top =
            Assert.Single(ranked.SemanticIntent.Operations);
        Assert.True(top.HasRankingOrderOperand);
        Assert.Equal(
            "confidence",
            top.RankingOrderOperand);
        Assert.False(ranked.HasBaselineOrderOperand);

        CliRowSelectionLowering<string> defaultRanked =
            Success(
                All,
                Top(2, "3"));
        Assert.False(
            Assert.Single(defaultRanked.SemanticIntent.Operations)
                .HasRankingOrderOperand);

        CliRowSelectionLowering<string> baseline =
            Success(
                All,
                OrderBy(1, "name"),
                Limit(3, "2"));
        Assert.True(baseline.HasBaselineOrderOperand);
        Assert.Equal("name", baseline.BaselineOrderOperand);
        Assert.Throws<InvalidOperationException>(
            () => _ = ranked.BaselineOrderOperand);
    }

    [Fact]
    public void CliRowSelectionExplicitModifierTests()
    {
        CliRowSelectionLowering<string> tail =
            Success(
                All,
                Limit(0, "4"),
                Tail(2),
                Tail(3));
        Assert.Equal(
            RowSelectionStageKind.Tail,
            Assert.Single(tail.SemanticIntent.Operations).Kind);

        CliRowSelectionLowering<string> head =
            Success(
                CliRowSelectionCapabilities.HeadTail,
                Limit(0, "3"),
                Head(1),
                Head(2));
        Assert.Equal(
            RowSelectionStageKind.Head,
            Assert.Single(head.SemanticIntent.Operations).Kind);

        CliRowSelectionLowering<string> lines =
            Success(
                All,
                Limit(0, "4"),
                Lines(1),
                Tail(2),
                TailLines(3),
                Lines(4));
        Assert.Empty(lines.SemanticIntent.Operations);
        Assert.NotNull(lines.LineIntent);
        Assert.Equal(4, lines.LineIntent.Count);
        Assert.Equal(
            CliLineSelectionDirection.Tail,
            lines.LineIntent.Direction);

        CliRowSelectionLowering<string> windowAndLines =
            Success(
                CliRowSelectionCapabilities.Window
                    | CliRowSelectionCapabilities.Lines,
                Rows(0, "3..6"),
                Limit(1, "2"),
                Lines(2));
        RowSelectionIntentOperation<string> survivingWindow =
            Assert.Single(
                windowAndLines.SemanticIntent.Operations);
        Assert.Equal(
            RowSelectionStageKind.Window,
            survivingWindow.Kind);
        Assert.Equal(3, survivingWindow.Start);
        Assert.Equal(6, survivingWindow.End);
        Assert.NotNull(windowAndLines.LineIntent);
        Assert.Equal(2, windowAndLines.LineIntent.Count);
        Assert.Equal(
            CliLineSelectionDirection.Head,
            windowAndLines.LineIntent.Direction);

        AssertConflict(
            CliRowSelectionFailureReason.ConflictingDirection,
            3,
            Limit(0, "2"),
            Head(1),
            Tail(3));
        AssertConflict(
            CliRowSelectionFailureReason.ConflictingDirection,
            4,
            Limit(0, "2"),
            TailLines(1),
            Head(4));
        AssertConflict(
            CliRowSelectionFailureReason.ModifierRequiresCount,
            1,
            Top(0, "2"),
            Lines(1),
            Tail(2));
    }

    [Fact]
    public void CliRowSelectionExplicitCapabilityTests()
    {
        AssertCapabilityFailure(
            CliRowSelectionCapabilities.Window,
            Rows(2, "1..2"));
        AssertCapabilityFailure(
            CliRowSelectionCapabilities.Top,
            Top(2, "3"));
        AssertCapabilityFailure(
            CliRowSelectionCapabilities.OrderBy,
            OrderBy(2, "confidence"));
        AssertCapabilityFailure(
            CliRowSelectionCapabilities.Lines,
            Limit(0, "2"),
            Lines(2));

        CliRowSelectionLowering<string> lineOnly =
            Success(
                CliRowSelectionCapabilities.Lines,
                TailLines(0),
                Limit(1, "2"));
        Assert.Empty(lineOnly.SemanticIntent.Operations);
        Assert.NotNull(lineOnly.LineIntent);
        Assert.Equal(
            CliLineSelectionDirection.Tail,
            lineOnly.LineIntent.Direction);

        CliRowSelectionLowering<string> exact =
            Success(
                CliRowSelectionCapabilities.Window
                    | CliRowSelectionCapabilities.Top
                    | CliRowSelectionCapabilities.OrderBy,
                Rows(0, "1..2"),
                Top(1, "3"),
                OrderBy(2, "confidence"));
        Assert.Equal(2, exact.SemanticIntent.Operations.Count);

        Assert.True(
            CliRowSelectionLowerer.Lower(
                Array.Empty<
                    CliRowSelectionOccurrence<string>>(),
                CliRowSelectionCapabilities.None)
                .IsSuccess);
    }

    [Fact]
    public void CliRowSelectionExplicitFailurePrecedenceTests()
    {
        CliRowSelectionFailure malformedBeforeRepeat =
            Failure(
                All,
                Limit(0, "2"),
                Limit(1, "3"),
                Rows(4, "bad"));
        Assert.Equal(
            CliRowSelectionFailureReason.InvalidWindowForm,
            malformedBeforeRepeat.Reason);
        Assert.Equal(4, malformedBeforeRepeat.Position);

        CliRowSelectionFailure firstMalformedByPosition =
            Failure(
                All,
                Top(8, "bad"),
                Rows(3, "0..2"));
        Assert.Equal(
            CliRowSelectionFailureReason.NonPositiveValue,
            firstMalformedByPosition.Reason);
        Assert.Equal(3, firstMalformedByPosition.Position);

        CliRowSelectionFailure repeatBeforeCapability =
            Failure(
                CliRowSelectionCapabilities.None,
                Limit(0, "2"),
                Limit(6, "3"));
        Assert.Equal(
            CliRowSelectionFailureReason.RepeatedGesture,
            repeatBeforeCapability.Reason);
        Assert.Equal(6, repeatBeforeCapability.Position);

        CliRowSelectionFailure conflictBeforeAbsence =
            Failure(
                All,
                Lines(0),
                Head(1),
                Tail(5));
        Assert.Equal(
            CliRowSelectionFailureReason.ConflictingDirection,
            conflictBeforeAbsence.Reason);
        Assert.Equal(5, conflictBeforeAbsence.Position);

        CliRowSelectionFailure firstAbsenceModifier =
            Failure(
                All,
                Tail(7),
                Lines(2));
        Assert.Equal(
            CliRowSelectionFailureReason.ModifierRequiresCount,
            firstAbsenceModifier.Reason);
        Assert.Equal(2, firstAbsenceModifier.Position);

        CliRowSelectionFailure firstCapability =
            Failure(
                CliRowSelectionCapabilities.HeadTail,
                Top(8, "2"),
                Rows(2, "1..2"));
        Assert.Equal(
            CliRowSelectionFailureReason.UnsupportedCapability,
            firstCapability.Reason);
        Assert.Equal(2, firstCapability.Position);
        Assert.Equal(
            CliRowSelectionOccurrenceKind.Rows,
            firstCapability.OccurrenceKind);
    }

    private const CliRowSelectionCapabilities All =
        CliRowSelectionCapabilities.All;

    private static CliRowSelectionOccurrence<string> Limit(
        int position,
        string value) =>
        CliRowSelectionOccurrence<string>.Limit(
            position,
            value);

    private static CliRowSelectionOccurrence<string> Rows(
        int position,
        string value) =>
        CliRowSelectionOccurrence<string>.Rows(
            position,
            value);

    private static CliRowSelectionOccurrence<string> Top(
        int position,
        string value) =>
        CliRowSelectionOccurrence<string>.Top(
            position,
            value);

    private static CliRowSelectionOccurrence<string> OrderBy(
        int position,
        string value) =>
        CliRowSelectionOccurrence<string>.OrderBy(
            position,
            value);

    private static CliRowSelectionOccurrence<string> Head(
        int position) =>
        CliRowSelectionOccurrence<string>.Head(position);

    private static CliRowSelectionOccurrence<string> Tail(
        int position) =>
        CliRowSelectionOccurrence<string>.Tail(position);

    private static CliRowSelectionOccurrence<string> Lines(
        int position) =>
        CliRowSelectionOccurrence<string>.Lines(position);

    private static CliRowSelectionOccurrence<string> TailLines(
        int position) =>
        CliRowSelectionOccurrence<string>.TailLines(position);

    private static void AssertWindow(
        string value,
        int? start,
        int? end)
    {
        RowSelectionIntentOperation<string> operation =
            Assert.Single(
                Success(
                    All,
                    Rows(0, value))
                    .SemanticIntent.Operations);
        Assert.Equal(start, operation.Start);
        Assert.Equal(end, operation.End);
    }

    private static void AssertCount(
        CliRowSelectionOccurrence<string> occurrence,
        int count)
    {
        Assert.Equal(
            count,
            Assert.Single(
                Success(All, occurrence)
                    .SemanticIntent.Operations)
                .Count);
    }

    private static void AssertFailure(
        CliRowSelectionOccurrence<string> occurrence,
        CliRowSelectionFailureReason reason)
    {
        CliRowSelectionFailure failure =
            Failure(All, occurrence);
        Assert.Equal(reason, failure.Reason);
        Assert.Equal(
            occurrence.Kind,
            failure.OccurrenceKind);
        Assert.Equal(
            occurrence.Position,
            failure.Position);
    }

    private static void AssertConflict(
        CliRowSelectionFailureReason reason,
        int position,
        params CliRowSelectionOccurrence<string>[] occurrences)
    {
        CliRowSelectionFailure failure =
            Failure(All, occurrences);
        Assert.Equal(reason, failure.Reason);
        Assert.Equal(position, failure.Position);
    }

    private static void AssertCapabilityFailure(
        CliRowSelectionCapabilities missing,
        params CliRowSelectionOccurrence<string>[] occurrences)
    {
        CliRowSelectionFailure failure =
            Failure(
                All & ~missing,
                occurrences);
        Assert.Equal(
            CliRowSelectionFailureReason.UnsupportedCapability,
            failure.Reason);
        Assert.Equal(missing, failure.MissingCapabilities);
    }

    private static CliRowSelectionLowering<string> Success(
        CliRowSelectionCapabilities capabilities,
        params CliRowSelectionOccurrence<string>[] occurrences)
    {
        CliRowSelectionLoweringResult<string> result =
            CliRowSelectionLowerer.Lower(
                occurrences,
                capabilities);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
        return Assert.IsType<CliRowSelectionLowering<string>>(
            result.Value);
    }

    private static CliRowSelectionFailure Failure(
        CliRowSelectionCapabilities capabilities,
        params CliRowSelectionOccurrence<string>[] occurrences)
    {
        CliRowSelectionLoweringResult<string> result =
            CliRowSelectionLowerer.Lower(
                occurrences,
                capabilities);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        return Assert.IsType<CliRowSelectionFailure>(
            result.Failure);
    }
}
