using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using QueryOverflow;
using QuerySpace.Composition;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    // PR-fast: the joined operation preserves Source evidence and line meaning
    // across host delivery credit and Source-selected batch bounds.
    [Fact]
    public async Task SourceLineOperationPreservesMeaningAcrossPullBounds()
    {
        string text = string.Join(
            "\n",
            Enumerable.Range(1, 600).Select(
                static value => $"line {value}"));
        InspectionEnvelope<SourceView> inspection =
            await ProjectTypeView(text);
        ResolvedRowQueryPlan<SourceViewLine> plan = ResolveLines();

        SourceViewLineOperation narrow =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    inspection,
                    plan));
        IReadOnlyList<SourceViewLine> narrowRows =
            DrainRows(narrow, finalRowCredit: 17);

        SourceViewLineOperation wide =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    inspection,
                    plan));
        IReadOnlyList<SourceViewLine> wideRows =
            DrainRows(wide, finalRowCredit: 1_000);

        Assert.Equal(600, narrowRows.Count);
        Assert.Equal(narrowRows, wideRows);
        Assert.Equal(text, Reconstruct(narrowRows));
        Assert.Same(inspection, narrow.Inspection);
        Assert.Same(inspection.Share, narrow.Inspection.Share);
        Assert.Equal(
            inspection.Diagnostics,
            narrow.Inspection.Diagnostics);

        SourceViewLineOperation count =
            Accepted(
                SourceViewLineOperation.AdmitCount(
                    inspection,
                    plan));
        SourceViewLineSegment countResult = DrainCount(count);
        Assert.True(countResult.HasCount);
        Assert.Equal(600, countResult.Count);
        Assert.Empty(countResult.Rows);
        Assert.Equal(600, count.CandidateRowsConsumed);
    }

    // PR-fast: Head limits decoded work instead of scanning an open-ended
    // population and discarding the excess.
    [Fact]
    public async Task SourceLineHeadPullsOnlyTheRequiredPrefix()
    {
        string text = string.Join(
            "\n",
            Enumerable.Range(1, 1_000).Select(
                static value => $"line {value}"));
        InspectionEnvelope<SourceView> inspection =
            await ProjectTypeView(text);
        SourceViewLineOperation operation =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    inspection,
                    ResolveLines(head: 100)));

        Assert.True(
            Pull(
                operation,
                continuation: null,
                finalRowCredit: 1_000,
                out SourceViewLineSegment? segment));
        Assert.NotNull(segment);
        Assert.True(segment.IsComplete);
        Assert.Equal(100, segment.Rows.Count);
        Assert.Equal(100, operation.CandidateRowsConsumed);
        Assert.Equal(100, operation.PublishedRows);
        Assert.Equal(
            SourceViewLineOperationState.Completed,
            operation.State);
        Assert.False(
            Pull(
                operation,
                continuation: null,
                finalRowCredit: 1,
                out _));
    }

    // PR-fast: process-local continuations are bound to one exact operation,
    // Source view binding, semantic selection, and generation.
    [Fact]
    public async Task SourceLineContinuationRejectsIncompatibleUse()
    {
        InspectionEnvelope<SourceView> firstInspection =
            await ProjectTypeView("one\ntwo\nthree");
        SourceViewLineOperation first =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    firstInspection,
                    ResolveLines()));

        Assert.True(
            Pull(
                first,
                continuation: null,
                finalRowCredit: 1,
                out SourceViewLineSegment? firstSegment));
        SourceViewLineContinuation firstContinuation =
            Assert.IsType<SourceViewLineContinuation>(
                firstSegment!.Continuation);
        Assert.Same(first.Identity, firstContinuation.Operation);
        Assert.Same(
            firstInspection.Content.Binding,
            firstContinuation.Binding);

        Assert.Throws<ArgumentException>(
            () => Pull(
                first,
                continuation: null,
                finalRowCredit: 1,
                out _));

        SourceViewLineOperation differentSelection =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    firstInspection,
                    ResolveLines(head: 2)));
        Assert.Throws<ArgumentException>(
            () => Pull(
                differentSelection,
                firstContinuation,
                finalRowCredit: 1,
                out _));

        InspectionEnvelope<SourceView> differentInspection =
            await ProjectTypeView("one\ntwo\nthree");
        SourceViewLineOperation differentBinding =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    differentInspection,
                    ResolveLines()));
        Assert.NotEqual(
            firstInspection.Content.Binding,
            differentInspection.Content.Binding);
        Assert.Throws<ArgumentException>(
            () => Pull(
                differentBinding,
                firstContinuation,
                finalRowCredit: 1,
                out _));

        Assert.True(
            Pull(
                first,
                firstContinuation,
                finalRowCredit: 1,
                out SourceViewLineSegment? secondSegment));
        SourceViewLineContinuation secondContinuation =
            Assert.IsType<SourceViewLineContinuation>(
                secondSegment!.Continuation);
        Assert.Throws<ArgumentException>(
            () => Pull(
                first,
                firstContinuation,
                finalRowCredit: 1,
                out _));
        Assert.True(
            Pull(
                first,
                secondContinuation,
                finalRowCredit: 1,
                out SourceViewLineSegment? completed));
        Assert.True(completed!.IsComplete);
        Assert.Throws<ArgumentException>(
            () => Pull(
                first,
                secondContinuation,
                finalRowCredit: 1,
                out _));
    }

    // PR-fast: Source applies both content ceilings before publishing a
    // complete segment and fails an individually oversized line visibly.
    [Fact]
    public async Task SourceLineSegmentsRespectExecutionBounds()
    {
        string text = string.Join(
            "\n",
            Enumerable.Repeat(new string('a', 200), 300));
        SourceViewLineOperation bounded =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    await ProjectTypeView(text),
                    ResolveLines()));

        Assert.True(
            Pull(
                bounded,
                continuation: null,
                finalRowCredit: 1_000,
                out SourceViewLineSegment? first));
        Assert.NotNull(first!.Continuation);
        Assert.InRange(
            first.Rows.Count,
            1,
            SourceViewLineExecutionPolicy.MaximumCandidateRows);
        Assert.InRange(
            first.Rows.Sum(
                static line =>
                    line.Content.Length
                    + line.TerminatorText.Length),
            1,
            SourceViewLineExecutionPolicy.MaximumUtf16CodeUnits);

        SourceViewLineOperation oversized =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    await ProjectTypeView(
                        new string('\u0080', 12_000)),
                    ResolveLines()));
        Assert.Throws<Inspector.Text.DecodedTextLineLimitException>(
            () => Pull(
                oversized,
                continuation: null,
                finalRowCredit: 1,
                out _));
        Assert.Equal(
            SourceViewLineOperationState.Failed,
            oversized.State);
        Assert.Equal(0, oversized.CandidateRowsConsumed);
    }

    // PR-fast: unsupported QueryOverflow shapes decline before decoded-line
    // work, while cancellation is a visible terminal Source state.
    [Fact]
    public async Task SourceLineOperationDeclinesAndCancelsVisibly()
    {
        InspectionEnvelope<SourceView> inspection =
            await ProjectTypeView("one\ntwo");
        SourceViewLineOperationAdmission declined =
            SourceViewLineOperation.AdmitRows(
                inspection,
                ResolveLines(withPredicate: true));
        Assert.False(declined.IsAccepted);
        Assert.Null(declined.Operation);
        Assert.Equal(
            QueryOverflowDeclineReason
                .RowsWithPredicatesRequireAtomicPublication,
            declined.DeclineReason);

        SourceViewLineOperation cancelled =
            Accepted(
                SourceViewLineOperation.AdmitRows(
                    inspection,
                    ResolveLines()));
        cancelled.Cancel();
        Assert.Equal(
            SourceViewLineOperationState.Cancelled,
            cancelled.State);
        Assert.Throws<InvalidOperationException>(
            () => Pull(
                cancelled,
                continuation: null,
                finalRowCredit: 1,
                out _));
    }

    private static async Task<InspectionEnvelope<SourceView>>
        ProjectTypeView(string text)
    {
        TestAssembly assembly =
            TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourcePairBytes(FixtureCatalog.SourceDiffV1));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);
        InspectionEnvelope<AssemblyTypeSourceEntry> template =
            await TypeSourceInspection.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest("Counter"),
                host.Context,
                TestContext.Current.CancellationToken);
        return Assert.IsType<
                SourceViewProjection<AssemblyTypeSourceEntry>
                    .Available>(
                SourceViewInspection.Project(
                    WithTypeText(template, text)))
            .Inspection;
    }

    private static ResolvedRowQueryPlan<SourceViewLine> ResolveLines(
        int? head = null,
        bool withPredicate = false)
    {
        IReadOnlyList<RowQueryPredicateIntent> predicates;
        if (!withPredicate)
        {
            predicates = [];
        }
        else
        {
            predicates =
            [
                new(
                    SourceViewLineVocabulary.NumberKey,
                    RowQueryOperator.GreaterOrEqual,
                    new("1")),
            ];
        }

        IReadOnlyList<
            RowSelectionIntentOperation<RowQueryOrderIntent>>
            selection = head is { } count
                ?
                [
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Head(count),
                ]
                : [];
        RowQueryResolutionResult<SourceViewLine> resolution =
            SourceViewLineVocabulary.Resolve(
                RowQueryIntent.Create(
                    predicates,
                    baselineOrder: null,
                    RowSelectionIntent<
                        RowQueryOrderIntent>.Create(selection)));
        return Assert.IsType<ResolvedRowQueryPlan<SourceViewLine>>(
            resolution.Plan);
    }

    private static SourceViewLineOperation Accepted(
        SourceViewLineOperationAdmission admission)
    {
        Assert.True(
            admission.IsAccepted,
            $"Source line operation declined: {admission.DeclineReason}.");
        Assert.Null(admission.DeclineReason);
        return Assert.IsType<SourceViewLineOperation>(
            admission.Operation);
    }

    private static IReadOnlyList<SourceViewLine> DrainRows(
        SourceViewLineOperation operation,
        int finalRowCredit)
    {
        var rows = new List<SourceViewLine>();
        SourceViewLineContinuation? continuation = null;
        while (Pull(
            operation,
            continuation,
            finalRowCredit,
            out SourceViewLineSegment? segment))
        {
            Assert.NotNull(segment);
            rows.AddRange(segment.Rows);
            continuation = segment.Continuation;
        }
        Assert.Equal(
            SourceViewLineOperationState.Completed,
            operation.State);
        return rows;
    }

    private static SourceViewLineSegment DrainCount(
        SourceViewLineOperation operation)
    {
        SourceViewLineContinuation? continuation = null;
        SourceViewLineSegment? last = null;
        while (Pull(
            operation,
            continuation,
            finalRowCredit: 0,
            out SourceViewLineSegment? segment))
        {
            last = segment;
            continuation = segment!.Continuation;
        }
        Assert.Equal(
            SourceViewLineOperationState.Completed,
            operation.State);
        return Assert.IsType<SourceViewLineSegment>(last);
    }

    private static string Reconstruct(
        IReadOnlyList<SourceViewLine> lines) =>
        string.Concat(
            lines.Select(
                static line =>
                    line.Content + line.TerminatorText));

    private static bool Pull(
        SourceViewLineOperation operation,
        SourceViewLineContinuation? continuation,
        int finalRowCredit,
        out SourceViewLineSegment? segment) =>
        operation.TryPull(
            continuation,
            finalRowCredit,
            out segment,
            TestContext.Current.CancellationToken);
}
