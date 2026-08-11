using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Inputs for composing one already-acquired graph layer with portable
/// annotated source for the selected member.
/// </summary>
public sealed record AnnotatedMemberDocumentInput(
    MetadataSource Source,
    MemberCallGraphView CallGraph,
    AnnotationStage Stage = AnnotationStage.Raised,
    PrinterOptions? PrinterOptions = null);

/// <summary>
/// One physical call occurrence joined to both a stable graph edge row and a
/// portable source fact. The fact reaches source through the document's sole
/// fact-to-node target relation.
/// </summary>
public readonly record struct AnnotatedCallGraphOccurrence(
    int EdgeRow,
    int FactId,
    Guid ModuleVersionId,
    int CallerToken,
    int ILOffset,
    int OperandToken,
    CallKind Kind,
    bool InLoop);

/// <summary>One bounded graph layer attached to an annotated source document.</summary>
public sealed record AnnotatedCallGraphOverlay(
    CallGraphTier Tier,
    CallGraphProjection Projection,
    ImmutableArray<AnnotatedCallGraphOccurrence> Occurrences,
    CatalogCallGraphDiagnostics Diagnostics);

/// <summary>
/// Portable annotated source plus a bounded, format-neutral relationship
/// overlay over that source.
/// </summary>
public sealed record AnnotatedMemberDocument(
    AnnotatedSourceDocument Source,
    AnnotatedCallGraphOverlay CallGraph);

/// <summary>Result of composing source and graph evidence.</summary>
public abstract record AnnotatedMemberDocumentResult
{
    private AnnotatedMemberDocumentResult()
    {
    }

    public sealed record Complete(AnnotatedMemberDocument Document)
        : AnnotatedMemberDocumentResult;

    public sealed record Failed(DecompilerResult Failure)
        : AnnotatedMemberDocumentResult;
}

/// <summary>
/// Composes an already-acquired progressive graph layer with one portable
/// source document. It performs no graph or Analysis acquisition.
/// </summary>
/// <remarks>
/// <c>AnnotatedMemberDocument_ReusesCalleeLayerAndMapsEveryPhysicalCallSite</c>
/// gates graph-session reuse and the unchanged graph build counts.
/// <c>RequirementsNone_DoesNotResolveAnAssemblyContext</c> gates the Research
/// acquisition boundary used by the call-only profile.
/// </remarks>
public static class AnnotatedMemberDocumentQuery
{
    public static InspectionQuery<AnnotatedMemberDocumentResult> Definition
        { get; } =
        new("Annotated member document", InspectionCost.NetworkFree);

    public static AnnotatedMemberDocumentResult Execute(
        AnnotatedMemberDocumentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Source);
        ArgumentNullException.ThrowIfNull(input.CallGraph);

        MemberCallGraphView graphView = input.CallGraph;
        if (graphView.CalleeRoot is null)
        {
            return Failure(
                "A call relationship overlay requires a callee topology.");
        }
        if (graphView.FocusModuleVersionId
                != input.Source.ModuleVersionId)
        {
            return Failure(
                "The annotated source module does not match the call-graph focus.");
        }

        CallGraphProjection projection = CallGraphProjection.Create(
            graphView.CallerRoot,
            graphView.CalleeRoot);
        var mappedCalls =
            ImmutableArray.CreateBuilder<(
                DirectCall Call,
                CallGraphRow Row)>();
        bool focusIsBudgetLimited =
            graphView.CalleeRoot.Status
                is CallTreeStatus.DepthLimited
                or CallTreeStatus.Truncated;
        foreach (DirectCall call in graphView.FocusCallSites)
        {
            CallGraphRowMatch match =
                projection.FindFocusCalleeRow(call, out CallGraphRow row);
            if (match == CallGraphRowMatch.Found)
            {
                mappedCalls.Add((call, row));
                continue;
            }
            if (match == CallGraphRowMatch.NotProjected
                && focusIsBudgetLimited)
            {
                continue;
            }

            return Failure(
                match == CallGraphRowMatch.Ambiguous
                    ? $"Call site IL_{call.ILOffset:X4} maps to more than one stable graph edge."
                    : $"Call site IL_{call.ILOffset:X4} could not be joined to one stable graph edge.");
        }

        var sourceProjection = ResearchViews.ProjectMember(
            new ResearchViews.MemberProjectionRequest(
                input.Source,
                projection.Focus.Member.DeclaringType
                    .ToQualifiedDisplayString(),
                projection.Focus.Member.Name,
                AnnotatedStage: input.Stage,
                Registry: ResearchFactRegistry.CallRelationships,
                MethodToken: graphView.FocusMethodToken,
                PrinterOptions: input.PrinterOptions,
                SourceDocument: true,
                CallSites:
                [
                    .. mappedCalls.Select(mapped => mapped.Call),
                ]));
        if (sourceProjection.SourceDocument is not { } source)
        {
            return new AnnotatedMemberDocumentResult.Failed(
                sourceProjection.SourceDocumentFailure
                ?? DecompilerResult.Failure(
                    DiagnosticIds.InternalError,
                    "Annotated source production returned no document."));
        }

        if (graphView.FocusMethodToken
                != sourceProjection.SelectedMethodToken)
        {
            return Failure(
                "The annotated source member does not match the call-graph focus.");
        }

        var factsByOffset = source.Facts
            .Where(fact =>
                fact.Descriptor
                    == ResearchFactRegistry.CallRelationshipDescriptorId)
            .ToDictionary(fact => fact.SourceOffset);
        var occurrences =
            ImmutableArray.CreateBuilder<AnnotatedCallGraphOccurrence>(
                mappedCalls.Count);
        foreach ((DirectCall call, CallGraphRow row) in mappedCalls)
        {
            if (!factsByOffset.TryGetValue(
                    call.ILOffset,
                    out AnnotatedSourceFact fact))
            {
                return Failure(
                    $"Call site IL_{call.ILOffset:X4} has no portable source fact.");
            }

            occurrences.Add(
                new AnnotatedCallGraphOccurrence(
                    row.Number,
                    fact.Id,
                    call.Caller.ModuleVersionId,
                    call.Caller.MetadataToken,
                    call.ILOffset,
                    call.OperandToken,
                    call.Kind,
                    call.InLoop));
        }

        return new AnnotatedMemberDocumentResult.Complete(
            new AnnotatedMemberDocument(
                source,
                new AnnotatedCallGraphOverlay(
                    graphView.Tier,
                    projection,
                    occurrences.MoveToImmutable(),
                    graphView.Diagnostics)));
    }

    static AnnotatedMemberDocumentResult.Failed Failure(
        string message) =>
        new(
            DecompilerResult.Failure(
                DiagnosticIds.InternalError,
                message));
}
