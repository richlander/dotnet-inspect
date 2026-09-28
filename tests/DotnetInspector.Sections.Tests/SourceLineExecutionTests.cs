using DotnetInspector.Sections;
using Inspector.Text;
using QuerySpace.Composition;
using System.Text.Json;

namespace DotnetInspector.Sections.Tests;

public sealed class SourceLineExecutionTests
{
    private static readonly SourceLineDeliveryProfile s_coldProfile =
        new(
            "test/cold",
            "test-1",
            exactCountThresholdUtf16: 0,
            boundedRowsThresholdUtf16: 0,
            unboundedRowsThresholdUtf16: 0);

    [Fact]
    public void CompleteAndColdPullPreserveFilteredHeadMeaning()
    {
        const string text =
            "drop one\nkeep one\ndrop two\nkeep two\nkeep three\ntail";
        ForwardRowQueryPlan<SourceLineCandidate> plan =
            Plan(pattern: "keep*", head: 3);
        var bounds = new SourceLineExecutionBounds(
            maximumCandidateRows: 2,
            maximumUtf16CodeUnits: 32,
            maximumJsonEncodedUtf8Bytes: 64);
        using SourceLineExecution complete =
            SourceLineExecution.Create(
                new DecodedTextDocument(text),
                QuerySpaceTerminalRequirement.Rows,
                plan,
                bounds: bounds,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        using SourceLineExecution cold =
            SourceLineExecution.Create(
                new DecodedTextDocument(text),
                QuerySpaceTerminalRequirement.Rows,
                plan,
                s_coldProfile,
                bounds,
                TestContext.Current.CancellationToken);

        DrainedRows expected = Drain(complete);
        DrainedRows actual = Drain(cold);

        Assert.Equal(
            SourceLineExecutionStrategy.Complete,
            complete.Strategy);
        Assert.Equal(
            SourceLineExecutionStrategy.ColdPull,
            cold.Strategy);
        Assert.Equal(SourceLineDemand.BoundedRows, cold.Demand);
        Assert.Equal(
            expected.Rows.Select(RowValue),
            actual.Rows.Select(RowValue));
        Assert.Equal(3, expected.ExactCount);
        Assert.Equal(expected.ExactCount, actual.ExactCount);
        Assert.Equal(6, complete.Observation.CandidateLinesScanned);
        Assert.Equal(6, complete.Observation.ProjectedRows);
        Assert.Equal(5, cold.Observation.CandidateLinesScanned);
        Assert.Equal(3, cold.Observation.ProjectedRows);
        Assert.True(actual.PullCount > 1);
    }

    [Fact]
    public void AdaptiveStrategiesPreserveEveryDemandClass()
    {
        const string text =
            "one\ntwo\nthree\nfour";
        (QuerySpaceTerminalRequirement Terminal,
            ForwardRowQueryPlan<SourceLineCandidate> Plan)[] cases =
        [
            (
                QuerySpaceTerminalRequirement.Count,
                SourceLineVocabulary.EmptyPlan),
            (
                QuerySpaceTerminalRequirement.Rows,
                HeadPlan(2)),
            (
                QuerySpaceTerminalRequirement.Rows,
                SourceLineVocabulary.EmptyPlan),
        ];
        foreach ((QuerySpaceTerminalRequirement terminal,
            ForwardRowQueryPlan<SourceLineCandidate> plan) in cases)
        {
            using SourceLineExecution complete =
                SourceLineExecution.Create(
                    new DecodedTextDocument(text),
                    terminal,
                    plan,
                    cancellationToken:
                        TestContext.Current.CancellationToken);
            using SourceLineExecution cold =
                SourceLineExecution.Create(
                    new DecodedTextDocument(text),
                    terminal,
                    plan,
                    s_coldProfile,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

            DrainedRows expected = Drain(complete);
            DrainedRows actual = Drain(cold);

            Assert.Equal(
                expected.Rows.Select(RowValue),
                actual.Rows.Select(RowValue));
            Assert.Equal(expected.ExactCount, actual.ExactCount);
        }
    }

    [Fact]
    public void AdaptiveSelectorUsesDecodedLengthAndClosedDemand()
    {
        var profile = new SourceLineDeliveryProfile(
            "test/selector",
            "test-1",
            exactCountThresholdUtf16: 3,
            boundedRowsThresholdUtf16: 4,
            unboundedRowsThresholdUtf16: 5);
        var document = new DecodedTextDocument("abc");
        using SourceLineExecution count =
            SourceLineExecution.Create(
                document,
                QuerySpaceTerminalRequirement.Count,
                SourceLineVocabulary.EmptyPlan,
                profile,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        using SourceLineExecution bounded =
            SourceLineExecution.Create(
                document,
                QuerySpaceTerminalRequirement.Rows,
                HeadPlan(1),
                profile,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        using SourceLineExecution unbounded =
            SourceLineExecution.Create(
                document,
                QuerySpaceTerminalRequirement.Rows,
                SourceLineVocabulary.EmptyPlan,
                profile,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        using SourceLineExecution unsupportedProfile =
            SourceLineExecution.Create(
                document,
                QuerySpaceTerminalRequirement.Count,
                SourceLineVocabulary.EmptyPlan,
                deliveryProfile: null,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            SourceLineExecutionStrategy.ColdPull,
            count.Strategy);
        Assert.Equal(3, count.DecodedUtf16Length);
        Assert.Equal("test/selector", count.DeliveryProfileIdentity);
        Assert.Equal("test-1", count.PolicyGeneration);
        Assert.Equal(3, count.SelectedThresholdUtf16);
        Assert.Equal(
            SourceLineExecutionStrategy.Complete,
            bounded.Strategy);
        Assert.Equal(
            SourceLineExecutionStrategy.Complete,
            unbounded.Strategy);
        Assert.Equal(
            SourceLineExecutionStrategy.Complete,
            unsupportedProfile.Strategy);
        Assert.Null(unsupportedProfile.DeliveryProfileIdentity);
        Assert.Null(unsupportedProfile.PolicyGeneration);
        Assert.Null(unsupportedProfile.SelectedThresholdUtf16);
    }

    [Fact]
    public void ColdCountStartsOnFirstPullAndProjectsNoRows()
    {
        using SourceLineExecution execution =
            SourceLineExecution.Create(
                new DecodedTextDocument("one\ntwo\nthree"),
                QuerySpaceTerminalRequirement.Count,
                SourceLineVocabulary.EmptyPlan,
                s_coldProfile,
                new SourceLineExecutionBounds(
                    maximumCandidateRows: 1,
                    maximumUtf16CodeUnits: 32,
                    maximumJsonEncodedUtf8Bytes: 64),
                TestContext.Current.CancellationToken);

        Assert.Equal(SourceLineDemand.ExactCount, execution.Demand);
        Assert.Equal(
            new SourceLineExecutionObservation(
                SourceLineExecutionStrategy.ColdPull,
                SourceLineDemand.ExactCount,
                PullCount: 0,
                CandidateLinesScanned: 0,
                SelectedLines: 0,
                ProjectedRows: 0),
            execution.Observation);

        SourceLineExecutionBatch first =
            execution.Pull(TestContext.Current.CancellationToken);

        Assert.Empty(first.Rows);
        Assert.False(first.IsComplete);
        Assert.Null(first.ExactCount);
        DrainedRows drained = Drain(execution);
        Assert.Empty(drained.Rows);
        Assert.Equal(3, drained.ExactCount);
        Assert.Equal(3, execution.Observation.CandidateLinesScanned);
        Assert.Equal(0, execution.Observation.ProjectedRows);
    }

    [Fact]
    public void FilteredHeadStopsBeforeOversizedTail()
    {
        string text =
            "drop\nkeep one\nkeep two\n"
            + new string('x', 1_000);
        ForwardRowQueryPlan<SourceLineCandidate> plan =
            Plan(pattern: "keep*", head: 2);
        var bounds = new SourceLineExecutionBounds(
            maximumCandidateRows: 10,
            maximumUtf16CodeUnits: 16,
            maximumJsonEncodedUtf8Bytes: 64);
        using SourceLineExecution cold =
            SourceLineExecution.Create(
                new DecodedTextDocument(text),
                QuerySpaceTerminalRequirement.Rows,
                plan,
                s_coldProfile,
                bounds,
                TestContext.Current.CancellationToken);
        using SourceLineExecution complete =
            SourceLineExecution.Create(
                new DecodedTextDocument(text),
                QuerySpaceTerminalRequirement.Rows,
                plan,
                bounds: bounds,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        DrainedRows coldRows = Drain(cold);
        DrainedRows completeRows = Drain(complete);

        Assert.Equal(
            ["keep one", "keep two"],
            coldRows.Rows.Select(row => row.Content));
        Assert.Equal(
            completeRows.Rows.Select(RowValue),
            coldRows.Rows.Select(RowValue));
        Assert.Equal(2, coldRows.ExactCount);
        Assert.Equal(3, cold.Observation.CandidateLinesScanned);
        Assert.Equal(4, complete.Observation.CandidateLinesScanned);
    }

    [Fact]
    public void RequiredOversizedLineFailsRowsButCountNeedsNoFrame()
    {
        string text = "ok\n" + new string('x', 100);
        var bounds = new SourceLineExecutionBounds(
            maximumCandidateRows: 10,
            maximumUtf16CodeUnits: 16,
            maximumJsonEncodedUtf8Bytes: 64);
        using SourceLineExecution rows =
            SourceLineExecution.Create(
                new DecodedTextDocument(text),
                QuerySpaceTerminalRequirement.Rows,
                SourceLineVocabulary.EmptyPlan,
                s_coldProfile,
                bounds,
                TestContext.Current.CancellationToken);

        SourceLineExecutionBatch prefix =
            rows.Pull(TestContext.Current.CancellationToken);

        Assert.Equal("ok", Assert.Single(prefix.Rows).Content);
        Assert.False(prefix.IsComplete);
        DecodedTextLineLimitException rowsFailure =
            Assert.Throws<DecodedTextLineLimitException>(
                () => rows.Pull(
                    TestContext.Current.CancellationToken));
        Assert.Equal(2, rowsFailure.LineNumber);
        DecodedTextLineLimitException repeatedFailure =
            Assert.Throws<DecodedTextLineLimitException>(
                () => rows.Pull(
                    TestContext.Current.CancellationToken));
        Assert.Equal(2, repeatedFailure.LineNumber);
        Assert.Equal(2, rows.Observation.CandidateLinesScanned);
        DecodedTextLineLimitException completeRowsFailure =
            Assert.Throws<DecodedTextLineLimitException>(
                () => SourceLineExecution.Create(
                    new DecodedTextDocument(text),
                    QuerySpaceTerminalRequirement.Rows,
                    SourceLineVocabulary.EmptyPlan,
                    bounds: bounds,
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        Assert.Equal(2, completeRowsFailure.LineNumber);

        using SourceLineExecution count =
            SourceLineExecution.Create(
                new DecodedTextDocument(text),
                QuerySpaceTerminalRequirement.Count,
                SourceLineVocabulary.EmptyPlan,
                s_coldProfile,
                bounds,
                TestContext.Current.CancellationToken);
        DrainedRows counted = Drain(count);
        Assert.Equal(2, counted.ExactCount);
        Assert.Empty(counted.Rows);
        Assert.Equal(2, count.Observation.CandidateLinesScanned);
        Assert.Equal(0, count.Observation.ProjectedRows);
        using SourceLineExecution completeCount =
            SourceLineExecution.Create(
                new DecodedTextDocument(text),
                QuerySpaceTerminalRequirement.Count,
                SourceLineVocabulary.EmptyPlan,
                bounds: bounds,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.Equal(2, Drain(completeCount).ExactCount);
    }

    [Fact]
    public void DifferentExecutionBoundsPreserveRowsAndCount()
    {
        const string text =
            "one\ntwo\nthree\nfour\nfive";
        SourceLineExecutionBounds[] boundsCases =
        [
            new(1, 8, 32),
            new(3, 16, 64),
            new(10, 128, 256),
        ];
        DrainedRows? expected = null;
        foreach (SourceLineExecutionBounds bounds in boundsCases)
        {
            using SourceLineExecution execution =
                SourceLineExecution.Create(
                    new DecodedTextDocument(text),
                    QuerySpaceTerminalRequirement.Rows,
                    SourceLineVocabulary.EmptyPlan,
                    s_coldProfile,
                    bounds,
                    TestContext.Current.CancellationToken);
            DrainedRows actual = Drain(execution, bounds);
            expected ??= actual;

            Assert.Equal(
                expected.Rows.Select(RowValue),
                actual.Rows.Select(RowValue));
            Assert.Equal(expected.ExactCount, actual.ExactCount);
        }
    }

    [Fact]
    public void CancellationAndDisposalStopColdProduction()
    {
        using var cancellation = new CancellationTokenSource();
        using SourceLineExecution cancelled =
            SourceLineExecution.Create(
                new DecodedTextDocument("one\ntwo"),
                QuerySpaceTerminalRequirement.Rows,
                SourceLineVocabulary.EmptyPlan,
                s_coldProfile,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => cancelled.Pull(cancellation.Token));
        Assert.Throws<ObjectDisposedException>(
            () => cancelled.Pull(
                TestContext.Current.CancellationToken));
        Assert.Equal(0, cancelled.Observation.CandidateLinesScanned);

        SourceLineExecution disposed =
            SourceLineExecution.Create(
                new DecodedTextDocument("one"),
                QuerySpaceTerminalRequirement.Rows,
                SourceLineVocabulary.EmptyPlan,
                s_coldProfile,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => disposed.Pull(
                TestContext.Current.CancellationToken));
    }

    private static ForwardRowQueryPlan<SourceLineCandidate> Plan(
        string pattern,
        int head)
    {
        RowQueryIntent intent =
            RowQueryIntent.Create(
                [
                    new RowQueryPredicateIntent(
                        SourceLineVocabulary.ContentKey,
                        RowQueryOperator.Equals,
                        new RowQueryValueToken(pattern)),
                ],
                baselineOrder: null,
                RowSelectionIntent<RowQueryOrderIntent>.Create(
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Head(head),
                    ]));
        RowQueryResolutionResult<SourceLineCandidate> resolution =
            SourceLineVocabulary.Resolve(intent);
        ResolvedRowQueryPlan<SourceLineCandidate> resolved =
            Assert.IsType<ResolvedRowQueryPlan<SourceLineCandidate>>(
                resolution.Plan);
        Assert.True(
            ForwardRowQueryPlanner.TryCreate(
                resolved,
                out ForwardRowQueryPlan<SourceLineCandidate>? plan));
        return plan;
    }

    private static ForwardRowQueryPlan<SourceLineCandidate> HeadPlan(
        int head)
    {
        RowQueryIntent intent =
            RowQueryIntent.Create(
                [],
                baselineOrder: null,
                RowSelectionIntent<RowQueryOrderIntent>.Create(
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Head(head),
                    ]));
        ResolvedRowQueryPlan<SourceLineCandidate> resolved =
            Assert.IsType<ResolvedRowQueryPlan<SourceLineCandidate>>(
                SourceLineVocabulary.Resolve(intent).Plan);
        Assert.True(
            ForwardRowQueryPlanner.TryCreate(
                resolved,
                out ForwardRowQueryPlan<SourceLineCandidate>? plan));
        return plan;
    }

    private static DrainedRows Drain(
        SourceLineExecution execution,
        SourceLineExecutionBounds? bounds = null)
    {
        var rows = new List<SourceViewLine>();
        int pulls = 0;
        int? exactCount = null;
        while (exactCount is null)
        {
            SourceLineExecutionBatch batch =
                execution.Pull(
                    TestContext.Current.CancellationToken);
            pulls++;
            if (bounds is not null)
                AssertWithinBounds(batch, bounds);
            rows.AddRange(batch.Rows);
            exactCount = batch.ExactCount;
            Assert.Equal(batch.IsComplete, exactCount is not null);
            Assert.InRange(pulls, 1, 1_000);
        }

        return new(rows, exactCount.Value, pulls);
    }

    private static void AssertWithinBounds(
        SourceLineExecutionBatch batch,
        SourceLineExecutionBounds bounds)
    {
        int utf16CodeUnits = 0;
        int jsonEncodedUtf8Bytes = 0;
        foreach (SourceViewLine line in batch.Rows)
        {
            string rowText = line.Content + line.TerminatorText;
            utf16CodeUnits = checked(
                utf16CodeUnits + rowText.Length);
            jsonEncodedUtf8Bytes = checked(
                jsonEncodedUtf8Bytes
                + JsonEncodedText.Encode(rowText)
                    .EncodedUtf8Bytes.Length);
        }

        Assert.InRange(
            batch.Rows.Length,
            0,
            bounds.MaximumCandidateRows);
        Assert.InRange(
            utf16CodeUnits,
            0,
            bounds.MaximumUtf16CodeUnits);
        Assert.InRange(
            jsonEncodedUtf8Bytes,
            0,
            bounds.MaximumJsonEncodedUtf8Bytes);
    }

    private static (
        int Number,
        int Start,
        string Content,
        SourceViewLineTerminator Terminator)
        RowValue(SourceViewLine line) =>
        (
            line.Number,
            line.Start,
            line.Content,
            line.Terminator);

    private sealed record DrainedRows(
        IReadOnlyList<SourceViewLine> Rows,
        int ExactCount,
        int PullCount);
}
